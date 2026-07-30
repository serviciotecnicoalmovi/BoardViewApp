using BoardView.Core.Documents.Common;

namespace BoardView.Core.Contracts.Documents;

/// <summary>Proveedor de búsquedas sobre el modelo interno.</summary>
public interface IDocumentSearchProvider
{
    IReadOnlyList<DocumentSearchResult> Search(
        TechnicalDocument document,
        DocumentSearchQuery query);
}
