using BoardView.Core.Pdf;
using UglyToad.PdfPig;

namespace BoardView.Formats.Pdf;

/// <summary>
/// Clasifica documentos PDF antes de su visualización o análisis técnico.
/// La detección estructural se realiza sin modificar el archivo original.
/// </summary>
public sealed class PdfDocumentInspector : IPdfDocumentInspector
{
    private static readonly byte[] PdfHeader = "%PDF-"u8.ToArray();
    private static readonly byte[] EncryptToken = "/Encrypt"u8.ToArray();
    private static readonly byte[] AcroFormToken = "/AcroForm"u8.ToArray();
    private static readonly byte[] XfaToken = "/XFA"u8.ToArray();
    private static readonly byte[] DynamicRenderToken = "dynamicRender"u8.ToArray();
    private static readonly byte[] PleaseWaitToken = "Please wait"u8.ToArray();

    public PdfDocumentInspection Inspect(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("No se encontró el documento PDF.", filePath);
        }

        string absolutePath = Path.GetFullPath(filePath);
        PdfStructureFlags flags = ScanStructure(absolutePath, cancellationToken);
        if (!flags.HasPdfHeader)
        {
            return Create(absolutePath, PdfDocumentType.Corrupted, flags, "El archivo no contiene una cabecera PDF válida.");
        }

        if (flags.IsEncrypted)
        {
            return Create(absolutePath, PdfDocumentType.Protected, flags, "El documento está cifrado o protegido y requiere credenciales compatibles.");
        }

        if (flags.HasXfa)
        {
            PdfDocumentType xfaType = flags.IsDynamicXfa
                ? PdfDocumentType.XfaDynamic
                : PdfDocumentType.XfaStatic;
            string message = xfaType == PdfDocumentType.XfaDynamic
                ? "El documento utiliza XFA dinámico. Debe abrirse con Adobe Acrobat o Reader."
                : "El documento contiene formularios XFA que el visor integrado no puede representar fielmente.";
            return Create(absolutePath, xfaType, flags, message);
        }

        PdfRawCompatibilityInfo compatibility = PdfRawCompatibilityProbe.Inspect(absolutePath);
        if (compatibility.HasMalformedDotRetDestination)
        {
            return new PdfDocumentInspection(
                absolutePath,
                PdfDocumentType.Standard,
                compatibility.EstimatedPageCount,
                0,
                0,
                flags.HasAcroForm,
                false,
                false,
                "PDF visible en el visor integrado. El análisis técnico se omitió porque contiene el destino interno no conforme /DotRet.",
                canAnalyzeTechnically: false);
        }

        try
        {
            using PdfDocument document = PdfDocument.Open(absolutePath, PdfParsingOptionsFactory.CreateResilient());
            int pageCount = document.NumberOfPages;
            int wordCount = 0;
            int vectorCount = 0;

            foreach (UglyToad.PdfPig.Content.Page page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                wordCount += page.GetWords().Count(word => !string.IsNullOrWhiteSpace(word.Text));
                vectorCount += page.Paths.Count;
            }

            PdfDocumentType documentType = Classify(flags.HasAcroForm, wordCount, vectorCount);
            string message = documentType switch
            {
                PdfDocumentType.Technical => "PDF técnico compatible con indexación textual y extracción vectorial.",
                PdfDocumentType.ImageOnly => "PDF sin texto ni vectores detectables; probablemente contiene páginas rasterizadas.",
                PdfDocumentType.AcroForm => "PDF AcroForm compatible con el visor integrado.",
                _ => "PDF estándar compatible con el visor integrado."
            };

            return new PdfDocumentInspection(
                absolutePath,
                documentType,
                pageCount,
                wordCount,
                vectorCount,
                flags.HasAcroForm,
                false,
                false,
                message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Create(
                absolutePath,
                PdfDocumentType.Corrupted,
                flags,
                $"El documento no pudo interpretarse como PDF válido: {exception.Message}");
        }
    }

    private static PdfDocumentType Classify(bool hasAcroForm, int wordCount, int vectorCount)
    {
        if (hasAcroForm)
        {
            return PdfDocumentType.AcroForm;
        }

        if (vectorCount > 0)
        {
            return PdfDocumentType.Technical;
        }

        return wordCount > 0 ? PdfDocumentType.Standard : PdfDocumentType.ImageOnly;
    }

    private static PdfDocumentInspection Create(
        string filePath,
        PdfDocumentType documentType,
        PdfStructureFlags flags,
        string message) =>
        new(
            filePath,
            documentType,
            0,
            0,
            0,
            flags.HasAcroForm,
            flags.HasXfa,
            flags.IsEncrypted,
            message);

    private static PdfStructureFlags ScanStructure(string filePath, CancellationToken cancellationToken)
    {
        const int bufferSize = 64 * 1024;
        int overlapLength = Math.Max(
            Math.Max(EncryptToken.Length, AcroFormToken.Length),
            Math.Max(XfaToken.Length, DynamicRenderToken.Length));
        overlapLength = Math.Max(overlapLength, PleaseWaitToken.Length) - 1;

        bool hasPdfHeader = false;
        bool isEncrypted = false;
        bool hasAcroForm = false;
        bool hasXfa = false;
        bool hasDynamicRender = false;
        bool hasPleaseWait = false;
        byte[] overlap = [];
        byte[] buffer = new byte[bufferSize];

        using FileStream stream = File.OpenRead(filePath);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0)
            {
                break;
            }

            byte[] window = new byte[overlap.Length + bytesRead];
            overlap.CopyTo(window, 0);
            Buffer.BlockCopy(buffer, 0, window, overlap.Length, bytesRead);

            hasPdfHeader |= Contains(window, PdfHeader);
            isEncrypted |= Contains(window, EncryptToken);
            hasAcroForm |= Contains(window, AcroFormToken);
            hasXfa |= Contains(window, XfaToken);
            hasDynamicRender |= Contains(window, DynamicRenderToken);
            hasPleaseWait |= Contains(window, PleaseWaitToken);

            int copyLength = Math.Min(overlapLength, window.Length);
            overlap = window[^copyLength..];
        }

        return new PdfStructureFlags(
            hasPdfHeader,
            isEncrypted,
            hasAcroForm,
            hasXfa,
            hasDynamicRender || hasPleaseWait);
    }

    private static bool Contains(ReadOnlySpan<byte> source, ReadOnlySpan<byte> value) =>
        source.IndexOf(value) >= 0;

    private readonly record struct PdfStructureFlags(
        bool HasPdfHeader,
        bool IsEncrypted,
        bool HasAcroForm,
        bool HasXfa,
        bool IsDynamicXfa);
}
