namespace BoardView.Core.Pdf;

/// <summary>Resultado inmutable de la inspección preliminar de un PDF.</summary>
public sealed class PdfDocumentInspection
{
    public PdfDocumentInspection(
        string filePath,
        PdfDocumentType documentType,
        int pageCount,
        int wordCount,
        int vectorCount,
        bool hasAcroForm,
        bool hasXfa,
        bool isEncrypted,
        string message,
        bool? canAnalyzeTechnically = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        FilePath = Path.GetFullPath(filePath);
        DocumentType = documentType;
        PageCount = Math.Max(0, pageCount);
        WordCount = Math.Max(0, wordCount);
        VectorCount = Math.Max(0, vectorCount);
        HasAcroForm = hasAcroForm;
        HasXfa = hasXfa;
        IsEncrypted = isEncrypted;
        Message = message;
        technicalAnalysisAvailable = canAnalyzeTechnically;
    }

    public string FilePath { get; }
    public PdfDocumentType DocumentType { get; }
    public int PageCount { get; }
    public int WordCount { get; }
    public int VectorCount { get; }
    public bool HasAcroForm { get; }
    public bool HasXfa { get; }
    public bool IsEncrypted { get; }
    public string Message { get; }

    private readonly bool? technicalAnalysisAvailable;

    /// <summary>Indica si WebView2 puede intentar mostrar el documento normalmente.</summary>
    public bool CanUseIntegratedViewer => DocumentType is
        PdfDocumentType.Standard or
        PdfDocumentType.ImageOnly or
        PdfDocumentType.Technical or
        PdfDocumentType.AcroForm;

    /// <summary>Indica si el documento puede pasar al indexador técnico actual.</summary>
    public bool CanAnalyzeTechnically => technicalAnalysisAvailable ?? (DocumentType is
        PdfDocumentType.Standard or
        PdfDocumentType.Technical or
        PdfDocumentType.AcroForm);
}
