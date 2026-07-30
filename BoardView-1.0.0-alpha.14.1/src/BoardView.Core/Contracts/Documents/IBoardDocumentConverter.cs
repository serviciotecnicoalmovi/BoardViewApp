using BoardView.Core.Documents;
using BoardView.Core.Documents.Common;

namespace BoardView.Core.Contracts.Documents;

/// <summary>
/// Convierte un documento técnico extraído a la representación interna normalizada
/// utilizada por el motor gráfico, las búsquedas y las herramientas de BoardView.
/// </summary>
public interface IBoardDocumentConverter
{
    /// <summary>Identificador estable del formato de origen admitido.</summary>
    string SourceFormatId { get; }

    /// <summary>Indica si el conversor admite el documento suministrado.</summary>
    bool CanConvert(TechnicalDocument document);

    /// <summary>Convierte y valida estructuralmente el documento técnico.</summary>
    BoardDocument Convert(TechnicalDocument document);
}
