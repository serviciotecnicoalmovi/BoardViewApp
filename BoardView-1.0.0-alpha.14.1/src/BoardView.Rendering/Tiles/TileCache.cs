using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BoardView.Rendering.Tiles;

/// <summary>
/// Almacena teselas renderizadas mediante una estrategia LRU
/// (Least Recently Used).
///
/// Cuando la memoria utilizada supera el límite configurado,
/// la caché elimina primero las teselas que llevan más tiempo
/// sin utilizarse.
///
/// Esta clase es segura para acceso desde varios hilos.
/// </summary>
public sealed class TileCache
{
    /*
     * El diccionario permite localizar rápidamente una tesela
     * utilizando su TileKey.
     *
     * Cada elemento apunta además a su nodo dentro de la lista LRU.
     */
    private readonly Dictionary<TileKey, LinkedListNode<CacheEntry>> _entries =
        new();

    /*
     * El primer nodo representa la tesela usada más recientemente.
     * El último nodo representa la menos utilizada.
     */
    private readonly LinkedList<CacheEntry> _usageOrder = new();

    /*
     * Protege el estado interno cuando el renderizador asíncrono
     * comience a solicitar y almacenar teselas desde varios hilos.
     */
    private readonly object _syncRoot = new();

    private long _currentSizeInBytes;

    /// <summary>
    /// Inicializa una caché con un límite máximo de memoria.
    /// </summary>
    /// <param name="maxSizeInBytes">
    /// Cantidad máxima de bytes que podrá conservar la caché.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Se produce si el tamaño máximo es menor o igual que cero.
    /// </exception>
    public TileCache(long maxSizeInBytes)
    {
        if (maxSizeInBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSizeInBytes),
                maxSizeInBytes,
                "El tamaño máximo de la caché debe ser mayor que cero.");
        }

        MaxSizeInBytes = maxSizeInBytes;
    }

    /// <summary>
    /// Obtiene el límite máximo de memoria configurado.
    /// </summary>
    public long MaxSizeInBytes { get; }

    /// <summary>
    /// Obtiene la cantidad de memoria utilizada actualmente.
    /// </summary>
    public long CurrentSizeInBytes
    {
        get
        {
            lock (_syncRoot)
            {
                return _currentSizeInBytes;
            }
        }
    }

    /// <summary>
    /// Obtiene la cantidad de teselas almacenadas.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_syncRoot)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// Intenta recuperar una tesela almacenada.
    ///
    /// Cuando la tesela existe, se marca automáticamente como
    /// la tesela utilizada más recientemente.
    /// </summary>
    /// <param name="key">
    /// Clave de la tesela solicitada.
    /// </param>
    /// <param name="tile">
    /// Tesela encontrada o null cuando no existe.
    /// </param>
    /// <returns>
    /// true si la tesela existe; de lo contrario, false.
    /// </returns>
    public bool TryGet(
    TileKey key,
    [NotNullWhen(true)] out Tile? tile)
    {
        lock (_syncRoot)
        {
            if (!_entries.TryGetValue(key, out LinkedListNode<CacheEntry>? node))
            {
                tile = null;
                return false;
            }

            /*
             * La tesela acaba de utilizarse, por lo que debe pasar
             * al comienzo de la lista LRU.
             */
            MarkAsMostRecentlyUsed(node);

            tile = node.Value.Tile;
            return true;
        }
    }

    /// <summary>
    /// Agrega una nueva tesela o reemplaza la existente.
    ///
    /// Si la caché supera su límite, elimina automáticamente
    /// las teselas menos utilizadas.
    /// </summary>
    /// <param name="tile">
    /// Tesela que será almacenada.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Se produce si la tesela recibida es null.
    /// </exception>
    public void AddOrUpdate(Tile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        lock (_syncRoot)
        {
            /*
             * Una tesela individual mayor que toda la capacidad
             * no puede almacenarse de forma útil.
             *
             * En ese caso se elimina cualquier versión anterior
             * de la misma clave y se abandona la operación.
             */
            if (tile.SizeInBytes > MaxSizeInBytes)
            {
                RemoveInternal(tile.Key);
                return;
            }

            if (_entries.TryGetValue(
                    tile.Key,
                    out LinkedListNode<CacheEntry>? existingNode))
            {
                /*
                 * Descuenta el tamaño de la versión anterior antes
                 * de reemplazarla.
                 */
                _currentSizeInBytes -= existingNode.Value.Tile.SizeInBytes;

                existingNode.Value = new CacheEntry(tile);

                _currentSizeInBytes = checked(
                    _currentSizeInBytes + tile.SizeInBytes);

                MarkAsMostRecentlyUsed(existingNode);
            }
            else
            {
                var entry = new CacheEntry(tile);

                LinkedListNode<CacheEntry> newNode =
                    _usageOrder.AddFirst(entry);

                _entries.Add(tile.Key, newNode);

                _currentSizeInBytes = checked(
                    _currentSizeInBytes + tile.SizeInBytes);
            }

            TrimToCapacity();
        }
    }

    /// <summary>
    /// Elimina una tesela concreta.
    /// </summary>
    /// <param name="key">
    /// Clave de la tesela que será eliminada.
    /// </param>
    /// <returns>
    /// true si la tesela existía; de lo contrario, false.
    /// </returns>
    public bool Remove(TileKey key)
    {
        lock (_syncRoot)
        {
            return RemoveInternal(key);
        }
    }

    /// <summary>
    /// Elimina todas las teselas correspondientes a una página.
    ///
    /// Será útil cuando una página se cierre, se vuelva a cargar
    /// o cambie su contenido.
    /// </summary>
    /// <param name="page">
    /// Índice de la página que será eliminada de la caché.
    /// </param>
    /// <returns>
    /// Cantidad de teselas eliminadas.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Se produce si el índice de página es negativo.
    /// </exception>
    public int RemovePage(int page)
    {
        if (page < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                page,
                "El índice de página no puede ser negativo.");
        }

        lock (_syncRoot)
        {
            /*
             * Se copian primero las claves para evitar modificar
             * el diccionario mientras se está recorriendo.
             */
            var keysToRemove = new List<TileKey>();

            foreach (TileKey key in _entries.Keys)
            {
                if (key.Page == page)
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (TileKey key in keysToRemove)
            {
                RemoveInternal(key);
            }

            return keysToRemove.Count;
        }
    }

    /// <summary>
    /// Elimina todas las teselas almacenadas.
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            _entries.Clear();
            _usageOrder.Clear();
            _currentSizeInBytes = 0;
        }
    }

    /// <summary>
    /// Marca una tesela como la utilizada más recientemente.
    /// </summary>
    private void MarkAsMostRecentlyUsed(
        LinkedListNode<CacheEntry> node)
    {
        if (ReferenceEquals(_usageOrder.First, node))
        {
            return;
        }

        _usageOrder.Remove(node);
        _usageOrder.AddFirst(node);
    }

    /// <summary>
    /// Elimina teselas antiguas hasta que el consumo de memoria
    /// vuelva a estar dentro del límite configurado.
    /// </summary>
    private void TrimToCapacity()
    {
        while (_currentSizeInBytes > MaxSizeInBytes)
        {
            LinkedListNode<CacheEntry>? leastRecentlyUsed =
                _usageOrder.Last;

            /*
             * Esta condición solo protege ante una inconsistencia
             * inesperada. Normalmente la lista siempre tendrá nodos
             * mientras el tamaño sea mayor que cero.
             */
            if (leastRecentlyUsed is null)
            {
                _currentSizeInBytes = 0;
                _entries.Clear();
                return;
            }

            RemoveInternal(leastRecentlyUsed.Value.Tile.Key);
        }
    }

    /// <summary>
    /// Elimina una tesela sin volver a adquirir el bloqueo.
    ///
    /// Este método solo debe llamarse mientras _syncRoot
    /// ya se encuentre bloqueado.
    /// </summary>
    private bool RemoveInternal(TileKey key)
    {
        if (!_entries.Remove(
                key,
                out LinkedListNode<CacheEntry>? node))
        {
            return false;
        }

        _usageOrder.Remove(node);

        _currentSizeInBytes -= node.Value.Tile.SizeInBytes;

        /*
         * La protección evita conservar un valor negativo
         * si el estado interno sufriera una inconsistencia.
         */
        if (_currentSizeInBytes < 0)
        {
            _currentSizeInBytes = 0;
        }

        return true;
    }

    /// <summary>
    /// Agrupa la información almacenada dentro de la lista LRU.
    /// </summary>
    private sealed class CacheEntry
    {
        public CacheEntry(Tile tile)
        {
            Tile = tile;
        }

        public Tile Tile { get; }
    }
}
