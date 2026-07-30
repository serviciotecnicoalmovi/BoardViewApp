namespace BoardView.Core.Pdf;

/// <summary>
/// Convierte un documento PDF en un índice técnico independiente de la interfaz.
/// </summary>
public interface IPdfDocumentIndexer
{
    PdfDocumentIndex BuildIndex(string filePath);
}
