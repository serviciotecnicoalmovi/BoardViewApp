using System.Runtime.InteropServices;
using BoardView.Core.Pdf;

namespace BoardView.Formats.Pdf;

/// <summary>
/// Indexador textual aislado basado en PDFium. PDFium se usa exclusivamente
/// para extraer texto y coordenadas; WebView2 continúa mostrando el documento
/// y PdfPig continúa atendiendo el pipeline geométrico existente.
/// </summary>
public sealed class SafePdfDocumentIndexer : ISafePdfDocumentIndexer
{
    private static readonly SemaphoreSlim PdfiumGate = new(1, 1);
    
    /// <inheritdoc />
    public async Task<SafePdfIndexResult> BuildIndexAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string absolutePath = Path.GetFullPath(filePath);
        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException("No se encontró el documento PDF.", absolutePath);
        }

        await PdfiumGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () => BuildIndex(absolutePath, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            PdfiumGate.Release();
        }
    }

    private static SafePdfIndexResult BuildIndex(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        PdfiumRuntime.EnsureInitialized();

        List<string> warnings = [];
        List<PdfPage> pages = [];
        int indexedPages = 0;
        IntPtr document = IntPtr.Zero;
        IntPtr utf8Path = IntPtr.Zero;

        try
        {
            utf8Path = Marshal.StringToCoTaskMemUTF8(absolutePath);
            document = PdfiumNative.LoadDocument(utf8Path, IntPtr.Zero);
            if (document == IntPtr.Zero)
            {
                throw CreatePdfiumException("PDFium no pudo abrir el documento");
            }

            int pageCount = Math.Max(0, PdfiumNative.GetPageCount(document));
            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    pages.Add(IndexPage(document, pageIndex, cancellationToken));
                    indexedPages++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    int pageNumber = pageIndex + 1;
                    warnings.Add($"Página {pageNumber} omitida por PDFium: {GetInnermostMessage(exception)}");
                    pages.Add(new PdfPage(pageNumber, 0D, 0D, Array.Empty<PdfWord>()));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            warnings.Add($"No fue posible crear el índice PDFium: {GetInnermostMessage(exception)}");
            pages.Clear();
            indexedPages = 0;

            PdfRawCompatibilityInfo compatibility = PdfRawCompatibilityProbe.Inspect(absolutePath);
            pages.AddRange(Enumerable
                .Range(1, compatibility.EstimatedPageCount)
                .Select(static pageNumber =>
                    new PdfPage(pageNumber, 0D, 0D, Array.Empty<PdfWord>())));
        }
        finally
        {
            if (document != IntPtr.Zero)
            {
                PdfiumNative.CloseDocument(document);
            }

            if (utf8Path != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(utf8Path);
            }
        }

        return new SafePdfIndexResult(
            new PdfDocumentIndex(absolutePath, pages),
            indexedPages,
            usedSanitizedCopy: false,
            warnings);
    }

    private static PdfPage IndexPage(
        IntPtr document,
        int pageIndex,
        CancellationToken cancellationToken)
    {
        IntPtr page = PdfiumNative.LoadPage(document, pageIndex);
        if (page == IntPtr.Zero)
        {
            throw CreatePdfiumException($"No se pudo cargar la página {pageIndex + 1}");
        }

        IntPtr textPage = IntPtr.Zero;
        try
        {
            double width = PdfiumNative.GetPageWidth(page);
            double height = PdfiumNative.GetPageHeight(page);
            textPage = PdfiumNative.LoadTextPage(page);
            if (textPage == IntPtr.Zero)
            {
                return new PdfPage(pageIndex + 1, width, height, Array.Empty<PdfWord>());
            }

            int characterCount = Math.Max(0, PdfiumNative.CountChars(textPage));
            PdfiumTextWordBuilder builder = new();

            for (int characterIndex = 0; characterIndex < characterCount; characterIndex++)
            {
                if ((characterIndex & 255) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                uint codePoint = PdfiumNative.GetUnicode(textPage, characterIndex);
                if (codePoint == 0 || codePoint > char.MaxValue)
                {
                    builder.Append(' ', null);
                    continue;
                }

                char character = (char)codePoint;
                PdfiumTextWordBuilder.CharacterBox? box = TryGetCharacterBox(textPage, characterIndex);
                builder.Append(character, box);
            }

            return new PdfPage(pageIndex + 1, width, height, builder.Complete());
        }
        finally
        {
            if (textPage != IntPtr.Zero)
            {
                PdfiumNative.CloseTextPage(textPage);
            }

            PdfiumNative.ClosePage(page);
        }
    }

    private static PdfiumTextWordBuilder.CharacterBox? TryGetCharacterBox(
        IntPtr textPage,
        int characterIndex)
    {
        bool succeeded = PdfiumNative.GetCharBox(
            textPage,
            characterIndex,
            out double left,
            out double right,
            out double bottom,
            out double top);

        if (!succeeded ||
            !double.IsFinite(left) ||
            !double.IsFinite(right) ||
            !double.IsFinite(bottom) ||
            !double.IsFinite(top))
        {
            return null;
        }

        return new PdfiumTextWordBuilder.CharacterBox(left, right, bottom, top);
    }
        
    private static InvalidDataException CreatePdfiumException(string message)
    {
        uint errorCode = PdfiumNative.GetLastError();
        string reason = errorCode switch
        {
            1 => "error desconocido",
            2 => "archivo inexistente o inaccesible",
            3 => "formato PDF inválido o dañado",
            4 => "contraseña requerida o incorrecta",
            5 => "restricción de seguridad no compatible",
            6 => "error interno de página",
            _ => "sin información adicional",
        };

        return new InvalidDataException($"{message}. PDFium {errorCode}: {reason}.");
    }

    private static string GetInnermostMessage(Exception exception)
    {
        Exception current = exception;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }
}
