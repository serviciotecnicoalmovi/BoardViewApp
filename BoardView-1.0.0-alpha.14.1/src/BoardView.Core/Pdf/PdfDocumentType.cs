namespace BoardView.Core.Pdf;

/// <summary>Clasificación técnica de un documento PDF.</summary>
public enum PdfDocumentType
{
    Unknown,
    Standard,
    ImageOnly,
    Technical,
    AcroForm,
    XfaStatic,
    XfaDynamic,
    Protected,
    Corrupted
}
