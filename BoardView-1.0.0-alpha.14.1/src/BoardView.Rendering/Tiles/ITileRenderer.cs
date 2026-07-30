using System.Threading;
using System.Threading.Tasks;

namespace BoardView.Rendering.Tiles;

/// <summary>
/// Define el contrato que debe implementar cualquier motor
/// capaz de renderizar una tesela.
///
/// Esta interfaz desacopla el planificador del motor concreto
/// de renderizado (PDFium, SVG, Gerber, etc.).
///
/// El resultado siempre será una instancia de <see cref="Tile"/>.
/// </summary>
public interface ITileRenderer
{
    /// <summary>
    /// Renderiza una única tesela.
    /// </summary>
    /// <param name="request">
    /// Información necesaria para renderizar la tesela.
    /// </param>
    /// <param name="cancellationToken">
    /// Permite cancelar el renderizado cuando la tesela deja
    /// de ser necesaria (por ejemplo, debido a un cambio de zoom
    /// o desplazamiento rápido del visor).
    /// </param>
    /// <returns>
    /// Una tarea que produce la tesela renderizada.
    /// </returns>
    Task<Tile> RenderTileAsync(
        TileRenderRequest request,
        CancellationToken cancellationToken = default);
}
