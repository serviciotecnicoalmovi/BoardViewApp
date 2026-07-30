using BoardView.Core.Contracts.Documents;
using BoardView.Core.Documents;
using BoardView.Core.Documents.Common;

namespace BoardView.Formats.Pdf;

/// <summary>
/// Orquesta la extracción técnica y la conversión al modelo interno de un archivo PDF.
/// </summary>
public sealed class PdfBoardDocumentLoader
{
    private readonly IDocumentParser parser;
    private readonly IBoardDocumentConverter converter;

    /// <summary>Inicializa el cargador con las implementaciones PDF predeterminadas.</summary>
    public PdfBoardDocumentLoader()
        : this(new PdfTechnicalDocumentParser(), new PdfBoardDocumentConverter())
    {
    }

    /// <summary>Inicializa el cargador con dependencias explícitas y comprobables.</summary>
    public PdfBoardDocumentLoader(IDocumentParser parser, IBoardDocumentConverter converter)
    {
        this.parser = parser ?? throw new ArgumentNullException(nameof(parser));
        this.converter = converter ?? throw new ArgumentNullException(nameof(converter));
    }

    /// <summary>Extrae y convierte un PDF al modelo interno normalizado.</summary>
    public async ValueTask<BoardDocument> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!parser.CanParse(filePath))
        {
            throw new NotSupportedException($"El archivo '{filePath}' no es admitido por el parser PDF.");
        }

        TechnicalDocument technicalDocument = await parser.ParseAsync(
            new DocumentParseRequest(filePath, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();

        if (!converter.CanConvert(technicalDocument))
        {
            throw new InvalidDataException(
                "El parser PDF produjo un documento que el conversor interno no admite.");
        }

        return converter.Convert(technicalDocument);
    }
}
