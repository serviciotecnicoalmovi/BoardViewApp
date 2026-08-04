using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BoardView.Rendering.Geometry;

/// <summary>
/// Selecciona y agrupa los componentes conectados que probablemente
/// representan la región útil de una placa o de un esquemático.
/// </summary>
/// <remarks>
/// Un documento técnico no siempre contiene una única figura conectada.
/// Las pistas, encapsulados, textos, marcos y símbolos suelen formar
/// componentes independientes.
///
/// Esta clase no altera la máscara original. Recibe la clasificación
/// completa producida por <see cref="BoardGeometryComponentClassifier"/>,
/// descarta categorías semánticas irrelevantes, aplica los filtros
/// geométricos existentes y combina los componentes útiles en un único
/// rectángulo.
///
/// El selector aplica las siguientes reglas:
///
/// <list type="number">
/// <item>Descarta componentes demasiado pequeños.</item>
/// <item>Descarta marcos que ocupan casi toda la página.</item>
/// <item>Calcula una región semilla con los componentes más significativos.</item>
/// <item>Agrupa componentes cercanos a la región semilla.</item>
/// <item>Devuelve la unión geométrica de los componentes seleccionados.</item>
/// </list>
/// </remarks>
public sealed class BoardGeometryComponentSelector
{
    /// <summary>
    /// Opciones predeterminadas del selector.
    /// </summary>
    public static BoardGeometryComponentSelectorOptions DefaultOptions { get; } =
        new();

    /// <summary>
    /// Selecciona los componentes clasificados utilizando las opciones
    /// predeterminadas.
    /// </summary>
    public BoardGeometryComponentSelectionResult Select(
        BoardGeometryComponentClassificationResult classification)
    {
        return Select(
            classification,
            DefaultOptions);
    }

    /// <summary>
    /// Selecciona y agrupa los componentes clasificados relevantes.
    /// </summary>
    /// <param name="classification">
    /// Clasificación completa de todos los componentes conectados.
    /// </param>
    /// <param name="options">
    /// Opciones de filtrado semántico, agrupación y descarte geométrico.
    /// </param>
    /// <returns>
    /// Resultado inmutable con los componentes aceptados, límites combinados
    /// y estadísticas de aceptación y descarte por tipo.
    /// </returns>
    public BoardGeometryComponentSelectionResult Select(
        BoardGeometryComponentClassificationResult classification,
        BoardGeometryComponentSelectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(options);

        ValidateOptions(options);

        if (classification.ClassificationCount == 0)
        {
            return BoardGeometryComponentSelectionResult.Empty(
                classification.PageWidth,
                classification.PageHeight);
        }

        long pageArea =
            checked(
                (long)classification.PageWidth *
                classification.PageHeight);

        long minimumPixelCount =
            Math.Max(
                options.MinimumAbsolutePixelCount,
                checked(
                    (long)Math.Ceiling(
                        pageArea *
                        options.MinimumPageCoverage)));

        var acceptedByType =
            CreateTypeCounter();

        var discardedByType =
            CreateTypeCounter();

        var candidates =
            new List<SemanticCandidate>(
                classification.ClassificationCount);

        foreach (BoardGeometryComponentClassification item
                 in classification.Classifications)
        {
            if (IsSemanticallyExcluded(
                    item,
                    options))
            {
                discardedByType[item.Type]++;
                continue;
            }

            if (!IsCandidate(
                    item.Component,
                    classification.PageWidth,
                    classification.PageHeight,
                    minimumPixelCount,
                    options))
            {
                discardedByType[item.Type]++;
                continue;
            }

            candidates.Add(
                new SemanticCandidate(
                    item.Component,
                    item.Type,
                    item.Confidence));
        }

        candidates =
            candidates
                .OrderByDescending(candidate =>
                    GetSemanticPriority(candidate.Type))
                .ThenByDescending(candidate =>
                    candidate.Confidence)
                .ThenByDescending(candidate =>
                    candidate.Component.PixelCount)
                .ThenByDescending(candidate =>
                    candidate.Component.BoundsArea)
                .ToList();

        if (candidates.Count == 0)
        {
            return CreateLargestFallback(
                classification,
                acceptedByType,
                discardedByType);
        }

        IReadOnlyList<SemanticCandidate> seeds =
            SelectSeedComponents(
                candidates,
                options);

        BoardGeometryBounds seedBounds =
            UnionBounds(
                seeds
                    .Select(seed => seed.Component)
                    .ToList());

        var selected =
            new List<SemanticCandidate>(seeds);

        var selectedIds =
            new HashSet<int>(
                seeds.Select(seed => seed.Component.Id));

        bool changed;

        do
        {
            changed = false;

            foreach (SemanticCandidate candidate in candidates)
            {
                if (selectedIds.Contains(candidate.Component.Id))
                {
                    continue;
                }

                if (!ShouldJoinSelection(
                        candidate.Component,
                        seedBounds,
                        classification.PageWidth,
                        classification.PageHeight,
                        options))
                {
                    continue;
                }

                selected.Add(candidate);
                selectedIds.Add(candidate.Component.Id);

                seedBounds =
                    UnionBounds(
                        seedBounds,
                        candidate.Component.Bounds);

                changed = true;
            }
        }
        while (changed);

        foreach (SemanticCandidate candidate in candidates)
        {
            if (selectedIds.Contains(candidate.Component.Id))
            {
                acceptedByType[candidate.Type]++;
            }
            else
            {
                discardedByType[candidate.Type]++;
            }
        }

        List<BoardGeometryComponent> selectedComponents =
            selected
                .Select(candidate => candidate.Component)
                .OrderBy(component => component.Bounds.Top)
                .ThenBy(component => component.Bounds.Left)
                .ThenBy(component => component.Id)
                .ToList();

        BoardGeometryBounds selectedBounds =
            UnionBounds(selectedComponents);

        selectedBounds =
            ApplyPadding(
                selectedBounds,
                classification.PageWidth,
                classification.PageHeight,
                options.PaddingPixels);

        long selectedPixelCount =
            selectedComponents.Sum(
                component => component.PixelCount);

        return new BoardGeometryComponentSelectionResult(
            new ReadOnlyCollection<BoardGeometryComponent>(
                selectedComponents),
            selectedBounds,
            selectedPixelCount,
            classification.PageWidth,
            classification.PageHeight,
            usedFallback: false,
            new ReadOnlyDictionary<BoardGeometryComponentType, int>(
                acceptedByType),
            new ReadOnlyDictionary<BoardGeometryComponentType, int>(
                discardedByType));
    }

    /// <summary>
    /// Determina si una clasificación debe excluirse antes de aplicar
    /// cualquier filtro geométrico.
    /// </summary>
    private static bool IsSemanticallyExcluded(
        BoardGeometryComponentClassification classification,
        BoardGeometryComponentSelectorOptions options)
    {
        return classification.Type switch
        {
            BoardGeometryComponentType.Noise =>
                options.ExcludeNoise,

            BoardGeometryComponentType.Hole =>
                options.ExcludeHoles,

            BoardGeometryComponentType.BoardOutline =>
                options.ExcludeBoardOutline,

            BoardGeometryComponentType.Unknown =>
                classification.Confidence <
                options.MinimumUnknownConfidence,

            _ => false
        };
    }

    /// <summary>
    /// Asigna prioridad a los tipos que normalmente describen contenido
    /// técnico útil.
    /// </summary>
    private static int GetSemanticPriority(
        BoardGeometryComponentType type)
    {
        return type switch
        {
            BoardGeometryComponentType.ComponentBody => 8,
            BoardGeometryComponentType.Pad => 7,
            BoardGeometryComponentType.Copper => 6,
            BoardGeometryComponentType.Silkscreen => 5,
            BoardGeometryComponentType.Text => 4,
            BoardGeometryComponentType.Unknown => 3,
            BoardGeometryComponentType.Hole => 2,
            BoardGeometryComponentType.BoardOutline => 1,
            BoardGeometryComponentType.Noise => 0,
            _ => 0
        };
    }

    /// <summary>
    /// Determina si un componente puede participar en la selección.
    /// </summary>
    private static bool IsCandidate(
        BoardGeometryComponent component,
        int pageWidth,
        int pageHeight,
        long minimumPixelCount,
        BoardGeometryComponentSelectorOptions options)
    {
        if (component.PixelCount < minimumPixelCount)
        {
            return false;
        }

        double widthCoverage =
            (double)component.Bounds.Width /
            pageWidth;

        double heightCoverage =
            (double)component.Bounds.Height /
            pageHeight;

        double boundsCoverage =
            (double)component.BoundsArea /
            checked((long)pageWidth * pageHeight);

        bool touchesLeft =
            component.Bounds.Left <=
            options.FrameBorderTolerancePixels;

        bool touchesTop =
            component.Bounds.Top <=
            options.FrameBorderTolerancePixels;

        bool touchesRight =
            component.Bounds.Right >=
            pageWidth -
            options.FrameBorderTolerancePixels;

        bool touchesBottom =
            component.Bounds.Bottom >=
            pageHeight -
            options.FrameBorderTolerancePixels;

        int touchedEdges =
            (touchesLeft ? 1 : 0) +
            (touchesTop ? 1 : 0) +
            (touchesRight ? 1 : 0) +
            (touchesBottom ? 1 : 0);

        bool probablePageFrame =
            touchedEdges >= options.MinimumTouchedEdgesForFrame &&
            widthCoverage >= options.MinimumFrameWidthCoverage &&
            heightCoverage >= options.MinimumFrameHeightCoverage &&
            boundsCoverage >= options.MinimumFrameBoundsCoverage &&
            component.Density <= options.MaximumFrameDensity;

        return !probablePageFrame;
    }

    /// <summary>
    /// Selecciona los componentes iniciales que definen la región semilla.
    /// </summary>
    private static IReadOnlyList<SemanticCandidate> SelectSeedComponents(
        IReadOnlyList<SemanticCandidate> candidates,
        BoardGeometryComponentSelectorOptions options)
    {
        SemanticCandidate largest =
            candidates
                .OrderByDescending(candidate =>
                    candidate.Component.PixelCount)
                .First();

        long minimumSeedPixels =
            Math.Max(
                options.MinimumAbsoluteSeedPixelCount,
                checked(
                    (long)Math.Ceiling(
                        largest.Component.PixelCount *
                        options.MinimumRelativeSeedPixelCount)));

        var seeds =
            candidates
                .Where(candidate =>
                    candidate.Component.PixelCount >= minimumSeedPixels)
                .Take(options.MaximumSeedComponentCount)
                .ToList();

        if (seeds.Count == 0)
        {
            seeds.Add(largest);
        }

        return seeds;
    }

    /// <summary>
    /// Determina si un componente está suficientemente cerca de la región
    /// seleccionada para formar parte del mismo documento técnico.
    /// </summary>
    private static bool ShouldJoinSelection(
        BoardGeometryComponent candidate,
        BoardGeometryBounds selectedBounds,
        int pageWidth,
        int pageHeight,
        BoardGeometryComponentSelectorOptions options)
    {
        int horizontalGap =
            CalculateAxisGap(
                candidate.Bounds.Left,
                candidate.Bounds.Right,
                selectedBounds.Left,
                selectedBounds.Right);

        int verticalGap =
            CalculateAxisGap(
                candidate.Bounds.Top,
                candidate.Bounds.Bottom,
                selectedBounds.Top,
                selectedBounds.Bottom);

        double normalizedHorizontalGap =
            (double)horizontalGap /
            pageWidth;

        double normalizedVerticalGap =
            (double)verticalGap /
            pageHeight;

        bool overlapsOrClose =
            normalizedHorizontalGap <= options.MaximumHorizontalGap &&
            normalizedVerticalGap <= options.MaximumVerticalGap;

        if (!overlapsOrClose)
        {
            return false;
        }

        BoardGeometryBounds combined =
            UnionBounds(
                selectedBounds,
                candidate.Bounds);

        double combinedCoverage =
            (double)checked(
                (long)combined.Width *
                combined.Height) /
            checked((long)pageWidth * pageHeight);

        return combinedCoverage <=
               options.MaximumCombinedBoundsCoverage;
    }

    /// <summary>
    /// Calcula la separación entre dos intervalos de un mismo eje.
    /// </summary>
    private static int CalculateAxisGap(
        int firstStart,
        int firstEnd,
        int secondStart,
        int secondEnd)
    {
        if (firstEnd < secondStart)
        {
            return secondStart - firstEnd;
        }

        if (secondEnd < firstStart)
        {
            return firstStart - secondEnd;
        }

        return 0;
    }

    /// <summary>
    /// Crea un resultado de respaldo con el componente más grande cuando
    /// todos los componentes fueron descartados por los filtros.
    /// </summary>
    private static BoardGeometryComponentSelectionResult CreateLargestFallback(
        BoardGeometryComponentClassificationResult classification,
        Dictionary<BoardGeometryComponentType, int> acceptedByType,
        Dictionary<BoardGeometryComponentType, int> discardedByType)
    {
        BoardGeometryComponentClassification? largest =
            classification.Classifications
                .OrderByDescending(item =>
                    item.Component.PixelCount)
                .FirstOrDefault();

        if (largest is null)
        {
            return BoardGeometryComponentSelectionResult.Empty(
                classification.PageWidth,
                classification.PageHeight);
        }

        foreach (BoardGeometryComponentClassification item
                 in classification.Classifications)
        {
            if (item.Component.Id == largest.Component.Id)
            {
                acceptedByType[item.Type]++;
            }
            else
            {
                discardedByType[item.Type]++;
            }
        }

        IReadOnlyList<BoardGeometryComponent> selected =
            new ReadOnlyCollection<BoardGeometryComponent>(
                new List<BoardGeometryComponent>
                {
                    largest.Component
                });

        return new BoardGeometryComponentSelectionResult(
            selected,
            largest.Component.Bounds,
            largest.Component.PixelCount,
            classification.PageWidth,
            classification.PageHeight,
            usedFallback: true,
            new ReadOnlyDictionary<BoardGeometryComponentType, int>(
                acceptedByType),
            new ReadOnlyDictionary<BoardGeometryComponentType, int>(
                discardedByType));
    }

    private static Dictionary<BoardGeometryComponentType, int> CreateTypeCounter()
    {
        return Enum
            .GetValues<BoardGeometryComponentType>()
            .ToDictionary(
                type => type,
                _ => 0);
    }

    /// <summary>
    /// Une los límites de una colección de componentes.
    /// </summary>
    private static BoardGeometryBounds UnionBounds(
        IReadOnlyList<BoardGeometryComponent> components)
    {
        if (components.Count == 0)
        {
            throw new ArgumentException(
                "La colección no contiene componentes.",
                nameof(components));
        }

        BoardGeometryBounds bounds =
            components[0].Bounds;

        for (int index = 1;
             index < components.Count;
             index++)
        {
            bounds =
                UnionBounds(
                    bounds,
                    components[index].Bounds);
        }

        return bounds;
    }

    /// <summary>
    /// Une dos rectángulos geométricos.
    /// </summary>
    private static BoardGeometryBounds UnionBounds(
        BoardGeometryBounds first,
        BoardGeometryBounds second)
    {
        int left =
            Math.Min(
                first.Left,
                second.Left);

        int top =
            Math.Min(
                first.Top,
                second.Top);

        int right =
            Math.Max(
                first.Right,
                second.Right);

        int bottom =
            Math.Max(
                first.Bottom,
                second.Bottom);

        return new BoardGeometryBounds(
            left,
            top,
            checked(right - left),
            checked(bottom - top));
    }

    /// <summary>
    /// Agrega un margen de seguridad sin exceder la página.
    /// </summary>
    private static BoardGeometryBounds ApplyPadding(
        BoardGeometryBounds bounds,
        int pageWidth,
        int pageHeight,
        int padding)
    {
        if (padding == 0)
        {
            return bounds;
        }

        int left =
            Math.Max(
                0,
                bounds.Left - padding);

        int top =
            Math.Max(
                0,
                bounds.Top - padding);

        int right =
            Math.Min(
                pageWidth,
                bounds.Right + padding);

        int bottom =
            Math.Min(
                pageHeight,
                bounds.Bottom + padding);

        return new BoardGeometryBounds(
            left,
            top,
            checked(right - left),
            checked(bottom - top));
    }

    /// <summary>
    /// Valida las opciones públicas del selector.
    /// </summary>
    private static void ValidateOptions(
        BoardGeometryComponentSelectorOptions options)
    {
        ValidateFraction(
            options.MinimumPageCoverage,
            nameof(options.MinimumPageCoverage));

        ValidateFraction(
            options.MinimumRelativeSeedPixelCount,
            nameof(options.MinimumRelativeSeedPixelCount));

        ValidateFraction(
            options.MaximumHorizontalGap,
            nameof(options.MaximumHorizontalGap));

        ValidateFraction(
            options.MaximumVerticalGap,
            nameof(options.MaximumVerticalGap));

        ValidateFraction(
            options.MaximumCombinedBoundsCoverage,
            nameof(options.MaximumCombinedBoundsCoverage));

        ValidateFraction(
            options.MinimumFrameWidthCoverage,
            nameof(options.MinimumFrameWidthCoverage));

        ValidateFraction(
            options.MinimumFrameHeightCoverage,
            nameof(options.MinimumFrameHeightCoverage));

        ValidateFraction(
            options.MinimumFrameBoundsCoverage,
            nameof(options.MinimumFrameBoundsCoverage));

        ValidateFraction(
            options.MaximumFrameDensity,
            nameof(options.MaximumFrameDensity));

        ValidateFraction(
            options.MinimumUnknownConfidence,
            nameof(options.MinimumUnknownConfidence));

        if (options.MinimumAbsolutePixelCount < 1L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MinimumAbsolutePixelCount));
        }

        if (options.MinimumAbsoluteSeedPixelCount < 1L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MinimumAbsoluteSeedPixelCount));
        }

        if (options.MaximumSeedComponentCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaximumSeedComponentCount));
        }

        if (options.FrameBorderTolerancePixels < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.FrameBorderTolerancePixels));
        }

        if (options.MinimumTouchedEdgesForFrame < 1 ||
            options.MinimumTouchedEdgesForFrame > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MinimumTouchedEdgesForFrame));
        }

        if (options.PaddingPixels < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.PaddingPixels));
        }
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

    private readonly record struct SemanticCandidate(
        BoardGeometryComponent Component,
        BoardGeometryComponentType Type,
        double Confidence);
}

/// <summary>
/// Opciones del selector y agrupador de componentes.
/// </summary>
public sealed record BoardGeometryComponentSelectorOptions
{
    /// <summary>
    /// Cantidad absoluta mínima de píxeles de un componente candidato.
    /// </summary>
    public long MinimumAbsolutePixelCount { get; init; } =
        12L;

    /// <summary>
    /// Cobertura mínima del componente respecto de la página.
    /// </summary>
    public double MinimumPageCoverage { get; init; } =
        0.000002D;

    /// <summary>
    /// Cantidad absoluta mínima de píxeles de un componente semilla.
    /// </summary>
    public long MinimumAbsoluteSeedPixelCount { get; init; } =
        40L;

    /// <summary>
    /// Tamaño mínimo relativo de una semilla respecto del componente mayor.
    /// </summary>
    public double MinimumRelativeSeedPixelCount { get; init; } =
        0.08D;

    /// <summary>
    /// Cantidad máxima de componentes utilizados como semillas iniciales.
    /// </summary>
    public int MaximumSeedComponentCount { get; init; } =
        32;

    /// <summary>
    /// Separación horizontal máxima, expresada como proporción del ancho
    /// total de la página.
    /// </summary>
    public double MaximumHorizontalGap { get; init; } =
        0.08D;

    /// <summary>
    /// Separación vertical máxima, expresada como proporción del alto total
    /// de la página.
    /// </summary>
    public double MaximumVerticalGap { get; init; } =
        0.08D;

    /// <summary>
    /// Cobertura máxima permitida para el rectángulo combinado.
    /// </summary>
    public double MaximumCombinedBoundsCoverage { get; init; } =
        0.94D;

    /// <summary>
    /// Distancia máxima al borde para considerar que un componente toca
    /// dicho borde.
    /// </summary>
    public int FrameBorderTolerancePixels { get; init; } =
        8;

    /// <summary>
    /// Cantidad mínima de bordes tocados para considerar un marco.
    /// </summary>
    public int MinimumTouchedEdgesForFrame { get; init; } =
        3;

    /// <summary>
    /// Cobertura horizontal mínima de un probable marco de página.
    /// </summary>
    public double MinimumFrameWidthCoverage { get; init; } =
        0.90D;

    /// <summary>
    /// Cobertura vertical mínima de un probable marco de página.
    /// </summary>
    public double MinimumFrameHeightCoverage { get; init; } =
        0.90D;

    /// <summary>
    /// Cobertura mínima del rectángulo de un probable marco.
    /// </summary>
    public double MinimumFrameBoundsCoverage { get; init; } =
        0.80D;

    /// <summary>
    /// Densidad máxima de un marco fino respecto de su rectángulo.
    /// </summary>
    public double MaximumFrameDensity { get; init; } =
        0.08D;

    /// <summary>
    /// Excluye componentes clasificados como ruido.
    /// </summary>
    public bool ExcludeNoise { get; init; } =
        true;

    /// <summary>
    /// Excluye agujeros del cálculo de límites. Se conserva configurable
    /// porque algunas placas pueden requerirlos como referencia.
    /// </summary>
    public bool ExcludeHoles { get; init; } =
        true;

    /// <summary>
    /// Excluye marcos o contornos de página clasificados como BoardOutline.
    /// </summary>
    public bool ExcludeBoardOutline { get; init; } =
        true;

    /// <summary>
    /// Confianza mínima requerida para aceptar una clasificación Unknown.
    /// </summary>
    public double MinimumUnknownConfidence { get; init; } =
        0.55D;

    /// <summary>
    /// Margen adicional aplicado al rectángulo seleccionado.
    /// </summary>
    public int PaddingPixels { get; init; } =
        8;
}

/// <summary>
/// Resultado inmutable del selector de componentes.
/// </summary>
public sealed class BoardGeometryComponentSelectionResult
{
    /// <summary>
    /// Inicializa un resultado de selección.
    /// </summary>
    public BoardGeometryComponentSelectionResult(
        IReadOnlyList<BoardGeometryComponent> selectedComponents,
        BoardGeometryBounds bounds,
        long selectedPixelCount,
        int pageWidth,
        int pageHeight,
        bool usedFallback,
        IReadOnlyDictionary<BoardGeometryComponentType, int>? acceptedByType = null,
        IReadOnlyDictionary<BoardGeometryComponentType, int>? discardedByType = null)
    {
        ArgumentNullException.ThrowIfNull(selectedComponents);

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

        if (selectedPixelCount < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectedPixelCount));
        }

        SelectedComponents = selectedComponents;
        Bounds = bounds;
        SelectedPixelCount = selectedPixelCount;
        PageWidth = pageWidth;
        PageHeight = pageHeight;
        UsedFallback = usedFallback;

        AcceptedByType =
            acceptedByType ??
            new ReadOnlyDictionary<BoardGeometryComponentType, int>(
                Enum.GetValues<BoardGeometryComponentType>()
                    .ToDictionary(
                        type => type,
                        _ => 0));

        DiscardedByType =
            discardedByType ??
            new ReadOnlyDictionary<BoardGeometryComponentType, int>(
                Enum.GetValues<BoardGeometryComponentType>()
                    .ToDictionary(
                        type => type,
                        _ => 0));
    }

    /// <summary>
    /// Componentes aceptados por el selector.
    /// </summary>
    public IReadOnlyList<BoardGeometryComponent> SelectedComponents { get; }

    /// <summary>
    /// Unión geométrica de todos los componentes aceptados.
    /// </summary>
    public BoardGeometryBounds Bounds { get; }

    /// <summary>
    /// Cantidad total de píxeles de los componentes seleccionados.
    /// </summary>
    public long SelectedPixelCount { get; }

    /// <summary>
    /// Ancho de la página analizada.
    /// </summary>
    public int PageWidth { get; }

    /// <summary>
    /// Alto de la página analizada.
    /// </summary>
    public int PageHeight { get; }

    /// <summary>
    /// Indica si el selector tuvo que recurrir al componente más grande.
    /// </summary>
    public bool UsedFallback { get; }

    /// <summary>
    /// Cantidad de componentes aceptados, agrupada por tipo semántico.
    /// </summary>
    public IReadOnlyDictionary<BoardGeometryComponentType, int> AcceptedByType
    {
        get;
    }

    /// <summary>
    /// Cantidad de componentes descartados, agrupada por tipo semántico.
    /// </summary>
    public IReadOnlyDictionary<BoardGeometryComponentType, int> DiscardedByType
    {
        get;
    }

    /// <summary>
    /// Cantidad total de componentes descartados.
    /// </summary>
    public int DiscardedComponentCount =>
        DiscardedByType.Values.Sum();

    public int GetAcceptedCount(
        BoardGeometryComponentType type)
    {
        return AcceptedByType.TryGetValue(
            type,
            out int count)
                ? count
                : 0;
    }

    public int GetDiscardedCount(
        BoardGeometryComponentType type)
    {
        return DiscardedByType.TryGetValue(
            type,
            out int count)
                ? count
                : 0;
    }

    /// <summary>
    /// Cantidad de componentes seleccionados.
    /// </summary>
    public int SelectedComponentCount =>
        SelectedComponents.Count;

    /// <summary>
    /// Indica si se seleccionó al menos un componente.
    /// </summary>
    public bool HasSelection =>
        SelectedComponents.Count > 0 &&
        Bounds.Width > 0 &&
        Bounds.Height > 0;

    /// <summary>
    /// Cobertura del rectángulo seleccionado respecto de la página.
    /// </summary>
    public double BoundsCoverage =>
        HasSelection
            ? (double)checked(
                (long)Bounds.Width *
                Bounds.Height) /
              checked((long)PageWidth * PageHeight)
            : 0D;

    /// <summary>
    /// Crea un resultado vacío.
    /// </summary>
    public static BoardGeometryComponentSelectionResult Empty(
        int pageWidth,
        int pageHeight)
    {
        return new BoardGeometryComponentSelectionResult(
            Array.Empty<BoardGeometryComponent>(),
            default,
            0L,
            pageWidth,
            pageHeight,
            usedFallback: false);
    }
}
