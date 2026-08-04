using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BoardView.Rendering.Geometry;

/// <summary>
/// Clasifica componentes geométricos mediante reglas deterministas basadas
/// en tamaño, proporción, densidad, cobertura y contacto con los bordes.
/// </summary>
/// <remarks>
/// Esta primera versión no utiliza OCR ni modelos de aprendizaje automático.
/// Su objetivo es proporcionar una clasificación geométrica inicial y una
/// puntuación de confianza reproducible.
///
/// Las categorías son heurísticas. Un componente puede conservar el tipo
/// <see cref="BoardGeometryComponentType.Unknown"/> cuando sus características
/// no permiten una decisión suficientemente segura.
/// </remarks>
public sealed class BoardGeometryComponentClassifier
{
    /// <summary>
    /// Opciones predeterminadas del clasificador.
    /// </summary>
    public static BoardGeometryComponentClassifierOptions DefaultOptions { get; } =
        new();

    /// <summary>
    /// Clasifica todos los componentes con las opciones predeterminadas.
    /// </summary>
    public BoardGeometryComponentClassificationResult Classify(
        BoardGeometryComponentsResult components)
    {
        return Classify(
            components,
            DefaultOptions);
    }

    /// <summary>
    /// Clasifica todos los componentes detectados.
    /// </summary>
    public BoardGeometryComponentClassificationResult Classify(
        BoardGeometryComponentsResult components,
        BoardGeometryComponentClassifierOptions options)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(options);

        ValidateOptions(options);

        var classifications =
            new List<BoardGeometryComponentClassification>(
                components.ComponentCount);

        foreach (BoardGeometryComponent component in components.Components)
        {
            BoardGeometryComponentFeatures features =
                ExtractFeatures(
                    component,
                    components.MaskWidth,
                    components.MaskHeight,
                    options);

            BoardGeometryComponentClassification classification =
                ClassifyComponent(
                    component,
                    features,
                    options);

            classifications.Add(classification);
        }

        IReadOnlyDictionary<BoardGeometryComponentType, int> counts =
            BuildCounts(classifications);

        return new BoardGeometryComponentClassificationResult(
            new ReadOnlyCollection<BoardGeometryComponentClassification>(
                classifications),
            counts,
            components.MaskWidth,
            components.MaskHeight);
    }

    /// <summary>
    /// Extrae las características geométricas utilizadas por las reglas.
    /// </summary>
    private static BoardGeometryComponentFeatures ExtractFeatures(
        BoardGeometryComponent component,
        int pageWidth,
        int pageHeight,
        BoardGeometryComponentClassifierOptions options)
    {
        BoardGeometryBounds bounds =
            component.Bounds;

        double aspectRatio =
            bounds.Height == 0
                ? 0D
                : (double)bounds.Width /
                  bounds.Height;

        double normalizedAspectRatio =
            aspectRatio <= 0D
                ? 0D
                : Math.Max(
                    aspectRatio,
                    1D / aspectRatio);

        double widthCoverage =
            (double)bounds.Width /
            pageWidth;

        double heightCoverage =
            (double)bounds.Height /
            pageHeight;

        double boundsCoverage =
            (double)component.BoundsArea /
            checked((long)pageWidth * pageHeight);

        bool touchesLeft =
            bounds.Left <=
            options.BorderTolerancePixels;

        bool touchesTop =
            bounds.Top <=
            options.BorderTolerancePixels;

        bool touchesRight =
            bounds.Right >=
            pageWidth -
            options.BorderTolerancePixels;

        bool touchesBottom =
            bounds.Bottom >=
            pageHeight -
            options.BorderTolerancePixels;

        int touchedEdgeCount =
            (touchesLeft ? 1 : 0) +
            (touchesTop ? 1 : 0) +
            (touchesRight ? 1 : 0) +
            (touchesBottom ? 1 : 0);

        double centerX =
            bounds.Left +
            (bounds.Width / 2D);

        double centerY =
            bounds.Top +
            (bounds.Height / 2D);

        double normalizedCenterX =
            centerX /
            pageWidth;

        double normalizedCenterY =
            centerY /
            pageHeight;

        double squareness =
            normalizedAspectRatio <= 0D
                ? 0D
                : 1D /
                  normalizedAspectRatio;

        return new BoardGeometryComponentFeatures(
            aspectRatio,
            normalizedAspectRatio,
            squareness,
            component.Density,
            component.MaskCoverage,
            widthCoverage,
            heightCoverage,
            boundsCoverage,
            touchedEdgeCount,
            normalizedCenterX,
            normalizedCenterY);
    }

    /// <summary>
    /// Aplica las reglas de clasificación en orden de prioridad.
    /// </summary>
    private static BoardGeometryComponentClassification ClassifyComponent(
        BoardGeometryComponent component,
        BoardGeometryComponentFeatures features,
        BoardGeometryComponentClassifierOptions options)
    {
        ClassificationCandidate noise =
            ScoreNoise(
                component,
                features,
                options);

        ClassificationCandidate frame =
            ScoreBoardOutline(
                component,
                features,
                options);

        ClassificationCandidate hole =
            ScoreHole(
                component,
                features,
                options);

        ClassificationCandidate pad =
            ScorePad(
                component,
                features,
                options);

        ClassificationCandidate text =
            ScoreText(
                component,
                features,
                options);

        ClassificationCandidate componentBody =
            ScoreComponentBody(
                component,
                features,
                options);

        ClassificationCandidate silkscreen =
            ScoreSilkscreen(
                component,
                features,
                options);

        ClassificationCandidate copper =
            ScoreCopper(
                component,
                features,
                options);

        ClassificationCandidate best =
            new(
                BoardGeometryComponentType.Unknown,
                0D,
                "No se encontró una regla suficientemente sólida.");

        ClassificationCandidate[] candidates =
        [
            noise,
            frame,
            hole,
            pad,
            text,
            componentBody,
            silkscreen,
            copper
        ];

        foreach (ClassificationCandidate candidate in candidates)
        {
            if (candidate.Confidence >
                best.Confidence)
            {
                best = candidate;
            }
        }

        if (best.Confidence <
            options.MinimumAcceptedConfidence)
        {
            best =
                new ClassificationCandidate(
                    BoardGeometryComponentType.Unknown,
                    best.Confidence,
                    "La mejor puntuación quedó por debajo del mínimo aceptado.");
        }

        return new BoardGeometryComponentClassification(
            component,
            features,
            best.Type,
            Clamp01(best.Confidence),
            best.Reason);
    }

    private static ClassificationCandidate ScoreNoise(
        BoardGeometryComponent component,
        BoardGeometryComponentFeatures features,
        BoardGeometryComponentClassifierOptions options)
    {
        double score = 0D;

        if (component.PixelCount <=
            options.NoiseMaximumPixelCount)
        {
            score += 0.65D;
        }

        if (component.BoundsArea <=
            options.NoiseMaximumBoundsArea)
        {
            score += 0.20D;
        }

        if (features.BoundsCoverage <=
            options.NoiseMaximumBoundsCoverage)
        {
            score += 0.15D;
        }

        return new ClassificationCandidate(
            BoardGeometryComponentType.Noise,
            score,
            "Componente pequeño con cobertura mínima.");
    }

    private static ClassificationCandidate ScoreBoardOutline(
        BoardGeometryComponent component,
        BoardGeometryComponentFeatures features,
        BoardGeometryComponentClassifierOptions options)
    {
        double score = 0D;

        if (features.WidthCoverage >=
            options.OutlineMinimumWidthCoverage)
        {
            score += 0.25D;
        }

        if (features.HeightCoverage >=
            options.OutlineMinimumHeightCoverage)
        {
            score += 0.25D;
        }

        if (features.BoundsCoverage >=
            options.OutlineMinimumBoundsCoverage)
        {
            score += 0.20D;
        }

        if (features.TouchedEdgeCount >=
            options.OutlineMinimumTouchedEdges)
        {
            score += 0.15D;
        }

        if (features.Density <=
            options.OutlineMaximumDensity)
        {
            score += 0.15D;
        }

        return new ClassificationCandidate(
            BoardGeometryComponentType.BoardOutline,
            score,
            "Región extensa, poco densa y próxima a los bordes.");
    }

    private static ClassificationCandidate ScoreHole(
        BoardGeometryComponent component,
        BoardGeometryComponentFeatures features,
        BoardGeometryComponentClassifierOptions options)
    {
        double score = 0D;

        if (component.PixelCount >=
            options.HoleMinimumPixelCount &&
            component.PixelCount <=
            options.HoleMaximumPixelCount)
        {
            score += 0.25D;
        }

        if (features.Squareness >=
            options.HoleMinimumSquareness)
        {
            score += 0.25D;
        }

        if (features.Density >=
            options.HoleMinimumDensity &&
            features.Density <=
            options.HoleMaximumDensity)
        {
            score += 0.30D;
        }

        if (features.BoundsCoverage <=
            options.HoleMaximumBoundsCoverage)
        {
            score += 0.20D;
        }

        return new ClassificationCandidate(
            BoardGeometryComponentType.Hole,
            score,
            "Componente compacto, aproximadamente cuadrado y de densidad media.");
    }

    private static ClassificationCandidate ScorePad(
        BoardGeometryComponent component,
        BoardGeometryComponentFeatures features,
        BoardGeometryComponentClassifierOptions options)
    {
        double score = 0D;

        if (component.PixelCount >=
            options.PadMinimumPixelCount &&
            component.PixelCount <=
            options.PadMaximumPixelCount)
        {
            score += 0.25D;
        }

        if (features.NormalizedAspectRatio <=
            options.PadMaximumAspectRatio)
        {
            score += 0.20D;
        }

        if (features.Density >=
            options.PadMinimumDensity)
        {
            score += 0.35D;
        }

        if (features.BoundsCoverage <=
            options.PadMaximumBoundsCoverage)
        {
            score += 0.20D;
        }

        return new ClassificationCandidate(
            BoardGeometryComponentType.Pad,
            score,
            "Componente pequeño o mediano, compacto y con alta densidad.");
    }

    private static ClassificationCandidate ScoreText(
        BoardGeometryComponent component,
        BoardGeometryComponentFeatures features,
        BoardGeometryComponentClassifierOptions options)
    {
        double score = 0D;

        if (component.PixelCount >=
            options.TextMinimumPixelCount &&
            component.PixelCount <=
            options.TextMaximumPixelCount)
        {
            score += 0.20D;
        }

        if (features.NormalizedAspectRatio >=
            options.TextMinimumAspectRatio)
        {
            score += 0.30D;
        }

        if (features.Density >=
            options.TextMinimumDensity &&
            features.Density <=
            options.TextMaximumDensity)
        {
            score += 0.30D;
        }

        if (features.BoundsCoverage <=
            options.TextMaximumBoundsCoverage)
        {
            score += 0.20D;
        }

        return new ClassificationCandidate(
            BoardGeometryComponentType.Text,
            score,
            "Trazo alargado con densidad compatible con caracteres.");
    }

    private static ClassificationCandidate ScoreComponentBody(
        BoardGeometryComponent component,
        BoardGeometryComponentFeatures features,
        BoardGeometryComponentClassifierOptions options)
    {
        double score = 0D;

        if (component.PixelCount >=
            options.ComponentBodyMinimumPixelCount)
        {
            score += 0.25D;
        }

        if (features.BoundsCoverage >=
            options.ComponentBodyMinimumBoundsCoverage &&
            features.BoundsCoverage <=
            options.ComponentBodyMaximumBoundsCoverage)
        {
            score += 0.25D;
        }

        if (features.Density >=
            options.ComponentBodyMinimumDensity &&
            features.Density <=
            options.ComponentBodyMaximumDensity)
        {
            score += 0.25D;
        }

        if (features.NormalizedAspectRatio <=
            options.ComponentBodyMaximumAspectRatio)
        {
            score += 0.25D;
        }

        return new ClassificationCandidate(
            BoardGeometryComponentType.ComponentBody,
            score,
            "Región mediana o grande con proporción y densidad de encapsulado.");
    }

    private static ClassificationCandidate ScoreSilkscreen(
        BoardGeometryComponent component,
        BoardGeometryComponentFeatures features,
        BoardGeometryComponentClassifierOptions options)
    {
        double score = 0D;

        if (component.PixelCount >=
            options.SilkscreenMinimumPixelCount)
        {
            score += 0.20D;
        }

        if (features.Density >=
            options.SilkscreenMinimumDensity &&
            features.Density <=
            options.SilkscreenMaximumDensity)
        {
            score += 0.35D;
        }

        if (features.BoundsCoverage <=
            options.SilkscreenMaximumBoundsCoverage)
        {
            score += 0.20D;
        }

        if (features.TouchedEdgeCount == 0)
        {
            score += 0.25D;
        }

        return new ClassificationCandidate(
            BoardGeometryComponentType.Silkscreen,
            score,
            "Trazo interior de densidad baja o media.");
    }

    private static ClassificationCandidate ScoreCopper(
        BoardGeometryComponent component,
        BoardGeometryComponentFeatures features,
        BoardGeometryComponentClassifierOptions options)
    {
        double score = 0D;

        if (component.PixelCount >=
            options.CopperMinimumPixelCount)
        {
            score += 0.25D;
        }

        if (features.NormalizedAspectRatio >=
            options.CopperMinimumAspectRatio)
        {
            score += 0.25D;
        }

        if (features.Density <=
            options.CopperMaximumDensity)
        {
            score += 0.25D;
        }

        if (features.BoundsCoverage >=
            options.CopperMinimumBoundsCoverage)
        {
            score += 0.25D;
        }

        return new ClassificationCandidate(
            BoardGeometryComponentType.Copper,
            score,
            "Trazo extenso, alargado y poco denso.");
    }

    private static IReadOnlyDictionary<BoardGeometryComponentType, int> BuildCounts(
        IReadOnlyList<BoardGeometryComponentClassification> classifications)
    {
        var counts =
            Enum.GetValues<BoardGeometryComponentType>()
                .ToDictionary(
                    type => type,
                    _ => 0);

        foreach (BoardGeometryComponentClassification classification in classifications)
        {
            counts[classification.Type]++;
        }

        return new ReadOnlyDictionary<BoardGeometryComponentType, int>(
            counts);
    }

    private static double Clamp01(double value)
    {
        return Math.Max(
            0D,
            Math.Min(
                1D,
                value));
    }

    private static void ValidateOptions(
        BoardGeometryComponentClassifierOptions options)
    {
        if (options.BorderTolerancePixels < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.BorderTolerancePixels));
        }

        ValidateFraction(
            options.MinimumAcceptedConfidence,
            nameof(options.MinimumAcceptedConfidence));

        ValidateFraction(
            options.NoiseMaximumBoundsCoverage,
            nameof(options.NoiseMaximumBoundsCoverage));

        ValidateFraction(
            options.OutlineMinimumWidthCoverage,
            nameof(options.OutlineMinimumWidthCoverage));

        ValidateFraction(
            options.OutlineMinimumHeightCoverage,
            nameof(options.OutlineMinimumHeightCoverage));

        ValidateFraction(
            options.OutlineMinimumBoundsCoverage,
            nameof(options.OutlineMinimumBoundsCoverage));

        ValidateFraction(
            options.OutlineMaximumDensity,
            nameof(options.OutlineMaximumDensity));

        ValidateFraction(
            options.HoleMinimumSquareness,
            nameof(options.HoleMinimumSquareness));

        ValidateFraction(
            options.HoleMinimumDensity,
            nameof(options.HoleMinimumDensity));

        ValidateFraction(
            options.HoleMaximumDensity,
            nameof(options.HoleMaximumDensity));

        ValidateFraction(
            options.HoleMaximumBoundsCoverage,
            nameof(options.HoleMaximumBoundsCoverage));

        ValidateFraction(
            options.PadMinimumDensity,
            nameof(options.PadMinimumDensity));

        ValidateFraction(
            options.PadMaximumBoundsCoverage,
            nameof(options.PadMaximumBoundsCoverage));

        ValidateFraction(
            options.TextMinimumDensity,
            nameof(options.TextMinimumDensity));

        ValidateFraction(
            options.TextMaximumDensity,
            nameof(options.TextMaximumDensity));

        ValidateFraction(
            options.TextMaximumBoundsCoverage,
            nameof(options.TextMaximumBoundsCoverage));

        ValidateFraction(
            options.ComponentBodyMinimumBoundsCoverage,
            nameof(options.ComponentBodyMinimumBoundsCoverage));

        ValidateFraction(
            options.ComponentBodyMaximumBoundsCoverage,
            nameof(options.ComponentBodyMaximumBoundsCoverage));

        ValidateFraction(
            options.ComponentBodyMinimumDensity,
            nameof(options.ComponentBodyMinimumDensity));

        ValidateFraction(
            options.ComponentBodyMaximumDensity,
            nameof(options.ComponentBodyMaximumDensity));

        ValidateFraction(
            options.SilkscreenMinimumDensity,
            nameof(options.SilkscreenMinimumDensity));

        ValidateFraction(
            options.SilkscreenMaximumDensity,
            nameof(options.SilkscreenMaximumDensity));

        ValidateFraction(
            options.SilkscreenMaximumBoundsCoverage,
            nameof(options.SilkscreenMaximumBoundsCoverage));

        ValidateFraction(
            options.CopperMaximumDensity,
            nameof(options.CopperMaximumDensity));

        ValidateFraction(
            options.CopperMinimumBoundsCoverage,
            nameof(options.CopperMinimumBoundsCoverage));
    }

    private static void ValidateFraction(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) ||
            value < 0D ||
            value > 1D)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "El valor debe estar entre cero y uno.");
        }
    }

    private readonly record struct ClassificationCandidate(
        BoardGeometryComponentType Type,
        double Confidence,
        string Reason);
}

/// <summary>
/// Tipos geométricos reconocidos por el clasificador heurístico.
/// </summary>
public enum BoardGeometryComponentType
{
    Unknown = 0,
    Noise = 1,
    Hole = 2,
    Pad = 3,
    Silkscreen = 4,
    Text = 5,
    BoardOutline = 6,
    Copper = 7,
    ComponentBody = 8
}

/// <summary>
/// Características normalizadas de un componente conectado.
/// </summary>
public readonly record struct BoardGeometryComponentFeatures(
    double AspectRatio,
    double NormalizedAspectRatio,
    double Squareness,
    double Density,
    double MaskCoverage,
    double WidthCoverage,
    double HeightCoverage,
    double BoundsCoverage,
    int TouchedEdgeCount,
    double NormalizedCenterX,
    double NormalizedCenterY);

/// <summary>
/// Clasificación individual de un componente conectado.
/// </summary>
public sealed record BoardGeometryComponentClassification(
    BoardGeometryComponent Component,
    BoardGeometryComponentFeatures Features,
    BoardGeometryComponentType Type,
    double Confidence,
    string Reason);

/// <summary>
/// Resultado completo del clasificador.
/// </summary>
public sealed class BoardGeometryComponentClassificationResult
{
    public BoardGeometryComponentClassificationResult(
        IReadOnlyList<BoardGeometryComponentClassification> classifications,
        IReadOnlyDictionary<BoardGeometryComponentType, int> counts,
        int pageWidth,
        int pageHeight)
    {
        ArgumentNullException.ThrowIfNull(classifications);
        ArgumentNullException.ThrowIfNull(counts);

        if (pageWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageWidth));
        }

        if (pageHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageHeight));
        }

        Classifications = classifications;
        Counts = counts;
        PageWidth = pageWidth;
        PageHeight = pageHeight;
    }

    public IReadOnlyList<BoardGeometryComponentClassification> Classifications
    {
        get;
    }

    public IReadOnlyDictionary<BoardGeometryComponentType, int> Counts
    {
        get;
    }

    public int PageWidth { get; }

    public int PageHeight { get; }

    public int ClassificationCount =>
        Classifications.Count;

    public int GetCount(
        BoardGeometryComponentType type)
    {
        return Counts.TryGetValue(
            type,
            out int count)
                ? count
                : 0;
    }

    public IEnumerable<BoardGeometryComponentClassification> OfType(
        BoardGeometryComponentType type)
    {
        return Classifications.Where(
            classification =>
                classification.Type == type);
    }
}

/// <summary>
/// Umbrales configurables del clasificador heurístico.
/// </summary>
public sealed record BoardGeometryComponentClassifierOptions
{
    public int BorderTolerancePixels { get; init; } = 8;

    public double MinimumAcceptedConfidence { get; init; } = 0.55D;

    public long NoiseMaximumPixelCount { get; init; } = 10L;
    public long NoiseMaximumBoundsArea { get; init; } = 24L;
    public double NoiseMaximumBoundsCoverage { get; init; } = 0.00001D;

    public double OutlineMinimumWidthCoverage { get; init; } = 0.80D;
    public double OutlineMinimumHeightCoverage { get; init; } = 0.80D;
    public double OutlineMinimumBoundsCoverage { get; init; } = 0.60D;
    public int OutlineMinimumTouchedEdges { get; init; } = 2;
    public double OutlineMaximumDensity { get; init; } = 0.10D;

    public long HoleMinimumPixelCount { get; init; } = 20L;
    public long HoleMaximumPixelCount { get; init; } = 6000L;
    public double HoleMinimumSquareness { get; init; } = 0.72D;
    public double HoleMinimumDensity { get; init; } = 0.08D;
    public double HoleMaximumDensity { get; init; } = 0.58D;
    public double HoleMaximumBoundsCoverage { get; init; } = 0.03D;

    public long PadMinimumPixelCount { get; init; } = 8L;
    public long PadMaximumPixelCount { get; init; } = 3000L;
    public double PadMaximumAspectRatio { get; init; } = 4D;
    public double PadMinimumDensity { get; init; } = 0.48D;
    public double PadMaximumBoundsCoverage { get; init; } = 0.01D;

    public long TextMinimumPixelCount { get; init; } = 6L;
    public long TextMaximumPixelCount { get; init; } = 2500L;
    public double TextMinimumAspectRatio { get; init; } = 2.2D;
    public double TextMinimumDensity { get; init; } = 0.08D;
    public double TextMaximumDensity { get; init; } = 0.72D;
    public double TextMaximumBoundsCoverage { get; init; } = 0.015D;

    public long ComponentBodyMinimumPixelCount { get; init; } = 150L;
    public double ComponentBodyMinimumBoundsCoverage { get; init; } = 0.0002D;
    public double ComponentBodyMaximumBoundsCoverage { get; init; } = 0.08D;
    public double ComponentBodyMinimumDensity { get; init; } = 0.10D;
    public double ComponentBodyMaximumDensity { get; init; } = 0.75D;
    public double ComponentBodyMaximumAspectRatio { get; init; } = 8D;

    public long SilkscreenMinimumPixelCount { get; init; } = 12L;
    public double SilkscreenMinimumDensity { get; init; } = 0.02D;
    public double SilkscreenMaximumDensity { get; init; } = 0.35D;
    public double SilkscreenMaximumBoundsCoverage { get; init; } = 0.08D;

    public long CopperMinimumPixelCount { get; init; } = 80L;
    public double CopperMinimumAspectRatio { get; init; } = 4D;
    public double CopperMaximumDensity { get; init; } = 0.25D;
    public double CopperMinimumBoundsCoverage { get; init; } = 0.0005D;
}
