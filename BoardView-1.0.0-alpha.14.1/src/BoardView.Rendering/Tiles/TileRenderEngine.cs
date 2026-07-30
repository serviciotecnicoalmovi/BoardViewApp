using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BoardView.Rendering.Tiles;

/// <summary>
/// Coordina el motor de renderizado por teselas.
///
/// Responsabilidades:
/// - Determinar qué teselas son visibles.
/// - Crear solicitudes de renderizado.
/// - Delegar el trabajo al TileScheduler.
/// - Devolver las teselas listas para dibujarse.
///
/// Esta primera versión no está conectada al visor WPF.
/// </summary>
public sealed class TileRenderEngine
{
    private readonly VisibleRegionCalculator _regionCalculator;
    private readonly TileScheduler _scheduler;

    /// <summary>
    /// Inicializa el motor de renderizado.
    /// </summary>
    public TileRenderEngine(
        VisibleRegionCalculator regionCalculator,
        TileScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(regionCalculator);
        ArgumentNullException.ThrowIfNull(scheduler);

        _regionCalculator = regionCalculator;
        _scheduler = scheduler;
    }

    /// <summary>
    /// Calcula y obtiene todas las teselas necesarias para cubrir
    /// una región visible.
    /// </summary>
    /// <param name="page">
    /// Página PDF.
    /// </param>
    /// <param name="zoomLevel">
    /// Nivel discreto de zoom.
    /// </param>
    /// <param name="pagePixelWidth">
    /// Ancho completo de la página.
    /// </param>
    /// <param name="pagePixelHeight">
    /// Alto completo de la página.
    /// </param>
    /// <param name="visibleRegion">
    /// Región actualmente visible.
    /// </param>
    /// <param name="cancellationToken">
    /// Permite cancelar la operación.
    /// </param>
    /// <returns>
    /// Teselas necesarias para dibujar la región solicitada.
    /// </returns>
    public async Task<IReadOnlyList<Tile>> RenderVisibleRegionAsync(
        int page,
        int zoomLevel,
        int pagePixelWidth,
        int pagePixelHeight,
        Rect visibleRegion,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TileKey> keys =
            _regionCalculator.CalculateVisibleTiles(
                page,
                zoomLevel,
                pagePixelWidth,
                pagePixelHeight,
                visibleRegion);

        if (keys.Count == 0)
        {
            return Array.Empty<Tile>();
        }

        var tasks = new List<Task<Tile>>(keys.Count);

        Size pageSize = new(
            pagePixelWidth,
            pagePixelHeight);

        foreach (TileKey key in keys)
        {
            Int32Rect bounds =
                _regionCalculator.CalculateTileBounds(
                    key,
                    pagePixelWidth,
                    pagePixelHeight);

            /*
             * Una tesela vacía no debe enviarse al renderizador.
             */
            if (bounds.IsEmpty)
            {
                continue;
            }

            var request = new TileRenderRequest(
                key,
                bounds,
                pageSize);

            tasks.Add(
                _scheduler.GetTileAsync(
                    request,
                    cancellationToken));
        }

        if (tasks.Count == 0)
        {
            return Array.Empty<Tile>();
        }

        Tile[] renderedTiles =
            await Task.WhenAll(tasks)
                .ConfigureAwait(false);

        return renderedTiles;
    }
}
