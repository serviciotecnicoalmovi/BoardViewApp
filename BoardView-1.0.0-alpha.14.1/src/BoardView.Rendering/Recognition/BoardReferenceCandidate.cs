using System.Text.RegularExpressions;
using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Representa una referencia textual candidata detectada dentro del documento.
/// </summary>
/// <remarks>
/// Esta clase no ejecuta OCR. Únicamente normaliza y conserva candidatos
/// producidos por cualquier extractor de texto futuro.
/// </remarks>
public sealed record BoardReferenceCandidate
{
    private static readonly Regex ReferencePattern =
        new(
            @"^(?<prefix>[A-Z]{1,4})(?<number>\d{1,8})(?<suffix>[A-Z]?)$",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase);

    /// <summary>
    /// Inicializa un candidato de referencia.
    /// </summary>
    public BoardReferenceCandidate(
        int id,
        string rawText,
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

        if (string.IsNullOrWhiteSpace(rawText))
        {
            throw new ArgumentException(
                "El texto candidato no puede estar vacío.",
                nameof(rawText));
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
                "La rotación debe ser un número finito.");
        }

        Id = id;
        RawText = rawText.Trim();
        NormalizedReference = NormalizeReference(RawText);
        Bounds = bounds;
        Confidence = confidence;
        PageIndex = pageIndex;
        RotationDegrees = NormalizeRotation(rotationDegrees);
        SourceId = string.IsNullOrWhiteSpace(sourceId)
            ? null
            : sourceId.Trim();
    }

    /// <summary>
    /// Identificador interno del candidato.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Texto original producido por el extractor.
    /// </summary>
    public string RawText { get; }

    /// <summary>
    /// Texto normalizado para búsquedas y asociación.
    /// </summary>
    public string NormalizedReference { get; }

    /// <summary>
    /// Límites del texto en coordenadas del render original.
    /// </summary>
    public BoardGeometryBounds Bounds { get; }

    /// <summary>
    /// Confianza del extractor textual.
    /// </summary>
    public double Confidence { get; }

    /// <summary>
    /// Índice de página cero-basado.
    /// </summary>
    public int PageIndex { get; }

    /// <summary>
    /// Rotación normalizada en grados dentro del intervalo [0, 360).
    /// </summary>
    public double RotationDegrees { get; }

    /// <summary>
    /// Identificador opcional proporcionado por el extractor.
    /// </summary>
    public string? SourceId { get; }

    /// <summary>
    /// Centro horizontal del candidato.
    /// </summary>
    public double CenterX =>
        Bounds.Left +
        (Bounds.Width / 2D);

    /// <summary>
    /// Centro vertical del candidato.
    /// </summary>
    public double CenterY =>
        Bounds.Top +
        (Bounds.Height / 2D);

    /// <summary>
    /// Indica si el texto coincide con el patrón general de referencia.
    /// </summary>
    public bool IsReferenceLike =>
        ReferencePattern.IsMatch(
            NormalizedReference);

    /// <summary>
    /// Prefijo alfabético de la referencia, cuando el formato es válido.
    /// </summary>
    public string? Prefix
    {
        get
        {
            Match match =
                ReferencePattern.Match(
                    NormalizedReference);

            return match.Success
                ? match.Groups["prefix"].Value
                : null;
        }
    }

    /// <summary>
    /// Normaliza una referencia textual.
    /// </summary>
    public static string NormalizeReference(
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value);

        Span<char> buffer =
            stackalloc char[value.Length];

        int length = 0;

        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] =
                    char.ToUpperInvariant(
                        character);
            }
        }

        return new string(
            buffer[..length]);
    }

    private static double NormalizeRotation(
        double value)
    {
        double normalized =
            value % 360D;

        return normalized < 0D
            ? normalized + 360D
            : normalized;
    }
}