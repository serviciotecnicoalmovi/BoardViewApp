namespace BoardView.Core.Pdf;

/// <summary>
/// Representa una palabra extraída de una página PDF y su posición en puntos PDF.
/// </summary>
public sealed record PdfWord(
    string Text,
    double Left,
    double Bottom,
    double Width,
    double Height);
