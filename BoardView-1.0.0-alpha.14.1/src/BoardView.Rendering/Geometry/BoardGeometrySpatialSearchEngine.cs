using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BoardView.Rendering.Geometry;

/// <summary>
/// Motor de búsqueda espacial construido sobre <see cref="BoardGeometryIndex"/>.
/// </summary>
/// <remarks>
/// Proporciona operaciones de alto nivel para:
///
/// <list type="bullet">
/// <item>Hit testing por punto.</item>
/// <item>Selección del mejor componente bajo el cursor.</item>
/// <item>Búsqueda por proximidad.</item>
/// <item>Búsqueda por región.</item>
/// <item>Filtrado por tipo y confianza.</item>
/// </list>
///
/// Esta clase no depende de WPF. Las coordenadas recibidas deben estar
/// expresadas en píxeles del render original usado para construir el índice.
/// </remarks>
public sealed class BoardGeometrySpatialSearchEngine
{
    private readonly BoardGeometryIndex _index;

    /// <summary>
    /// Inicializa el motor con un índice geométrico existente.
    /// </summary>
    public BoardGeometrySpatialSearchEngine(
        BoardGeometryIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);

        _index = index;
    }

    /// <summary>
    /// Índice geométrico utilizado por el motor.
    /// </summary>
    public BoardGeometryIndex Index =>
        _index;

    /// <summary>
    /// Devuelve todos los componentes que contienen el punto indicado.
    /// </summary>
    public BoardGeometrySpatialSearchResult HitTest(
        double x,
        double y,
        BoardGeometrySpatialSearchOptions? options = null)
    {
        BoardGeometrySpatialSearchOptions effectiveOptions =
            options ?? BoardGeometrySpatialSearchOptions.Default;

        BoardGeometryIndexQueryOptions queryOptions =
            CreateQueryOptions(
                effectiveOptions);

        IReadOnlyList<BoardGeometryIndexedComponent> matches =
            _index.QueryPoint(
                x,
                y,
                queryOptions);

        IReadOnlyList<BoardGeometrySpatialMatch> ranked =
            RankPointMatches(
                matches,
                x,
                y,
                effectiveOptions);

        return new BoardGeometrySpatialSearchResult(
            x,
            y,
            ranked,
            effectiveOptions);
    }

    /// <summary>
    /// Devuelve el componente más adecuado bajo el punto indicado.
    /// </summary>
    public bool TryHitTestBest(
        double x,
        double y,
        out BoardGeometryIndexedComponent? component,
        BoardGeometrySpatialSearchOptions? options = null)
    {
        BoardGeometrySpatialSearchResult result =
            HitTest(
                x,
                y,
                options);

        BoardGeometrySpatialMatch? best =
            result.BestMatch;

        component =
            best?.Component;

        return component is not null;
    }

    /// <summary>
    /// Busca componentes cercanos a un punto.
    /// </summary>
    public BoardGeometrySpatialSearchResult FindNearest(
        double x,
        double y,
        double maximumDistancePixels,
        int maximumResults = 10,
        BoardGeometrySpatialSearchOptions? options = null)
    {
        BoardGeometrySpatialSearchOptions effectiveOptions =
            options ?? BoardGeometrySpatialSearchOptions.Default;

        BoardGeometryIndexQueryOptions queryOptions =
            CreateQueryOptions(
                effectiveOptions);

        IReadOnlyList<BoardGeometryIndexedComponent> matches =
            _index.QueryNearest(
                x,
                y,
                maximumDistancePixels,
                maximumResults,
                queryOptions);

        IReadOnlyList<BoardGeometrySpatialMatch> ranked =
            RankNearestMatches(
                matches,
                x,
                y,
                effectiveOptions);

        return new BoardGeometrySpatialSearchResult(
            x,
            y,
            ranked,
            effectiveOptions);
    }

    /// <summary>
    /// Busca componentes que intersectan una región.
    /// </summary>
    public BoardGeometryRegionSearchResult FindInBounds(
        BoardGeometryBounds bounds,
        BoardGeometrySpatialSearchOptions? options = null)
    {
        BoardGeometrySpatialSearchOptions effectiveOptions =
            options ?? BoardGeometrySpatialSearchOptions.Default;

        BoardGeometryIndexQueryOptions queryOptions =
            CreateQueryOptions(
                effectiveOptions);

        IReadOnlyList<BoardGeometryIndexedComponent> matches =
            _index.QueryBounds(
                bounds,
                queryOptions);

        IReadOnlyList<BoardGeometryRegionMatch> ranked =
            matches
                .Select(component =>
                    CreateRegionMatch(
                        component,
                        bounds,
                        effectiveOptions))
                .Where(match =>
                    match.Score >=
                    effectiveOptions.MinimumScore)
                .OrderByDescending(match =>
                    match.Score)
                .ThenByDescending(match =>
                    match.IntersectionRatio)
                .ThenByDescending(match =>
                    match.Component.Confidence)
                .ToArray();

        return new BoardGeometryRegionSearchResult(
            bounds,
            ranked,
            effectiveOptions);
    }

    /// <summary>
    /// Obtiene todos los componentes de un tipo, aplicando confianza mínima.
    /// </summary>
    public IReadOnlyList<BoardGeometryIndexedComponent> GetByType(
        BoardGeometryComponentType type,
        double minimumConfidence = 0D)
    {
        ValidateConfidence(
            minimumConfidence);

        return _index
            .GetByType(type)
            .Where(component =>
                component.Confidence >= minimumConfidence)
            .OrderByDescending(component =>
                component.Confidence)
            .ThenBy(component =>
                component.Id)
            .ToArray();
    }

    /// <summary>
    /// Obtiene un componente por identificador.
    /// </summary>
    public bool TryGetById(
        int id,
        out BoardGeometryIndexedComponent? component)
    {
        return _index.TryGetById(
            id,
            out component);
    }

    /// <summary>
    /// Convierte las opciones de alto nivel en opciones del índice.
    /// </summary>
    private static BoardGeometryIndexQueryOptions CreateQueryOptions(
        BoardGeometrySpatialSearchOptions options)
    {
        ValidateOptions(options);

        return new BoardGeometryIndexQueryOptions
        {
            MinimumConfidence =
                options.MinimumConfidence,

            AllowedTypes =
                options.AllowedTypes,

            ExcludedTypes =
                options.ExcludedTypes
        };
    }

    /// <summary>
    /// Ordena coincidencias que contienen directamente el punto.
    /// </summary>
    private static IReadOnlyList<BoardGeometrySpatialMatch> RankPointMatches(
        IReadOnlyList<BoardGeometryIndexedComponent> components,
        double x,
        double y,
        BoardGeometrySpatialSearchOptions options)
    {
        return components
            .Select(component =>
                CreatePointMatch(
                    component,
                    x,
                    y,
                    options,
                    isDirectHit: true))
            .Where(match =>
                match.Score >=
                options.MinimumScore)
            .OrderByDescending(match =>
                match.Score)
            .ThenBy(match =>
                match.BoundsArea)
            .ThenByDescending(match =>
                match.Component.Confidence)
            .ToArray();
    }

    /// <summary>
    /// Ordena coincidencias obtenidas por proximidad.
    /// </summary>
    private static IReadOnlyList<BoardGeometrySpatialMatch> RankNearestMatches(
        IReadOnlyList<BoardGeometryIndexedComponent> components,
        double x,
        double y,
        BoardGeometrySpatialSearchOptions options)
    {
        return components
            .Select(component =>
                CreatePointMatch(
                    component,
                    x,
                    y,
                    options,
                    isDirectHit: false))
            .Where(match =>
                match.Score >=
                options.MinimumScore)
            .OrderByDescending(match =>
                match.Score)
            .ThenBy(match =>
                match.DistancePixels)
            .ThenByDescending(match =>
                match.Component.Confidence)
            .ToArray();
    }

    /// <summary>
    /// Calcula la puntuación de una coincidencia puntual.
    /// </summary>
    private static BoardGeometrySpatialMatch CreatePointMatch(
        BoardGeometryIndexedComponent component,
        double x,
        double y,
        BoardGeometrySpatialSearchOptions options,
        bool isDirectHit)
    {
        double distance =
            DistanceToBounds(
                x,
                y,
                component.Bounds);

        long boundsArea =
            checked(
                (long)component.Bounds.Width *
                component.Bounds.Height);

        double confidenceScore =
            component.Confidence;

        double typeScore =
            GetTypePriority(
                component.Type);

        double compactnessScore =
            boundsArea <= 0L
                ? 0D
                : 1D /
                  (1D + Math.Log10(boundsArea + 1D));

        double distanceScore =
            isDirectHit
                ? 1D
                : 1D /
                  (1D + distance);

        double directHitBonus =
            isDirectHit
                ? options.DirectHitBonus
                : 0D;

        double score =
            (confidenceScore *
             options.ConfidenceWeight) +
            (typeScore *
             options.TypeWeight) +
            (compactnessScore *
             options.CompactnessWeight) +
            (distanceScore *
             options.DistanceWeight) +
            directHitBonus;

        return new BoardGeometrySpatialMatch(
            component,
            Clamp01(score),
            distance,
            boundsArea,
            isDirectHit);
    }

    /// <summary>
    /// Calcula la puntuación de una coincidencia regional.
    /// </summary>
    private static BoardGeometryRegionMatch CreateRegionMatch(
        BoardGeometryIndexedComponent component,
        BoardGeometryBounds queryBounds,
        BoardGeometrySpatialSearchOptions options)
    {
        long intersectionArea =
            CalculateIntersectionArea(
                component.Bounds,
                queryBounds);

        long componentArea =
            checked(
                (long)component.Bounds.Width *
                component.Bounds.Height);

        double intersectionRatio =
            componentArea <= 0L
                ? 0D
                : (double)intersectionArea /
                  componentArea;

        double score =
            (component.Confidence *
             options.ConfidenceWeight) +
            (GetTypePriority(component.Type) *
             options.TypeWeight) +
            (intersectionRatio *
             options.RegionIntersectionWeight);

        return new BoardGeometryRegionMatch(
            component,
            Clamp01(score),
            intersectionArea,
            intersectionRatio);
    }

    /// <summary>
    /// Prioridad semántica usada para resolver componentes superpuestos.
    /// </summary>
    private static double GetTypePriority(
        BoardGeometryComponentType type)
    {
        return type switch
        {
            BoardGeometryComponentType.Pad => 1.00D,
            BoardGeometryComponentType.ComponentBody => 0.95D,
            BoardGeometryComponentType.Hole => 0.90D,
            BoardGeometryComponentType.Copper => 0.85D,
            BoardGeometryComponentType.Text => 0.80D,
            BoardGeometryComponentType.Silkscreen => 0.70D,
            BoardGeometryComponentType.BoardOutline => 0.45D,
            BoardGeometryComponentType.Unknown => 0.35D,
            BoardGeometryComponentType.Noise => 0.05D,
            _ => 0D
        };
    }

    /// <summary>
    /// Calcula la distancia mínima entre un punto y un rectángulo.
    /// </summary>
    private static double DistanceToBounds(
        double x,
        double y,
        BoardGeometryBounds bounds)
    {
        double horizontalDistance =
            x < bounds.Left
                ? bounds.Left - x
                : x >= bounds.Right
                    ? x - bounds.Right
                    : 0D;

        double verticalDistance =
            y < bounds.Top
                ? bounds.Top - y
                : y >= bounds.Bottom
                    ? y - bounds.Bottom
                    : 0D;

        return Math.Sqrt(
            (horizontalDistance * horizontalDistance) +
            (verticalDistance * verticalDistance));
    }

    /// <summary>
    /// Calcula el área de intersección de dos rectángulos.
    /// </summary>
    private static long CalculateIntersectionArea(
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
            return 0L;
        }

        return checked(
            (long)(right - left) *
            (bottom - top));
    }

    /// <summary>
    /// Valida las opciones públicas.
    /// </summary>
    private static void ValidateOptions(
        BoardGeometrySpatialSearchOptions options)
    {
        ValidateConfidence(
            options.MinimumConfidence);

        ValidateFraction(
            options.MinimumScore,
            nameof(options.MinimumScore));

        ValidateFraction(
            options.ConfidenceWeight,
            nameof(options.ConfidenceWeight));

        ValidateFraction(
            options.TypeWeight,
            nameof(options.TypeWeight));

        ValidateFraction(
            options.CompactnessWeight,
            nameof(options.CompactnessWeight));

        ValidateFraction(
            options.DistanceWeight,
            nameof(options.DistanceWeight));

        ValidateFraction(
            options.RegionIntersectionWeight,
            nameof(options.RegionIntersectionWeight));

        ValidateFraction(
            options.DirectHitBonus,
            nameof(options.DirectHitBonus));

        double pointWeightSum =
            options.ConfidenceWeight +
            options.TypeWeight +
            options.CompactnessWeight +
            options.DistanceWeight +
            options.DirectHitBonus;

        if (pointWeightSum <= 0D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "La suma de pesos debe ser mayor que cero.");
        }
    }

    private static void ValidateConfidence(
        double value)
    {
        ValidateFraction(
            value,
            nameof(value));
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

    private static double Clamp01(
        double value)
    {
        return Math.Max(
            0D,
            Math.Min(
                1D,
                value));
    }
}

/// <summary>
/// Opciones de búsqueda y ranking espacial.
/// </summary>
public sealed record BoardGeometrySpatialSearchOptions
{
    /// <summary>
    /// Configuración predeterminada.
    /// </summary>
    public static BoardGeometrySpatialSearchOptions Default { get; } =
        new();

    /// <summary>
    /// Confianza mínima admitida.
    /// </summary>
    public double MinimumConfidence { get; init; } =
        0D;

    /// <summary>
    /// Puntuación mínima para incluir una coincidencia.
    /// </summary>
    public double MinimumScore { get; init; } =
        0D;

    /// <summary>
    /// Tipos permitidos. Null permite todos los tipos.
    /// </summary>
    public IReadOnlySet<BoardGeometryComponentType>? AllowedTypes
    {
        get;
        init;
    }

    /// <summary>
    /// Tipos excluidos de la búsqueda.
    /// </summary>
    public IReadOnlySet<BoardGeometryComponentType> ExcludedTypes
    {
        get;
        init;
    } = new HashSet<BoardGeometryComponentType>
    {
        BoardGeometryComponentType.Noise
    };

    /// <summary>
    /// Peso de la confianza del clasificador.
    /// </summary>
    public double ConfidenceWeight { get; init; } =
        0.35D;

    /// <summary>
    /// Peso de la prioridad semántica.
    /// </summary>
    public double TypeWeight { get; init; } =
        0.25D;

    /// <summary>
    /// Peso que favorece regiones compactas.
    /// </summary>
    public double CompactnessWeight { get; init; } =
        0.15D;

    /// <summary>
    /// Peso de la cercanía al punto consultado.
    /// </summary>
    public double DistanceWeight { get; init; } =
        0.15D;

    /// <summary>
    /// Peso de la proporción de intersección en búsquedas regionales.
    /// </summary>
    public double RegionIntersectionWeight { get; init; } =
        0.40D;

    /// <summary>
    /// Bonificación aplicada cuando el punto está dentro del componente.
    /// </summary>
    public double DirectHitBonus { get; init; } =
        0.10D;
}

/// <summary>
/// Coincidencia espacial asociada a un punto.
/// </summary>
public sealed record BoardGeometrySpatialMatch(
    BoardGeometryIndexedComponent Component,
    double Score,
    double DistancePixels,
    long BoundsArea,
    bool IsDirectHit);

/// <summary>
/// Resultado de una búsqueda puntual o por proximidad.
/// </summary>
public sealed class BoardGeometrySpatialSearchResult
{
    public BoardGeometrySpatialSearchResult(
        double queryX,
        double queryY,
        IReadOnlyList<BoardGeometrySpatialMatch> matches,
        BoardGeometrySpatialSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(options);

        QueryX = queryX;
        QueryY = queryY;
        Matches = matches;
        Options = options;
    }

    public double QueryX { get; }

    public double QueryY { get; }

    public IReadOnlyList<BoardGeometrySpatialMatch> Matches { get; }

    public BoardGeometrySpatialSearchOptions Options { get; }

    public int MatchCount =>
        Matches.Count;

    public bool HasMatches =>
        Matches.Count > 0;

    public BoardGeometrySpatialMatch? BestMatch =>
        Matches.Count > 0
            ? Matches[0]
            : null;
}

/// <summary>
/// Coincidencia espacial asociada a una región.
/// </summary>
public sealed record BoardGeometryRegionMatch(
    BoardGeometryIndexedComponent Component,
    double Score,
    long IntersectionArea,
    double IntersectionRatio);

/// <summary>
/// Resultado de una consulta regional.
/// </summary>
public sealed class BoardGeometryRegionSearchResult
{
    public BoardGeometryRegionSearchResult(
        BoardGeometryBounds queryBounds,
        IReadOnlyList<BoardGeometryRegionMatch> matches,
        BoardGeometrySpatialSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(options);

        QueryBounds = queryBounds;
        Matches = matches;
        Options = options;
    }

    public BoardGeometryBounds QueryBounds { get; }

    public IReadOnlyList<BoardGeometryRegionMatch> Matches { get; }

    public BoardGeometrySpatialSearchOptions Options { get; }

    public int MatchCount =>
        Matches.Count;

    public bool HasMatches =>
        Matches.Count > 0;
}
