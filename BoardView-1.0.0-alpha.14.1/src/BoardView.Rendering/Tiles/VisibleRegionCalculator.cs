using System;
using System.Collections.Generic;
using System.Windows;

namespace BoardView.Rendering.Tiles;

/// <summary>
/// Calcula las teselas necesarias para cubrir una región visible.
///
/// La clase recibe:
/// - El número de página.
/// - El nivel de zoom.
/// - El tamaño de la página renderizada.
/// - El rectángulo visible del visor.
/// - El tamaño configurado para cada tesela.
///
/// Como resultado devuelve únicamente las claves de las teselas
/// que intersectan el área visible.
/// </summary>
public sealed class VisibleRegionCalculator
{
    /// <summary>
    /// Tamaño predeterminado de cada tesela en píxeles.
    /// </summary>
    public const int DefaultTileSize = 512;

    /// <summary>
    /// Inicializa el calculador de regiones visibles.
    /// </summary>
    /// <param name="tileSize">
    /// Tamaño cuadrado de cada tesela en píxeles.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Se produce si el tamaño recibido es menor o igual que cero.
    /// </exception>
    public VisibleRegionCalculator(int tileSize = DefaultTileSize)
    {
        if (tileSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tileSize),
                tileSize,
                "El tamaño de la tesela debe ser mayor que cero.");
        }

        TileSize = tileSize;
    }

    /// <summary>
    /// Obtiene el tamaño configurado para cada tesela.
    /// </summary>
    public int TileSize { get; }

    /// <summary>
    /// Calcula las teselas que deben estar disponibles para cubrir
    /// completamente el rectángulo visible.
    /// </summary>
    /// <param name="page">
    /// Índice de la página PDF.
    /// </param>
    /// <param name="zoomLevel">
    /// Nivel discreto de zoom utilizado por el motor de teselas.
    /// </param>
    /// <param name="pagePixelWidth">
    /// Ancho total de la página renderizada en píxeles.
    /// </param>
    /// <param name="pagePixelHeight">
    /// Alto total de la página renderizada en píxeles.
    /// </param>
    /// <param name="visibleRegion">
    /// Región actualmente visible, expresada en coordenadas de la página.
    /// </param>
    /// <returns>
    /// Lista de claves de tesela ordenadas de arriba hacia abajo
    /// y de izquierda a derecha.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Se produce si los índices o dimensiones no son válidos.
    /// </exception>
    public IReadOnlyList<TileKey> CalculateVisibleTiles(
        int page,
        int zoomLevel,
        int pagePixelWidth,
        int pagePixelHeight,
        Rect visibleRegion)
    {
        ValidateArguments(
            page,
            zoomLevel,
            pagePixelWidth,
            pagePixelHeight,
            visibleRegion);

        /*
         * Limita la región visible al área real de la página.
         * Esto evita solicitar teselas cuando el usuario desplaza
         * el visor fuera de los límites del documento.
         */
        Rect pageBounds = new(
            0d,
            0d,
            pagePixelWidth,
            pagePixelHeight);

        Rect clippedRegion = Rect.Intersect(
            pageBounds,
            visibleRegion);

        /*
         * Rect.Intersect devuelve Rect.Empty cuando las regiones
         * no tienen ninguna intersección.
         */
        if (clippedRegion.IsEmpty)
        {
            return Array.Empty<TileKey>();
        }

        int firstTileX = GetTileIndex(clippedRegion.Left);
        int firstTileY = GetTileIndex(clippedRegion.Top);

        /*
         * Se utiliza una pequeña corrección para el borde derecho
         * e inferior. Sin ella, un rectángulo que termina exactamente
         * al comienzo de otra tesela incluiría una tesela adicional.
         */
        double inclusiveRight = Math.Max(
            clippedRegion.Left,
            clippedRegion.Right - double.Epsilon);

        double inclusiveBottom = Math.Max(
            clippedRegion.Top,
            clippedRegion.Bottom - double.Epsilon);

        int lastTileX = GetTileIndex(inclusiveRight);
        int lastTileY = GetTileIndex(inclusiveBottom);

        int columnCount = checked(
            lastTileX - firstTileX + 1);

        int rowCount = checked(
            lastTileY - firstTileY + 1);

        int capacity = checked(
            columnCount * rowCount);

        var result = new List<TileKey>(capacity);

        /*
         * El orden fila por fila es intencional.
         * Más adelante permitirá que el planificador renderice
         * las teselas en un orden predecible.
         */
        for (int tileY = firstTileY;
             tileY <= lastTileY;
             tileY++)
        {
            for (int tileX = firstTileX;
                 tileX <= lastTileX;
                 tileX++)
            {
                result.Add(
                    new TileKey(
                        page,
                        zoomLevel,
                        tileX,
                        tileY));
            }
        }

        return result;
    }

    /// <summary>
    /// Calcula el rectángulo ocupado por una tesela dentro de la página.
    ///
    /// Las teselas situadas en los bordes pueden ser más pequeñas
    /// que TileSize.
    /// </summary>
    /// <param name="key">
    /// Clave de la tesela.
    /// </param>
    /// <param name="pagePixelWidth">
    /// Ancho total de la página en píxeles.
    /// </param>
    /// <param name="pagePixelHeight">
    /// Alto total de la página en píxeles.
    /// </param>
    /// <returns>
    /// Rectángulo de la tesela limitado al área real de la página.
    /// </returns>
    public Int32Rect CalculateTileBounds(
        TileKey key,
        int pagePixelWidth,
        int pagePixelHeight)
    {
        if (key.Page < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(key),
                key,
                "El índice de página no puede ser negativo.");
        }

        if (key.ZoomLevel < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(key),
                key,
                "El nivel de zoom no puede ser negativo.");
        }

        if (key.TileX < 0 || key.TileY < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(key),
                key,
                "Las coordenadas de la tesela no pueden ser negativas.");
        }

        if (pagePixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pagePixelWidth),
                pagePixelWidth,
                "El ancho de la página debe ser mayor que cero.");
        }

        if (pagePixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pagePixelHeight),
                pagePixelHeight,
                "El alto de la página debe ser mayor que cero.");
        }

        int x = checked(key.TileX * TileSize);
        int y = checked(key.TileY * TileSize);

        /*
         * Una clave fuera de la página no representa una tesela válida.
         */
        if (x >= pagePixelWidth || y >= pagePixelHeight)
        {
            return Int32Rect.Empty;
        }

        int width = Math.Min(
            TileSize,
            pagePixelWidth - x);

        int height = Math.Min(
            TileSize,
            pagePixelHeight - y);

        return new Int32Rect(
            x,
            y,
            width,
            height);
    }

    /// <summary>
    /// Convierte una coordenada de página en un índice de tesela.
    /// </summary>
    private int GetTileIndex(double coordinate)
    {
        return checked(
            (int)Math.Floor(coordinate / TileSize));
    }

    /// <summary>
    /// Verifica los argumentos utilizados para calcular la región visible.
    /// </summary>
    private static void ValidateArguments(
        int page,
        int zoomLevel,
        int pagePixelWidth,
        int pagePixelHeight,
        Rect visibleRegion)
    {
        if (page < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                page,
                "El índice de página no puede ser negativo.");
        }

        if (zoomLevel < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(zoomLevel),
                zoomLevel,
                "El nivel de zoom no puede ser negativo.");
        }

        if (pagePixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pagePixelWidth),
                pagePixelWidth,
                "El ancho de la página debe ser mayor que cero.");
        }

        if (pagePixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pagePixelHeight),
                pagePixelHeight,
                "El alto de la página debe ser mayor que cero.");
        }

        if (visibleRegion.IsEmpty)
        {
            throw new ArgumentException(
                "La región visible no puede estar vacía.",
                nameof(visibleRegion));
        }

        if (!IsFinite(visibleRegion.X) ||
            !IsFinite(visibleRegion.Y) ||
            !IsFinite(visibleRegion.Width) ||
            !IsFinite(visibleRegion.Height))
        {
            throw new ArgumentException(
                "La región visible contiene valores no finitos.",
                nameof(visibleRegion));
        }

        if (visibleRegion.Width < 0d ||
            visibleRegion.Height < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(visibleRegion),
                visibleRegion,
                "El ancho y el alto de la región no pueden ser negativos.");
        }
    }

    /// <summary>
    /// Determina si un valor puede utilizarse de forma segura
    /// en los cálculos geométricos.
    /// </summary>
    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) &&
               !double.IsInfinity(value);
    }
}
