using System.Text;
using System.Text.RegularExpressions;
using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Convierte observaciones textuales obtenidas desde PDF u OCR en candidatos
/// normalizados de referencia electrónica.
/// </summary>
/// <remarks>
/// El detector no depende de un motor OCR concreto. Recibe observaciones
/// textuales con límites y confianza, corrige confusiones frecuentes, divide
/// líneas compuestas y conserva únicamente tokens con formato de referencia.
///
/// Ejemplos admitidos:
///
/// <list type="bullet">
/// <item>R15</item>
/// <item>C5203</item>
/// <item>U3200</item>
/// <item>TP15</item>
/// <item>FL4200</item>
/// <item>J1A</item>
/// </list>
/// </remarks>
public sealed class BoardReferenceDetector
{
    private static readonly Regex TokenSeparatorPattern =
        new(
            @"[\s,;:|/\\()\[\]{}<>]+",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static readonly Regex ReferencePattern =
        new(
            @"^(?<prefix>[A-Z]{1,4})(?<number>\d{1,8})(?<suffix>[A-Z]?)$",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    /// <summary>
    /// Detecta referencias usando la configuración predeterminada.
    /// </summary>
    public IReadOnlyList<BoardReferenceCandidate> Detect(
        IEnumerable<BoardTextObservation> observations)
    {
        return Detect(
            observations,
            BoardReferenceDetectorOptions.Default,
            CancellationToken.None);
    }

    /// <summary>
    /// Detecta referencias usando la configuración indicada.
    /// </summary>
    public IReadOnlyList<BoardReferenceCandidate> Detect(
        IEnumerable<BoardTextObservation> observations,
        BoardReferenceDetectorOptions options)
    {
        return Detect(
            observations,
            options,
            CancellationToken.None);
    }

    /// <summary>
    /// Detecta y normaliza candidatos de referencia.
    /// </summary>
    public IReadOnlyList<BoardReferenceCandidate> Detect(
        IEnumerable<BoardTextObservation> observations,
        BoardReferenceDetectorOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            observations);

        ArgumentNullException.ThrowIfNull(
            options);

        options.Validate();

        cancellationToken.ThrowIfCancellationRequested();

        BoardTextObservation[] source =
            observations
                .OrderBy(observation => observation.PageIndex)
                .ThenBy(observation => observation.Bounds.Top)
                .ThenBy(observation => observation.Bounds.Left)
                .ThenBy(observation => observation.Id)
                .ToArray();

        ValidateObservationIdentifiers(
            source);

        var detected =
            new List<DetectedReference>();

        foreach (BoardTextObservation observation in source)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!CanEvaluate(
                    observation,
                    options))
            {
                continue;
            }

            IReadOnlyList<TokenSlice> tokens =
                SplitObservation(
                    observation);

            foreach (TokenSlice token in tokens)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string normalized =
                    NormalizeOcrToken(
                        token.Text,
                        options);

                if (!TryValidateReference(
                        normalized,
                        options,
                        out string canonicalReference))
                {
                    continue;
                }

                detected.Add(
                    new DetectedReference(
                        observation,
                        token.Bounds,
                        canonicalReference));
            }
        }

        IReadOnlyList<DetectedReference> merged =
            options.MergeDuplicateObservations
                ? MergeDuplicates(
                    detected,
                    options)
                : detected;

        var candidates =
            new List<BoardReferenceCandidate>(
                merged.Count);

        int candidateId = 0;

        foreach (DetectedReference item in merged
                     .OrderBy(item => item.Observation.PageIndex)
                     .ThenBy(item => item.Bounds.Top)
                     .ThenBy(item => item.Bounds.Left)
                     .ThenBy(item => item.Reference,
                         StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(
                new BoardReferenceCandidate(
                    candidateId++,
                    item.Reference,
                    item.Bounds,
                    item.Observation.Confidence,
                    item.Observation.PageIndex,
                    item.Observation.RotationDegrees,
                    item.Observation.SourceId));
        }

        return candidates;
    }

    /// <summary>
    /// Divide una observación textual en tokens y aproxima los límites de cada
    /// token según su posición dentro de la línea.
    /// </summary>
    private static IReadOnlyList<TokenSlice> SplitObservation(
        BoardTextObservation observation)
    {
        string text =
            observation.Text.Trim();

        string[] tokens =
            TokenSeparatorPattern
                .Split(text)
                .Where(token =>
                    !string.IsNullOrWhiteSpace(token))
                .ToArray();

        if (tokens.Length <= 1)
        {
            return new[]
            {
                new TokenSlice(
                    text,
                    observation.Bounds)
            };
        }

        int totalCharacterCount =
            tokens.Sum(token => token.Length);

        if (totalCharacterCount <= 0)
        {
            return Array.Empty<TokenSlice>();
        }

        var result =
            new List<TokenSlice>(
                tokens.Length);

        bool vertical =
            IsVertical(
                observation.RotationDegrees);

        int cursor =
            vertical
                ? observation.Bounds.Top
                : observation.Bounds.Left;

        int availableLength =
            vertical
                ? observation.Bounds.Height
                : observation.Bounds.Width;

        int consumedLength = 0;

        for (int index = 0;
             index < tokens.Length;
             index++)
        {
            string token =
                tokens[index];

            int tokenLength =
                index == tokens.Length - 1
                    ? availableLength - consumedLength
                    : Math.Max(
                        1,
                        (int)Math.Round(
                            availableLength *
                            ((double)token.Length /
                             totalCharacterCount)));

            BoardGeometryBounds bounds =
                vertical
                    ? new BoardGeometryBounds(
                        observation.Bounds.Left,
                        cursor,
                        observation.Bounds.Width,
                        Math.Max(1, tokenLength))
                    : new BoardGeometryBounds(
                        cursor,
                        observation.Bounds.Top,
                        Math.Max(1, tokenLength),
                        observation.Bounds.Height);

            result.Add(
                new TokenSlice(
                    token,
                    bounds));

            cursor +=
                tokenLength;

            consumedLength +=
                tokenLength;
        }

        return result;
    }

    /// <summary>
    /// Corrige confusiones OCR únicamente cuando la posición esperada permite
    /// resolverlas de forma determinista.
    /// </summary>
    private static string NormalizeOcrToken(
        string value,
        BoardReferenceDetectorOptions options)
    {
        string compact =
            BoardReferenceCandidate.NormalizeReference(
                value);

        if (!options.CorrectCommonOcrConfusions ||
            compact.Length == 0)
        {
            return compact;
        }

        int firstDigitIndex =
            FindFirstDigitLikeCharacter(
                compact);

        if (firstDigitIndex <= 0)
        {
            return compact;
        }

        var builder =
            new StringBuilder(
                compact.Length);

        for (int index = 0;
             index < compact.Length;
             index++)
        {
            char character =
                compact[index];

            if (index < firstDigitIndex)
            {
                builder.Append(
                    NormalizePrefixCharacter(
                        character));
            }
            else
            {
                builder.Append(
                    NormalizeNumericCharacter(
                        character));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Valida el patrón y los prefijos permitidos.
    /// </summary>
    private static bool TryValidateReference(
        string value,
        BoardReferenceDetectorOptions options,
        out string canonicalReference)
    {
        canonicalReference =
            string.Empty;

        if (value.Length <
                options.MinimumReferenceLength ||
            value.Length >
                options.MaximumReferenceLength)
        {
            return false;
        }

        Match match =
            ReferencePattern.Match(
                value);

        if (!match.Success)
        {
            return false;
        }

        string prefix =
            match.Groups["prefix"].Value;

        if (options.AllowedPrefixes is not null &&
            !options.AllowedPrefixes.Contains(
                prefix))
        {
            return false;
        }

        if (options.ExcludedPrefixes.Contains(
                prefix))
        {
            return false;
        }

        if (!int.TryParse(
                match.Groups["number"].Value,
                out int number) ||
            number < options.MinimumReferenceNumber ||
            number > options.MaximumReferenceNumber)
        {
            return false;
        }

        canonicalReference =
            prefix +
            match.Groups["number"].Value +
            match.Groups["suffix"].Value;

        return true;
    }

    /// <summary>
    /// Combina observaciones duplicadas producidas por varias capas de texto u
    /// OCR sobre la misma posición.
    /// </summary>
    private static IReadOnlyList<DetectedReference> MergeDuplicates(
        IReadOnlyList<DetectedReference> detected,
        BoardReferenceDetectorOptions options)
    {
        var accepted =
            new List<DetectedReference>();

        foreach (DetectedReference candidate in detected
                     .OrderByDescending(item =>
                         item.Observation.Confidence)
                     .ThenBy(item =>
                         item.Observation.PageIndex)
                     .ThenBy(item =>
                         item.Bounds.Top)
                     .ThenBy(item =>
                         item.Bounds.Left))
        {
            bool duplicate =
                accepted.Any(existing =>
                    existing.Observation.PageIndex ==
                        candidate.Observation.PageIndex &&
                    string.Equals(
                        existing.Reference,
                        candidate.Reference,
                        StringComparison.OrdinalIgnoreCase) &&
                    CalculateIntersectionOverUnion(
                        existing.Bounds,
                        candidate.Bounds) >=
                        options.DuplicateIntersectionThreshold);

            if (!duplicate)
            {
                accepted.Add(
                    candidate);
            }
        }

        return accepted;
    }

    private static bool CanEvaluate(
        BoardTextObservation observation,
        BoardReferenceDetectorOptions options)
    {
        return observation.Confidence >=
                   options.MinimumTextConfidence &&
               observation.Bounds.Width > 0 &&
               observation.Bounds.Height > 0 &&
               !string.IsNullOrWhiteSpace(
                   observation.Text);
    }

    private static char NormalizePrefixCharacter(
        char character)
    {
        return character switch
        {
            '0' => 'O',
            '1' => 'I',
            '5' => 'S',
            '8' => 'B',
            _ => character
        };
    }

    private static char NormalizeNumericCharacter(
        char character)
    {
        return character switch
        {
            'O' or 'Q' or 'D' => '0',
            'I' or 'L' => '1',
            'Z' => '2',
            'S' => '5',
            'B' => '8',
            _ => character
        };
    }

    private static int FindFirstDigitLikeCharacter(
        string value)
    {
        for (int index = 0;
             index < value.Length;
             index++)
        {
            char character =
                value[index];

            if (char.IsDigit(character) ||
                character is 'O' or 'Q' or 'D' or
                    'I' or 'L' or 'Z' or 'S' or 'B')
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsVertical(
        double rotationDegrees)
    {
        double normalized =
            rotationDegrees % 180D;

        if (normalized < 0D)
        {
            normalized += 180D;
        }

        return normalized >= 45D &&
               normalized <= 135D;
    }

    private static double CalculateIntersectionOverUnion(
        BoardGeometryBounds first,
        BoardGeometryBounds second)
    {
        int left =
            Math.Max(
                first.Left,
                second.Left);

        int top =
            Math.Max(
                first.Top,
                second.Top);

        int right =
            Math.Min(
                first.Right,
                second.Right);

        int bottom =
            Math.Min(
                first.Bottom,
                second.Bottom);

        if (right <= left ||
            bottom <= top)
        {
            return 0D;
        }

        long intersectionArea =
            checked(
                (long)(right - left) *
                (bottom - top));

        long firstArea =
            checked(
                (long)first.Width *
                first.Height);

        long secondArea =
            checked(
                (long)second.Width *
                second.Height);

        long unionArea =
            firstArea +
            secondArea -
            intersectionArea;

        return unionArea <= 0L
            ? 0D
            : (double)intersectionArea /
              unionArea;
    }

    private static void ValidateObservationIdentifiers(
        IReadOnlyList<BoardTextObservation> observations)
    {
        int uniqueCount =
            observations
                .Select(observation => observation.Id)
                .Distinct()
                .Count();

        if (uniqueCount != observations.Count)
        {
            throw new ArgumentException(
                "Los identificadores de observación deben ser únicos.",
                nameof(observations));
        }
    }

    private sealed record DetectedReference(
        BoardTextObservation Observation,
        BoardGeometryBounds Bounds,
        string Reference);

    private readonly record struct TokenSlice(
        string Text,
        BoardGeometryBounds Bounds);
}

/// <summary>
/// Observación textual producida por la capa de texto PDF o un motor OCR.
/// </summary>
public sealed record BoardTextObservation
{
    /// <summary>
    /// Inicializa una observación textual validada.
    /// </summary>
    public BoardTextObservation(
        int id,
        string text,
        BoardGeometryBounds bounds,
        double confidence,
        int pageIndex = 0,
        double rotationDegrees = 0D,
        string? sourceId = null)
    {
        if (id < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(id),
                id,
                "El identificador no puede ser negativo.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                "El texto no puede estar vacío.",
                nameof(text));
        }

        if (!double.IsFinite(confidence) ||
            confidence < 0D ||
            confidence > 1D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                confidence,
                "La confianza debe estar entre cero y uno.");
        }

        if (pageIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                pageIndex,
                "El índice de página no puede ser negativo.");
        }

        if (!double.IsFinite(rotationDegrees))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rotationDegrees),
                rotationDegrees,
                "La rotación debe ser finita.");
        }

        Id = id;
        Text = text.Trim();
        Bounds = bounds;
        Confidence = confidence;
        PageIndex = pageIndex;
        RotationDegrees = rotationDegrees;
        SourceId = string.IsNullOrWhiteSpace(sourceId)
            ? null
            : sourceId.Trim();
    }

    public int Id { get; }

    public string Text { get; }

    public BoardGeometryBounds Bounds { get; }

    public double Confidence { get; }

    public int PageIndex { get; }

    public double RotationDegrees { get; }

    public string? SourceId { get; }
}

/// <summary>
/// Configuración del detector de referencias.
/// </summary>
public sealed record BoardReferenceDetectorOptions
{
    private static readonly IReadOnlySet<string> DefaultPrefixes =
        new HashSet<string>(
            new[]
            {
                "R", "RN", "RA",
                "C", "CN",
                "L", "FB", "FL",
                "D", "ZD", "LED",
                "Q", "U", "IC",
                "J", "CN", "CON",
                "TP", "PP",
                "F", "Y", "X",
                "SW", "K",
                "T", "RT",
                "MH", "H"
            },
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Configuración predeterminada.
    /// </summary>
    public static BoardReferenceDetectorOptions Default { get; } =
        new();

    /// <summary>
    /// Confianza mínima de la observación textual.
    /// </summary>
    public double MinimumTextConfidence { get; init; } =
        0.50D;

    /// <summary>
    /// Longitud mínima de una referencia normalizada.
    /// </summary>
    public int MinimumReferenceLength { get; init; } =
        2;

    /// <summary>
    /// Longitud máxima de una referencia normalizada.
    /// </summary>
    public int MaximumReferenceLength { get; init; } =
        12;

    /// <summary>
    /// Número mínimo permitido en la referencia.
    /// </summary>
    public int MinimumReferenceNumber { get; init; } =
        0;

    /// <summary>
    /// Número máximo permitido en la referencia.
    /// </summary>
    public int MaximumReferenceNumber { get; init; } =
        99_999_999;

    /// <summary>
    /// Corrige confusiones frecuentes de OCR.
    /// </summary>
    public bool CorrectCommonOcrConfusions { get; init; } =
        true;

    /// <summary>
    /// Elimina observaciones duplicadas superpuestas.
    /// </summary>
    public bool MergeDuplicateObservations { get; init; } =
        true;

    /// <summary>
    /// Intersección mínima para considerar dos observaciones duplicadas.
    /// </summary>
    public double DuplicateIntersectionThreshold { get; init; } =
        0.65D;

    /// <summary>
    /// Prefijos permitidos. Null permite cualquier prefijo no excluido.
    /// </summary>
    public IReadOnlySet<string>? AllowedPrefixes { get; init; } =
        DefaultPrefixes;

    /// <summary>
    /// Prefijos que nunca deben aceptarse.
    /// </summary>
    public IReadOnlySet<string> ExcludedPrefixes { get; init; } =
        new HashSet<string>(
            new[]
            {
                "REV",
                "PAGE",
                "PCB",
                "TOP",
                "BOT",
                "BOTTOM",
                "SIDE",
                "NOTE"
            },
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Valida la configuración.
    /// </summary>
    public void Validate()
    {
        if (!double.IsFinite(
                MinimumTextConfidence) ||
            MinimumTextConfidence < 0D ||
            MinimumTextConfidence > 1D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumTextConfidence));
        }

        if (MinimumReferenceLength < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumReferenceLength));
        }

        if (MaximumReferenceLength <
            MinimumReferenceLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumReferenceLength));
        }

        if (MinimumReferenceNumber < 0 ||
            MaximumReferenceNumber <
                MinimumReferenceNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumReferenceNumber));
        }

        if (!double.IsFinite(
                DuplicateIntersectionThreshold) ||
            DuplicateIntersectionThreshold < 0D ||
            DuplicateIntersectionThreshold > 1D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DuplicateIntersectionThreshold));
        }
    }
}
