using BoardView.App.Controls;

namespace BoardView.App.Services;

/// <summary>
/// Coordina la navegación por referencia entre los visores de placa y
/// esquemático sin introducir dependencias entre ambos controles.
/// </summary>
/// <remarks>
/// La dependencia permanece en una sola dirección:
///
/// MainWindow → BoardNavigationService → PdfDocumentView
///
/// PdfDocumentView no conoce al servicio ni al otro visor. Cada control
/// conserva su propio render, selección pendiente, overlay y centrado.
/// </remarks>
public sealed class BoardNavigationService
{
    private readonly PdfDocumentView boardView;
    private readonly PdfDocumentView schematicView;

    /// <summary>
    /// Inicializa el coordinador con los dos visores existentes.
    /// </summary>
    public BoardNavigationService(
        PdfDocumentView boardView,
        PdfDocumentView schematicView)
    {
        this.boardView =
            boardView ??
            throw new ArgumentNullException(
                nameof(boardView));

        this.schematicView =
            schematicView ??
            throw new ArgumentNullException(
                nameof(schematicView));
    }

    /// <summary>
    /// Solicita la selección de una referencia en ambos documentos.
    /// </summary>
    /// <remarks>
    /// Cuando un visor aún está cargando o cambiando de página,
    /// PdfDocumentView conserva internamente la referencia pendiente y la
    /// aplica cuando su GeometryRenderResult queda disponible.
    /// </remarks>
    public BoardNavigationResult NavigateToReference(
        string reference,
        bool centerOnComponent = true)
    {
        string normalizedReference =
            NormalizeReference(
                reference);

        if (normalizedReference.Length == 0)
        {
            ClearSelection();

            return BoardNavigationResult.Empty;
        }

        bool boardSelectedImmediately =
            boardView.SelectReference(
                normalizedReference,
                centerOnComponent);

        bool schematicSelectedImmediately =
            schematicView.SelectReference(
                normalizedReference,
                centerOnComponent);

        return new BoardNavigationResult(
            normalizedReference,
            boardSelectedImmediately,
            schematicSelectedImmediately);
    }

    /// <summary>
    /// Limpia la selección persistente de ambos visores.
    /// </summary>
    public void ClearSelection()
    {
        boardView.ClearSelection();
        schematicView.ClearSelection();
    }

    /// <summary>
    /// Normaliza la referencia para que placa y esquemático reciban el mismo
    /// valor estable.
    /// </summary>
    private static string NormalizeReference(
        string? reference)
    {
        return string.IsNullOrWhiteSpace(
                reference)
            ? string.Empty
            : reference
                .Trim()
                .Replace(
                    " ",
                    string.Empty,
                    StringComparison.Ordinal)
                .ToUpperInvariant();
    }
}

/// <summary>
/// Resultado inmediato de una solicitud de navegación.
/// </summary>
/// <remarks>
/// Un valor false no significa necesariamente que la navegación haya fallado:
/// el visor puede haber almacenado la referencia como pendiente mientras
/// termina de cargar la nueva página.
/// </remarks>
public sealed record BoardNavigationResult(
    string Reference,
    bool BoardSelectedImmediately,
    bool SchematicSelectedImmediately)
{
    /// <summary>
    /// Resultado reutilizable para una referencia vacía.
    /// </summary>
    public static BoardNavigationResult Empty { get; } =
        new(
            string.Empty,
            BoardSelectedImmediately: false,
            SchematicSelectedImmediately: false);

    /// <summary>
    /// Indica si ambos visores resolvieron la referencia inmediatamente.
    /// </summary>
    public bool SelectedImmediatelyInBoth =>
        BoardSelectedImmediately &&
        SchematicSelectedImmediately;

    /// <summary>
    /// Indica si al menos un visor necesita completar la navegación después
    /// de terminar su carga geométrica.
    /// </summary>
    public bool HasPendingNavigation =>
        !BoardSelectedImmediately ||
        !SchematicSelectedImmediately;
}
