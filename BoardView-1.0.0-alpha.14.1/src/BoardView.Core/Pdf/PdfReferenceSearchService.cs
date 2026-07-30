namespace BoardView.Core.Pdf;

/// <summary>Resultado agregado de una referencia en una página PDF.</summary>
public sealed record PdfReferenceMatch(int PageNumber, int Occurrences);

/// <summary>
/// Busca referencias electrónicas dentro de índices PDF sin depender de WPF,
/// del parser geométrico ni del motor de reconocimiento.
/// </summary>
public sealed class PdfReferenceSearchService
{
    private const int MaximumJoinedWords = 8;

    /// <summary>
    /// Busca una referencia y devuelve una coincidencia agregada por página.
    /// Se consideran equivalentes los sufijos técnicos comunes, por ejemplo
    /// C3303 y C3303_E. También reconstruye referencias divididas en varios
    /// fragmentos de texto contiguos por el productor del PDF.
    /// </summary>
    public IReadOnlyList<PdfReferenceMatch> Search(PdfDocumentIndex? index, string query)
    {
        if (index is null || string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<PdfReferenceMatch>();
        }

        string normalizedQuery = NormalizeReference(query);
        if (normalizedQuery.Length == 0)
        {
            return Array.Empty<PdfReferenceMatch>();
        }

        List<PdfReferenceMatch> matches = [];
        foreach (PdfPage page in index.Pages)
        {
            int occurrences = CountOccurrences(page.Words, normalizedQuery);
            if (occurrences > 0)
            {
                matches.Add(new PdfReferenceMatch(page.Number, occurrences));
            }
        }

        return matches;
    }

    internal static bool IsReferenceMatch(string candidate, string normalizedQuery)
    {
        string normalizedCandidate = NormalizeReference(candidate);
        if (normalizedCandidate.Length == 0)
        {
            return false;
        }

        if (string.Equals(normalizedCandidate, normalizedQuery, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(
            RemoveSideSuffix(normalizedCandidate),
            RemoveSideSuffix(normalizedQuery),
            StringComparison.Ordinal);
    }

    private static int CountOccurrences(
        IReadOnlyList<PdfWord> words,
        string normalizedQuery)
    {
        int occurrences = 0;
        int index = 0;

        while (index < words.Count)
        {
            if (IsReferenceMatch(words[index].Text, normalizedQuery))
            {
                occurrences++;
                index++;
                continue;
            }

            int consumed = TryMatchJoinedWords(words, index, normalizedQuery);
            if (consumed > 0)
            {
                occurrences++;
                index += consumed;
                continue;
            }

            index++;
        }

        return occurrences;
    }

    private static int TryMatchJoinedWords(
        IReadOnlyList<PdfWord> words,
        int startIndex,
        string normalizedQuery)
    {
        if (startIndex >= words.Count - 1)
        {
            return 0;
        }

        PdfWord first = words[startIndex];
        string combined = first.Text;
        PdfWord previous = first;

        int maximum = Math.Min(words.Count, startIndex + MaximumJoinedWords);
        for (int index = startIndex + 1; index < maximum; index++)
        {
            PdfWord current = words[index];
            if (!AreTextFragmentsAdjacent(previous, current))
            {
                break;
            }

            combined += current.Text;
            if (IsReferenceMatch(combined, normalizedQuery))
            {
                return index - startIndex + 1;
            }

            string normalizedCombined = NormalizeReference(combined);
            string queryWithoutSuffix = RemoveSideSuffix(normalizedQuery);
            if (normalizedCombined.Length > normalizedQuery.Length + 2 ||
                (!normalizedQuery.StartsWith(normalizedCombined, StringComparison.Ordinal) &&
                 !queryWithoutSuffix.StartsWith(normalizedCombined, StringComparison.Ordinal)))
            {
                break;
            }

            previous = current;
        }

        return 0;
    }

    private static bool AreTextFragmentsAdjacent(PdfWord left, PdfWord right)
    {
        double leftCenterY = left.Bottom + (left.Height / 2D);
        double rightCenterY = right.Bottom + (right.Height / 2D);
        double height = Math.Max(0.5D, Math.Max(left.Height, right.Height));
        bool sameLine = Math.Abs(leftCenterY - rightCenterY) <= height * 0.65D;

        double leftRight = left.Left + left.Width;
        double gap = right.Left - leftRight;
        bool closeHorizontally = gap >= -(height * 0.35D) && gap <= height * 1.75D;
        return sameLine && closeHorizontally;
    }

    private static string NormalizeReference(string value)
    {
        ReadOnlySpan<char> source = value.AsSpan().Trim();
        if (source.IsEmpty)
        {
            return string.Empty;
        }

        Span<char> buffer = source.Length <= 128
            ? stackalloc char[source.Length]
            : new char[source.Length];

        int length = 0;
        foreach (char character in source)
        {
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                buffer[length++] = char.ToUpperInvariant(character);
            }
        }

        return new string(buffer[..length]);
    }

    private static string RemoveSideSuffix(string reference) =>
        reference.EndsWith("_E", StringComparison.Ordinal)
            ? reference[..^2]
            : reference;
}
