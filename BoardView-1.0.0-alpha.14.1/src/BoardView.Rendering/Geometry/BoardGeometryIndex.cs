using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BoardView.Rendering.Geometry;

/// <summary>
/// Índice geométrico inmutable construido a partir de la clasificación completa
/// producida por <see cref="BoardGeometryComponentClassifier"/>.
/// </summary>
/// <remarks>
/// Este índice permite consultar componentes por:
///
/// <list type="bullet">
/// <item>Identificador.</item>
/// <item>Tipo semántico.</item>
/// <item>Punto contenido.</item>
/// <item>Intersección con una región.</item>
/// <item>Proximidad espacial.</item>
/// </list>
///
/// La primera versión utiliza una cuadrícula espacial uniforme. Esto evita
/// recorrer los 2.203 componentes completos en cada consulta y mantiene la
/// implementación independiente de WPF.
/// </remarks>
public sealed class BoardGeometryIndex
{
    private readonly IReadOnlyDictionary<int, BoardGeometryIndexedComponent> _byId;
    private readonly IReadOnlyDictionary<BoardGeometryComponentType, IReadOnlyList<BoardGeometryIndexedComponent>> _byType;
    private readonly IReadOnlyDictionary<BoardGeometryGridCell, IReadOnlyList<BoardGeometryIndexedComponent>> _spatialCells;

    /// <summary>
    /// Construye un índice usando la configuración predeterminada.
    /// </summary>
    public BoardGeometryIndex(
        BoardGeometryComponentClassificationResult classification)
        : this(
            classification,
            BoardGeometryIndexOptions.Default)
    {
    }

    /// <summary>
    /// Construye un índice geométrico inmutable.
    /// </summary>
    public BoardGeometryIndex(
        BoardGeometryComponentClassificationResult classification,
        BoardGeometryIndexOptions options)
    {
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(options);

        ValidateOptions(options);

        PageWidth = classification.PageWidth;
        PageHeight = classification.PageHeight;
        Options = options;

        var indexedComponents =
            new List<BoardGeometryIndexedComponent>(
                classification.ClassificationCount);

        foreach (BoardGeometryComponentClassification item
                 in classification.Classifications)
        {
            BoardGeometryBounds bounds =
                item.Component.Bounds;

            var indexed =
                new BoardGeometryIndexedComponent(
                    item.Component.Id,
                    item.Type,
                    item.Confidence,
                    bounds,
                    item.Component.PixelCount,
                    item.Component.Density,
                    bounds.Left + (bounds.Width / 2D),
                    bounds.Top + (bounds.Height / 2D),
                    item);

            indexedComponents.Add(indexed);
        }

        Components =
            new ReadOnlyCollection<BoardGeometryIndexedComponent>(
                indexedComponents
                    .OrderBy(component => component.Id)
                    .ToList());

        _byId =
            new ReadOnlyDictionary<int, BoardGeometryIndexedComponent>(
                indexedComponents.ToDictionary(
                    component => component.Id));

        _byType =
            BuildTypeIndex(indexedComponents);

        _spatialCells =
            BuildSpatialIndex(
                indexedComponents,
                options.CellSizePixels);

        Statistics =
            BoardGeometryIndexStatistics.Create(
                Components,
                PageWidth,
                PageHeight,
                _spatialCells.Count);
    }

    /// <summary>
    /// Componentes indexados.
    /// </summary>
    public IReadOnlyList<BoardGeometryIndexedComponent> Components { get; }

    /// <summary>
    /// Ancho de la página utilizada para construir el índice.
    /// </summary>
    public int PageWidth { get; }

    /// <summary>
    /// Alto de la página utilizada para construir el índice.
    /// </summary>
    public int PageHeight { get; }

    /// <summary>
    /// Opciones utilizadas para construir el índice.
    /// </summary>
    public BoardGeometryIndexOptions Options { get; }

    /// <summary>
    /// Estadísticas globales del índice.
    /// </summary>
    public BoardGeometryIndexStatistics Statistics { get; }

    /// <summary>
    /// Cantidad total de componentes indexados.
    /// </summary>
    public int Count =>
        Components.Count;

    /// <summary>
    /// Busca un componente por identificador.
    /// </summary>
    public bool TryGetById(
        int id,
        out BoardGeometryIndexedComponent? component)
    {
        bool found =
            _byId.TryGetValue(
                id,
                out BoardGeometryIndexedComponent? value);

        component = value;
        return found;
    }

    /// <summary>
    /// Obtiene todos los componentes de un tipo determinado.
    /// </summary>
    public IReadOnlyList<BoardGeometryIndexedComponent> GetByType(
        BoardGeometryComponentType type)
    {
        return _byType.TryGetValue(
            type,
            out IReadOnlyList<BoardGeometryIndexedComponent>? components)
                ? components
                : Array.Empty<BoardGeometryIndexedComponent>();
    }

    /// <summary>
    /// Obtiene los componentes cuyo rectángulo contiene el punto indicado.
    /// </summary>
    public IReadOnlyList<BoardGeometryIndexedComponent> QueryPoint(
        double x,
        double y,
        BoardGeometryIndexQueryOptions? options = null)
    {
        ValidatePoint(
            x,
            y);

        BoardGeometryIndexQueryOptions effectiveOptions =
            options ?? BoardGeometryIndexQueryOptions.Default;

        BoardGeometryGridCell cell =
            GetCell(
                x,
                y,
                Options.CellSizePixels);

        if (!_spatialCells.TryGetValue(
                cell,
                out IReadOnlyList<BoardGeometryIndexedComponent>? candidates))
        {
            return Array.Empty<BoardGeometryIndexedComponent>();
        }

        return candidates
            .Where(component =>
                IsAccepted(
                    component,
                    effectiveOptions) &&
                Contains(
                    component.Bounds,
                    x,
                    y))
            .OrderBy(component =>
                checked(
                    (long)component.Bounds.Width *
                    component.Bounds.Height))
            .ThenByDescending(component =>
                component.Confidence)
            .ToArray();
    }

    /// <summary>
    /// Obtiene los componentes que intersectan una región.
    /// </summary>
    public IReadOnlyList<BoardGeometryIndexedComponent> QueryBounds(
        BoardGeometryBounds bounds,
        BoardGeometryIndexQueryOptions? options = null)
    {
        ValidateBounds(bounds);

        BoardGeometryIndexQueryOptions effectiveOptions =
            options ?? BoardGeometryIndexQueryOptions.Default;

        HashSet<int> visitedIds =
            new();

        var result =
            new List<BoardGeometryIndexedComponent>();

        foreach (BoardGeometryGridCell cell
                 in EnumerateCells(
                     bounds,
                     Options.CellSizePixels))
        {
            if (!_spatialCells.TryGetValue(
                    cell,
                    out IReadOnlyList<BoardGeometryIndexedComponent>? candidates))
            {
                continue;
            }

            foreach (BoardGeometryIndexedComponent component in candidates)
            {
                if (!visitedIds.Add(component.Id))
                {
                    continue;
                }

                if (!IsAccepted(
                        component,
                        effectiveOptions))
                {
                    continue;
                }

                if (Intersects(
                        component.Bounds,
                        bounds))
                {
                    result.Add(component);
                }
            }
        }

        return result
            .OrderByDescending(component =>
                IntersectionArea(
                    component.Bounds,
                    bounds))
            .ThenByDescending(component =>
                component.Confidence)
            .ToArray();
    }

    /// <summary>
    /// Busca los componentes más próximos a un punto.
    /// </summary>
    public IReadOnlyList<BoardGeometryIndexedComponent> QueryNearest(
        double x,
        double y,
        double maximumDistancePixels,
        int maximumResults = 10,
        BoardGeometryIndexQueryOptions? options = null)
    {
        ValidatePoint(
            x,
            y);

        if (!double.IsFinite(maximumDistancePixels) ||
            maximumDistancePixels < 0D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDistancePixels));
        }

        if (maximumResults <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumResults));
        }

        BoardGeometryIndexQueryOptions effectiveOptions =
            options ?? BoardGeometryIndexQueryOptions.Default;

        int left =
            Math.Max(
                0,
                (int)Math.Floor(x - maximumDistancePixels));

        int top =
            Math.Max(
                0,
                (int)Math.Floor(y - maximumDistancePixels));

        int right =
            Math.Min(
                PageWidth,
                (int)Math.Ceiling(x + maximumDistancePixels));

        int bottom =
            Math.Min(
                PageHeight,
                (int)Math.Ceiling(y + maximumDistancePixels));

        var searchBounds =
            new BoardGeometryBounds(
                left,
                top,
                Math.Max(1, right - left),
                Math.Max(1, bottom - top));

        return QueryBounds(
                searchBounds,
                effectiveOptions)
            .Select(component =>
                new
                {
                    Component = component,
                    Distance = DistanceToBounds(
                        x,
                        y,
                        component.Bounds)
                })
            .Where(item =>
                item.Distance <= maximumDistancePixels)
            .OrderBy(item =>
                item.Distance)
            .ThenByDescending(item =>
                item.Component.Confidence)
            .Take(maximumResults)
            .Select(item =>
                item.Component)
            .ToArray();
    }

    /// <summary>
    /// Crea el índice agrupado por tipo.
    /// </summary>
    private static IReadOnlyDictionary<BoardGeometryComponentType, IReadOnlyList<BoardGeometryIndexedComponent>> BuildTypeIndex(
        IReadOnlyList<BoardGeometryIndexedComponent> components)
    {
        var result =
            new Dictionary<BoardGeometryComponentType, IReadOnlyList<BoardGeometryIndexedComponent>>();

        foreach (BoardGeometryComponentType type
                 in Enum.GetValues<BoardGeometryComponentType>())
        {
            result[type] =
                new ReadOnlyCollection<BoardGeometryIndexedComponent>(
                    components
                        .Where(component => component.Type == type)
                        .OrderByDescending(component => component.Confidence)
                        .ThenBy(component => component.Id)
                        .ToList());
        }

        return new ReadOnlyDictionary<BoardGeometryComponentType, IReadOnlyList<BoardGeometryIndexedComponent>>(
            result);
    }

    /// <summary>
    /// Construye la cuadrícula espacial.
    /// </summary>
    private static IReadOnlyDictionary<BoardGeometryGridCell, IReadOnlyList<BoardGeometryIndexedComponent>> BuildSpatialIndex(
        IReadOnlyList<BoardGeometryIndexedComponent> components,
        int cellSizePixels)
    {
        var mutable =
            new Dictionary<BoardGeometryGridCell, List<BoardGeometryIndexedComponent>>();

        foreach (BoardGeometryIndexedComponent component in components)
        {
            foreach (BoardGeometryGridCell cell
                     in EnumerateCells(
                         component.Bounds,
                         cellSizePixels))
            {
                if (!mutable.TryGetValue(
                        cell,
                        out List<BoardGeometryIndexedComponent>? cellComponents))
                {
                    cellComponents =
                        new List<BoardGeometryIndexedComponent>();

                    mutable.Add(
                        cell,
                        cellComponents);
                }

                cellComponents.Add(component);
            }
        }

        var immutable =
            mutable.ToDictionary(
                pair => pair.Key,
                pair =>
                    (IReadOnlyList<BoardGeometryIndexedComponent>)
                    new ReadOnlyCollection<BoardGeometryIndexedComponent>(
                        pair.Value
                            .OrderBy(component => component.Id)
                            .ToList()));

        return new ReadOnlyDictionary<BoardGeometryGridCell, IReadOnlyList<BoardGeometryIndexedComponent>>(
            immutable);
    }

    private static IEnumerable<BoardGeometryGridCell> EnumerateCells(
        BoardGeometryBounds bounds,
        int cellSizePixels)
    {
        int firstColumn =
            bounds.Left /
            cellSizePixels;

        int lastColumn =
            Math.Max(
                bounds.Left,
                bounds.Right - 1) /
            cellSizePixels;

        int firstRow =
            bounds.Top /
            cellSizePixels;

        int lastRow =
            Math.Max(
                bounds.Top,
                bounds.Bottom - 1) /
            cellSizePixels;

        for (int row = firstRow;
             row <= lastRow;
             row++)
        {
            for (int column = firstColumn;
                 column <= lastColumn;
                 column++)
            {
                yield return new BoardGeometryGridCell(
                    column,
                    row);
            }
        }
    }

    private static BoardGeometryGridCell GetCell(
        double x,
        double y,
        int cellSizePixels)
    {
        return new BoardGeometryGridCell(
            (int)Math.Floor(x / cellSizePixels),
            (int)Math.Floor(y / cellSizePixels));
    }

    private static bool IsAccepted(
        BoardGeometryIndexedComponent component,
        BoardGeometryIndexQueryOptions options)
    {
        if (component.Confidence <
            options.MinimumConfidence)
        {
            return false;
        }

        if (options.AllowedTypes is not null &&
            !options.AllowedTypes.Contains(component.Type))
        {
            return false;
        }

        if (options.ExcludedTypes.Contains(component.Type))
        {
            return false;
        }

        return true;
    }

    private static bool Contains(
        BoardGeometryBounds bounds,
        double x,
        double y)
    {
        return x >= bounds.Left &&
               x < bounds.Right &&
               y >= bounds.Top &&
               y < bounds.Bottom;
    }

    private static bool Intersects(
        BoardGeometryBounds first,
        BoardGeometryBounds second)
    {
        return first.Left < second.Right &&
               first.Right > second.Left &&
               first.Top < second.Bottom &&
               first.Bottom > second.Top;
    }

    private static long IntersectionArea(
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

    private static double DistanceToBounds(
        double x,
        double y,
        BoardGeometryBounds bounds)
    {
        double horizontalDistance =
            x < bounds.Left
                ? bounds.Left - x
                : x > bounds.Right
                    ? x - bounds.Right
                    : 0D;

        double verticalDistance =
            y < bounds.Top
                ? bounds.Top - y
                : y > bounds.Bottom
                    ? y - bounds.Bottom
                    : 0D;

        return Math.Sqrt(
            (horizontalDistance * horizontalDistance) +
            (verticalDistance * verticalDistance));
    }

    private void ValidatePoint(
        double x,
        double y)
    {
        if (!double.IsFinite(x) ||
            !double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                "Las coordenadas deben ser finitas.");
        }

        if (x < 0D ||
            y < 0D ||
            x > PageWidth ||
            y > PageHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"El punto debe estar dentro de la página {PageWidth} × {PageHeight}.");
        }
    }

    private void ValidateBounds(
        BoardGeometryBounds bounds)
    {
        if (bounds.Width <= 0 ||
            bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "La región debe tener dimensiones positivas.");
        }

        if (bounds.Left < 0 ||
            bounds.Top < 0 ||
            bounds.Right > PageWidth ||
            bounds.Bottom > PageHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "La región excede las dimensiones de la página.");
        }
    }

    private static void ValidateOptions(
        BoardGeometryIndexOptions options)
    {
        if (options.CellSizePixels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.CellSizePixels));
        }
    }
}

/// <summary>
/// Componente preparado para búsquedas espaciales y semánticas.
/// </summary>
public sealed record BoardGeometryIndexedComponent(
    int Id,
    BoardGeometryComponentType Type,
    double Confidence,
    BoardGeometryBounds Bounds,
    long PixelCount,
    double Density,
    double CenterX,
    double CenterY,
    BoardGeometryComponentClassification Classification);

/// <summary>
/// Coordenada de una celda de la cuadrícula espacial.
/// </summary>
public readonly record struct BoardGeometryGridCell(
    int Column,
    int Row);

/// <summary>
/// Configuración de construcción del índice.
/// </summary>
public sealed record BoardGeometryIndexOptions
{
    /// <summary>
    /// Configuración predeterminada.
    /// </summary>
    public static BoardGeometryIndexOptions Default { get; } =
        new();

    /// <summary>
    /// Tamaño de cada celda espacial en píxeles.
    /// </summary>
    public int CellSizePixels { get; init; } =
        128;
}

/// <summary>
/// Filtros aplicados a una consulta del índice.
/// </summary>
public sealed record BoardGeometryIndexQueryOptions
{
    /// <summary>
    /// Configuración predeterminada.
    /// </summary>
    public static BoardGeometryIndexQueryOptions Default { get; } =
        new();

    /// <summary>
    /// Confianza mínima aceptada.
    /// </summary>
    public double MinimumConfidence { get; init; } =
        0D;

    /// <summary>
    /// Tipos permitidos. Cuando es null, se permiten todos.
    /// </summary>
    public IReadOnlySet<BoardGeometryComponentType>? AllowedTypes
    {
        get;
        init;
    }

    /// <summary>
    /// Tipos que deben excluirse.
    /// </summary>
    public IReadOnlySet<BoardGeometryComponentType> ExcludedTypes
    {
        get;
        init;
    } = new HashSet<BoardGeometryComponentType>();
}

/// <summary>
/// Estadísticas agregadas del índice geométrico.
/// </summary>
public sealed class BoardGeometryIndexStatistics
{
    private BoardGeometryIndexStatistics(
        int componentCount,
        int pageWidth,
        int pageHeight,
        int occupiedCellCount,
        IReadOnlyDictionary<BoardGeometryComponentType, int> componentsByType,
        double averageConfidence,
        long totalPixelCount)
    {
        ComponentCount = componentCount;
        PageWidth = pageWidth;
        PageHeight = pageHeight;
        OccupiedCellCount = occupiedCellCount;
        ComponentsByType = componentsByType;
        AverageConfidence = averageConfidence;
        TotalPixelCount = totalPixelCount;
    }

    public int ComponentCount { get; }

    public int PageWidth { get; }

    public int PageHeight { get; }

    public int OccupiedCellCount { get; }

    public IReadOnlyDictionary<BoardGeometryComponentType, int> ComponentsByType
    {
        get;
    }

    public double AverageConfidence { get; }

    public long TotalPixelCount { get; }

    internal static BoardGeometryIndexStatistics Create(
        IReadOnlyList<BoardGeometryIndexedComponent> components,
        int pageWidth,
        int pageHeight,
        int occupiedCellCount)
    {
        var counts =
            Enum.GetValues<BoardGeometryComponentType>()
                .ToDictionary(
                    type => type,
                    type => components.Count(component => component.Type == type));

        double averageConfidence =
            components.Count == 0
                ? 0D
                : components.Average(component => component.Confidence);

        long totalPixelCount =
            components.Sum(component => component.PixelCount);

        return new BoardGeometryIndexStatistics(
            components.Count,
            pageWidth,
            pageHeight,
            occupiedCellCount,
            new ReadOnlyDictionary<BoardGeometryComponentType, int>(
                counts),
            averageConfidence,
            totalPixelCount);
    }
}
