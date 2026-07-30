using BoardView.Core.Documents.Common;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;
using BoardView.Core.Model;
using BoardView.Core.Spatial;
using BoardView.Core.Validation;

namespace BoardView.Core.Documents;

/// <summary>
/// Normalized internal board model. Every format reader converts source data to this
/// structure and all rendering, search and analysis modules consume the same model.
/// </summary>
public sealed class BoardDocument
{
    private readonly List<BoardLayer> layers = [];
    private readonly List<BoardNet> nets = [];
    private readonly List<BoardComponent> components = [];
    private readonly List<BoardElement> elements = [];
    private readonly List<BoardDocumentPage> pages = [];
    private readonly Dictionary<string, BoardLayer> layersById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BoardNet> netsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BoardComponent> componentsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BoardElement> elementsById = new(StringComparer.Ordinal);
    private SpatialIndex<BoardElement>? spatialIndex;

    /// <summary>Initializes an empty normalized board document.</summary>
    public BoardDocument(string name, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        Name = name.Trim();
        SourcePath = sourcePath;
        CoordinateSpace = CoordinateSpace.CreateMillimeterWorld();
    }

    public string Name { get; }
    public string SourcePath { get; }
    public MeasurementUnit NormalizedUnit { get; } = MeasurementUnit.Millimeter;
    public CoordinateSpace CoordinateSpace { get; private set; }
    public DocumentMetadata Metadata { get; } = new();
    public PropertyBag Properties { get; } = new();
    public IReadOnlyList<BoardLayer> Layers => layers;
    public IReadOnlyList<BoardNet> Nets => nets;
    public IReadOnlyList<BoardComponent> Components => components;
    public IReadOnlyList<BoardElement> Elements => elements;
    public IReadOnlyList<BoardDocumentPage> Pages => pages;

    /// <summary>Gets the document spatial index, building it only on first access.</summary>
    public ISpatialIndex<BoardElement> SpatialIndex => GetSpatialIndex();

    /// <summary>Gets global limits calculated from every board element.</summary>
    public Bounds2D Bounds => elements.Count == 0
        ? Bounds2D.Empty
        : elements.Select(static element => element.Bounds).Aggregate(static (left, right) => left.Union(right));

    /// <summary>Defines the normalized coordinate space used by the document.</summary>
    public void SetCoordinateSpace(CoordinateSpace coordinateSpace)
    {
        CoordinateSpace = coordinateSpace ?? throw new ArgumentNullException(nameof(coordinateSpace));
    }

    /// <summary>Adds one source page or surface.</summary>
    public void AddPage(BoardDocumentPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (pages.Any(item => item.Number == page.Number))
        {
            throw new InvalidOperationException($"Ya existe la página número {page.Number}.");
        }

        foreach (string layerId in page.LayerIds)
        {
            if (!layersById.ContainsKey(layerId))
            {
                throw new InvalidOperationException(
                    $"La página {page.Number} referencia la capa inexistente '{layerId}'.");
            }
        }

        pages.Add(page);
        pages.Sort(static (left, right) => left.Number.CompareTo(right.Number));
    }

    /// <summary>Adds one layer with a unique identifier.</summary>
    public void AddLayer(BoardLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        AddUnique(layer.Id, layer, layersById, layers, "capa");
    }

    /// <summary>Adds one electrical net with a unique identifier.</summary>
    public void AddNet(BoardNet net)
    {
        ArgumentNullException.ThrowIfNull(net);
        AddUnique(net.Id, net, netsById, nets, "red");
    }

    /// <summary>Adds one component with a unique identifier.</summary>
    public void AddComponent(BoardComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        AddUnique(component.Id, component, componentsById, components, "componente");
    }

    /// <summary>
    /// Adds one board element and incrementally updates the spatial index when it has already
    /// been initialized. No full index rebuild is required for normal loading operations.
    /// </summary>
    public void AddElement(BoardElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ValidateReferences(element);
        AddUnique(element.Id, element, elementsById, elements, "elemento");

        if (element.NetId is not null)
        {
            netsById[element.NetId].AttachElement(element.Id);
        }

        if (element.ComponentId is not null)
        {
            componentsById[element.ComponentId].AttachElement(element.Id);
        }

        spatialIndex?.Add(element, element.Bounds);
    }

    /// <summary>Removes an element and all its model and spatial-index associations.</summary>
    public bool RemoveElement(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        string normalized = id.Trim();
        if (!elementsById.Remove(normalized, out BoardElement? element))
        {
            return false;
        }

        elements.Remove(element);
        if (element.NetId is not null)
        {
            netsById[element.NetId].DetachElement(element.Id);
        }

        if (element.ComponentId is not null)
        {
            componentsById[element.ComponentId].DetachElement(element.Id);
        }

        spatialIndex?.Remove(element);
        return true;
    }

    /// <summary>
    /// Updates the bounds of an existing element and applies the same mutation to the index.
    /// Geometry-specific data must be updated by the owning element before invoking this method.
    /// </summary>
    public void UpdateElementBounds(string id, Bounds2D bounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!elementsById.TryGetValue(id.Trim(), out BoardElement? element))
        {
            throw new KeyNotFoundException($"No existe el elemento '{id}'.");
        }

        element.UpdateBounds(bounds);
        spatialIndex?.Update(element, bounds);
    }

    public bool TryGetLayer(string id, out BoardLayer? layer) => layersById.TryGetValue(id, out layer);
    public bool TryGetNet(string id, out BoardNet? net) => netsById.TryGetValue(id, out net);
    public bool TryGetComponent(string id, out BoardComponent? component) => componentsById.TryGetValue(id, out component);
    public bool TryGetElement(string id, out BoardElement? element) => elementsById.TryGetValue(id, out element);

    /// <summary>Returns elements intersecting an area.</summary>
    public IReadOnlyList<BoardElement> Query(Bounds2D area) => GetSpatialIndex().Query(area);

    /// <summary>Returns elements located around a point.</summary>
    public IReadOnlyList<BoardElement> Query(Point2D point, double tolerance = 0D) =>
        GetSpatialIndex().Query(point, tolerance);

    /// <summary>Executes an advanced generic spatial query.</summary>
    public SpatialQueryResult<BoardElement> Query(SpatialQuery<BoardElement> query) =>
        GetSpatialIndex().Query(query);

    /// <summary>Executes a board-domain query with layer, net, component and type filters.</summary>
    public SpatialQueryResult<BoardElement> Query(BoardElementQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return GetSpatialIndex().Query(query.ToSpatialQuery());
    }

    /// <summary>Executes all model-integrity rules.</summary>
    public BoardValidationResult Validate() => new BoardDocumentValidator().Validate(this);

    private SpatialIndex<BoardElement> GetSpatialIndex()
    {
        if (spatialIndex is null)
        {
            spatialIndex = new SpatialIndex<BoardElement>();
            spatialIndex.AddRange(elements.Select(static element => (element, element.Bounds)));
        }

        return spatialIndex;
    }

    private void ValidateReferences(BoardElement element)
    {
        if (!layersById.ContainsKey(element.LayerId))
        {
            throw new InvalidOperationException(
                $"El elemento '{element.Id}' referencia la capa inexistente '{element.LayerId}'.");
        }

        if (element.NetId is not null && !netsById.ContainsKey(element.NetId))
        {
            throw new InvalidOperationException(
                $"El elemento '{element.Id}' referencia la red inexistente '{element.NetId}'.");
        }

        if (element.ComponentId is not null && !componentsById.ContainsKey(element.ComponentId))
        {
            throw new InvalidOperationException(
                $"El elemento '{element.Id}' referencia el componente inexistente '{element.ComponentId}'.");
        }
    }

    private static void AddUnique<T>(
        string id,
        T item,
        IDictionary<string, T> index,
        ICollection<T> collection,
        string entityName)
    {
        if (!index.TryAdd(id, item))
        {
            throw new InvalidOperationException($"Ya existe una {entityName} con el identificador '{id}'.");
        }

        collection.Add(item);
    }
}
