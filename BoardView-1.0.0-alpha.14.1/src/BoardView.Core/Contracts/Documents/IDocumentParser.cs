using BoardView.Core.Documents.Common;

namespace BoardView.Core.Contracts.Documents;

/// <summary>Contrato común para convertir un archivo en el modelo interno.</summary>
public interface IDocumentParser
{
    string FormatId { get; }
    bool CanParse(string filePath);
    ValueTask<TechnicalDocument> ParseAsync(DocumentParseRequest request);
}
