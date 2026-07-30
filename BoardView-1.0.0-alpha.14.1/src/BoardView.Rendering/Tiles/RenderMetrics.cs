using System;

namespace BoardView.Rendering.Tiles;

/// <summary>
/// Almacena métricas acumuladas del motor de renderizado por teselas.
///
/// Esta clase permitirá conocer:
/// - Cuántas teselas fueron solicitadas.
/// - Cuántas fueron servidas desde caché.
/// - Cuántas tuvieron que renderizarse.
/// - Cuántos errores ocurrieron.
/// - Cuánto tiempo total se utilizó en renderizado.
///
/// En esta entrega todavía no está conectada al visor.
/// Su función es preparar la infraestructura de diagnóstico.
/// </summary>
public sealed class RenderMetrics
{
    private readonly object _syncRoot = new();

    private long _requestedTiles;
    private long _cacheHits;
    private long _renderedTiles;
    private long _failedTiles;
    private long _totalRenderTicks;

    /// <summary>
    /// Obtiene la cantidad total de teselas solicitadas.
    /// </summary>
    public long RequestedTiles
    {
        get
        {
            lock (_syncRoot)
            {
                return _requestedTiles;
            }
        }
    }

    /// <summary>
    /// Obtiene la cantidad de solicitudes resueltas desde caché.
    /// </summary>
    public long CacheHits
    {
        get
        {
            lock (_syncRoot)
            {
                return _cacheHits;
            }
        }
    }

    /// <summary>
    /// Obtiene la cantidad de teselas generadas por el renderizador.
    /// </summary>
    public long RenderedTiles
    {
        get
        {
            lock (_syncRoot)
            {
                return _renderedTiles;
            }
        }
    }

    /// <summary>
    /// Obtiene la cantidad de teselas que no pudieron renderizarse.
    /// </summary>
    public long FailedTiles
    {
        get
        {
            lock (_syncRoot)
            {
                return _failedTiles;
            }
        }
    }

    /// <summary>
    /// Obtiene el tiempo total acumulado utilizado en renderizado.
    /// </summary>
    public TimeSpan TotalRenderTime
    {
        get
        {
            lock (_syncRoot)
            {
                return TimeSpan.FromTicks(_totalRenderTicks);
            }
        }
    }

    /// <summary>
    /// Obtiene el porcentaje de solicitudes resueltas desde caché.
    ///
    /// Devuelve cero cuando todavía no se ha solicitado ninguna tesela.
    /// </summary>
    public double CacheHitRatio
    {
        get
        {
            lock (_syncRoot)
            {
                if (_requestedTiles == 0)
                {
                    return 0d;
                }

                return (double)_cacheHits / _requestedTiles;
            }
        }
    }

    /// <summary>
    /// Obtiene el tiempo medio utilizado para renderizar una tesela.
    ///
    /// Las teselas servidas desde caché no se incluyen en este cálculo.
    /// </summary>
    public TimeSpan AverageRenderTime
    {
        get
        {
            lock (_syncRoot)
            {
                if (_renderedTiles == 0)
                {
                    return TimeSpan.Zero;
                }

                return TimeSpan.FromTicks(
                    _totalRenderTicks / _renderedTiles);
            }
        }
    }

    /// <summary>
    /// Registra una nueva solicitud de tesela.
    /// </summary>
    public void RecordRequest()
    {
        lock (_syncRoot)
        {
            _requestedTiles++;
        }
    }

    /// <summary>
    /// Registra que una solicitud fue resuelta desde la caché.
    /// </summary>
    public void RecordCacheHit()
    {
        lock (_syncRoot)
        {
            _cacheHits++;
        }
    }

    /// <summary>
    /// Registra una tesela renderizada correctamente.
    /// </summary>
    /// <param name="renderTime">
    /// Tiempo empleado por el motor para generar la tesela.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Se produce si el tiempo recibido es negativo.
    /// </exception>
    public void RecordRenderedTile(TimeSpan renderTime)
    {
        if (renderTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(renderTime),
                renderTime,
                "El tiempo de renderizado no puede ser negativo.");
        }

        lock (_syncRoot)
        {
            _renderedTiles++;
            _totalRenderTicks = checked(
                _totalRenderTicks + renderTime.Ticks);
        }
    }

    /// <summary>
    /// Registra una tesela que no pudo renderizarse.
    /// </summary>
    public void RecordFailure()
    {
        lock (_syncRoot)
        {
            _failedTiles++;
        }
    }

    /// <summary>
    /// Restablece todas las métricas acumuladas.
    /// </summary>
    public void Reset()
    {
        lock (_syncRoot)
        {
            _requestedTiles = 0;
            _cacheHits = 0;
            _renderedTiles = 0;
            _failedTiles = 0;
            _totalRenderTicks = 0;
        }
    }

    /// <summary>
    /// Devuelve un resumen legible para diagnóstico.
    /// </summary>
    public override string ToString()
    {
        lock (_syncRoot)
        {
            return
                $"Requested={_requestedTiles}, " +
                $"CacheHits={_cacheHits}, " +
                $"Rendered={_renderedTiles}, " +
                $"Failed={_failedTiles}, " +
                $"HitRatio={CacheHitRatio:P2}, " +
                $"TotalRenderTime={TotalRenderTime.TotalMilliseconds:F2} ms, " +
                $"AverageRenderTime={AverageRenderTime.TotalMilliseconds:F2} ms";
        }
    }
}
