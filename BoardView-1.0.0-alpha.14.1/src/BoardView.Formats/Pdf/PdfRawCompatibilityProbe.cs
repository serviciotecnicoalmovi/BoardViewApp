namespace BoardView.Formats.Pdf;

/// <summary>
/// Inspecciona tokens PDF conocidos por provocar errores antes de abrir el
/// documento con PdfPig. El análisis es deliberadamente superficial y no
/// modifica el archivo original.
/// </summary>
internal static class PdfRawCompatibilityProbe
{
    private static readonly byte[] DotRetToken = "/DotRet"u8.ToArray();
    private static readonly byte[] PageTypeToken = "/Type /Page"u8.ToArray();

    /// <summary>
    /// Detecta destinos internos no conformes y estima el número de páginas
    /// mediante los diccionarios de página visibles en el archivo.
    /// </summary>
    public static PdfRawCompatibilityInfo Inspect(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        byte[] content = File.ReadAllBytes(filePath);
        bool hasDotRet = Contains(content, DotRetToken);
        int pageCount = CountPageObjects(content);

        return new PdfRawCompatibilityInfo(hasDotRet, Math.Max(1, pageCount));
    }

    private static int CountPageObjects(ReadOnlySpan<byte> content)
    {
        int count = 0;
        int offset = 0;

        while (offset <= content.Length - PageTypeToken.Length)
        {
            int relativeIndex = content[offset..].IndexOf(PageTypeToken);
            if (relativeIndex < 0)
            {
                break;
            }

            int absoluteIndex = offset + relativeIndex;
            int suffixIndex = absoluteIndex + PageTypeToken.Length;

            // Excluye el diccionario /Type /Pages del árbol de páginas.
            bool isPagesDictionary = suffixIndex < content.Length && content[suffixIndex] == (byte)'s';
            if (!isPagesDictionary)
            {
                count++;
            }

            offset = absoluteIndex + PageTypeToken.Length;
        }

        return count;
    }

    private static bool Contains(ReadOnlySpan<byte> content, ReadOnlySpan<byte> token) =>
        content.IndexOf(token) >= 0;
}

/// <summary>Resultado de la inspección PDF previa a PdfPig.</summary>
internal readonly record struct PdfRawCompatibilityInfo(
    bool HasMalformedDotRetDestination,
    int EstimatedPageCount);
