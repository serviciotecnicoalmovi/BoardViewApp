using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BoardView.Rendering.Tiles;

/// <summary>
/// Coordina las solicitudes de renderizado de teselas.
///
/// Sus responsabilidades son:
/// - Consultar la caché antes de renderizar.
/// - Evitar renderizados duplicados de una misma tesela.
/// - Limitar la concurrencia del motor.
/// - Registrar métricas de rendimiento.
/// - Almacenar en caché las teselas completadas.
/// - Cancelar las operaciones cuando el planificador se cierre.
///
/// Esta clase todavía no modifica el visor actual.
/// </summary>
public sealed class TileScheduler : IDisposable
{
    private readonly ITileRenderer _renderer;
    private readonly TileCache _cache;
    private readonly RenderMetrics _metrics;

    /*
     * Limita la cantidad de renderizados simultáneos.
     * Esto evita saturar el procesador y consumir demasiada memoria.
     */
    private readonly SemaphoreSlim _renderSlots;

    /*
     * Conserva las solicitudes que ya están siendo procesadas.
     *
     * Si varios consumidores solicitan la misma TileKey,
     * todos esperarán la misma Task en lugar de iniciar
     * múltiples renderizados idénticos.
     */
    private readonly ConcurrentDictionary<TileKey, Task<Tile>> _inFlight =
        new();

    /*
     * Cancela todos los renderizados internos cuando se destruye
     * el planificador.
     */
    private readonly CancellationTokenSource _shutdownSource = new();

    private int _disposed;

    /// <summary>
    /// Inicializa un nuevo planificador de renderizado.
    /// </summary>
    /// <param name="renderer">
    /// Implementación concreta encargada de producir las teselas.
    /// </param>
    /// <param name="cache">
    /// Caché donde se buscarán y almacenarán los resultados.
    /// </param>
    /// <param name="metrics">
    /// Recolector de métricas del motor.
    /// </param>
    /// <param name="maxConcurrentRenders">
    /// Número máximo de teselas que podrán renderizarse simultáneamente.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Se produce si alguna dependencia es null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Se produce si el límite de concurrencia es menor que uno.
    /// </exception>
    public TileScheduler(
        ITileRenderer renderer,
        TileCache cache,
        RenderMetrics metrics,
        int maxConcurrentRenders = 2)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(metrics);

        if (maxConcurrentRenders <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentRenders),
                maxConcurrentRenders,
                "La cantidad máxima de renderizados debe ser mayor que cero.");
        }

        _renderer = renderer;
        _cache = cache;
        _metrics = metrics;
        _renderSlots = new SemaphoreSlim(
            maxConcurrentRenders,
            maxConcurrentRenders);

        MaxConcurrentRenders = maxConcurrentRenders;
    }

    /// <summary>
    /// Obtiene el máximo de renderizados simultáneos permitido.
    /// </summary>
    public int MaxConcurrentRenders { get; }

    /// <summary>
    /// Obtiene la cantidad de teselas que se encuentran actualmente
    /// en proceso de renderizado.
    /// </summary>
    public int InFlightCount => _inFlight.Count;

    /// <summary>
    /// Solicita una tesela.
    ///
    /// El orden de resolución es:
    /// 1. Buscar en caché.
    /// 2. Reutilizar una solicitud en curso.
    /// 3. Iniciar un nuevo renderizado.
    /// </summary>
    /// <param name="request">
    /// Información necesaria para generar la tesela.
    /// </param>
    /// <param name="cancellationToken">
    /// Permite cancelar la espera del consumidor.
    ///
    /// La cancelación de un consumidor no cancela una operación
    /// compartida por otros consumidores.
    /// </param>
    /// <returns>
    /// Tesela recuperada desde caché o generada por el renderizador.
    /// </returns>
    public async Task<Tile> GetTileAsync(
        TileRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateRequest(request);

        cancellationToken.ThrowIfCancellationRequested();

        _metrics.RecordRequest();

        /*
         * Primera comprobación rápida:
         * si la tesela ya está almacenada, no se necesita
         * ninguna operación de renderizado.
         */
        if (_cache.TryGet(request.Key, out Tile? cachedTile))
        {
            _metrics.RecordCacheHit();
            return cachedTile;
        }

        /*
         * GetOrAdd garantiza que todas las solicitudes simultáneas
         * para la misma clave compartan una única tarea.
         */
        Task<Tile> renderTask = _inFlight.GetOrAdd(
            request.Key,
            _ => RenderAndReleaseAsync(request));

        /*
         * WaitAsync permite cancelar únicamente la espera de este
         * consumidor sin cancelar el renderizado compartido.
         */
        return await renderTask
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Ejecuta el renderizado y elimina la solicitud de la colección
    /// de operaciones activas cuando finaliza.
    /// </summary>
    private async Task<Tile> RenderAndReleaseAsync(
        TileRenderRequest request)
    {
        try
        {
            /*
             * La tesela pudo haber sido agregada a la caché entre
             * la primera consulta y la creación de esta tarea.
             */
            if (_cache.TryGet(request.Key, out Tile? cachedTile))
            {
                _metrics.RecordCacheHit();
                return cachedTile;
            }

            return await RenderCoreAsync(request)
                .ConfigureAwait(false);
        }
        finally
        {
            /*
             * La eliminación ocurre dentro de la propia tarea.
             * Así, la cancelación de un consumidor no provoca que
             * desaparezca prematuramente una operación compartida.
             */
            _inFlight.TryRemove(
                request.Key,
                out _);
        }
    }

    /// <summary>
    /// Ejecuta el renderizado real respetando el límite de concurrencia.
    /// </summary>
    private async Task<Tile> RenderCoreAsync(
        TileRenderRequest request)
    {
        bool slotAcquired = false;
        var stopwatch = new Stopwatch();

        try
        {
            await _renderSlots
                .WaitAsync(_shutdownSource.Token)
                .ConfigureAwait(false);

            slotAcquired = true;

            /*
             * Se consulta nuevamente la caché después de esperar
             * por un espacio de renderizado.
             *
             * Otra operación pudo haber producido la tesela
             * durante la espera.
             */
            if (_cache.TryGet(request.Key, out Tile? cachedTile))
            {
                _metrics.RecordCacheHit();
                return cachedTile;
            }

            stopwatch.Start();

            Tile renderedTile = await _renderer
                .RenderTileAsync(
                    request,
                    _shutdownSource.Token)
                .ConfigureAwait(false);

            stopwatch.Stop();

            if (renderedTile is null)
            {
                throw new InvalidOperationException(
                    "El renderizador devolvió una tesela nula.");
            }

            /*
             * El resultado debe corresponder exactamente con
             * la solicitud procesada.
             */
            if (renderedTile.Key != request.Key)
            {
                throw new InvalidOperationException(
                    $"El renderizador devolvió la clave " +
                    $"'{renderedTile.Key}', pero se esperaba " +
                    $"'{request.Key}'.");
            }

            _cache.AddOrUpdate(renderedTile);
            _metrics.RecordRenderedTile(stopwatch.Elapsed);

            return renderedTile;
        }
        catch
        {
            if (stopwatch.IsRunning)
            {
                stopwatch.Stop();
            }

            _metrics.RecordFailure();
            throw;
        }
        finally
        {
            if (slotAcquired)
            {
                _renderSlots.Release();
            }
        }
    }

    /// <summary>
    /// Verifica que una solicitud creada mediante el valor
    /// predeterminado de la estructura no llegue al renderizador.
    /// </summary>
    private static void ValidateRequest(
        TileRenderRequest request)
    {
        if (request.Key.Page < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Key.Page,
                "El índice de página no puede ser negativo.");
        }

        if (request.Key.ZoomLevel < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Key.ZoomLevel,
                "El nivel de zoom no puede ser negativo.");
        }

        if (request.Key.TileX < 0 ||
            request.Key.TileY < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Key,
                "Las coordenadas de la tesela no pueden ser negativas.");
        }

        if (request.TileBounds.Width <= 0 ||
            request.TileBounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.TileBounds,
                "La región de la tesela debe tener dimensiones positivas.");
        }

        if (request.PagePixelSize.Width <= 0d ||
            request.PagePixelSize.Height <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.PagePixelSize,
                "La página debe tener dimensiones positivas.");
        }
    }

    /// <summary>
    /// Lanza una excepción si el planificador ya fue destruido.
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
    }

    /// <summary>
    /// Cancela las operaciones internas y libera los recursos.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(
                ref _disposed,
                1) != 0)
        {
            return;
        }

        _shutdownSource.Cancel();

        /*
         * No se destruye inmediatamente SemaphoreSlim porque
         * todavía puede haber tareas liberando espacios en finally.
         *
         * Ambos recursos serán recogidos por el recolector cuando
         * terminen las tareas pendientes.
         */
        _shutdownSource.Dispose();
    }
}
