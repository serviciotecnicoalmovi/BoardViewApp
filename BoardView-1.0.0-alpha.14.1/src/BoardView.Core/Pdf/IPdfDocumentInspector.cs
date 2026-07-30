namespace BoardView.Core.Pdf;

/// <summary>Inspecciona un PDF antes de enviarlo al visor o a los extractores.</summary>
public interface IPdfDocumentInspector
{
    PdfDocumentInspection Inspect(string filePath, CancellationToken cancellationToken = default);
}
