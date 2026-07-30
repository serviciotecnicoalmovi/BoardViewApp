using BoardView.Core.Geometry;

namespace BoardView.Core.Elements;

/// <summary>Polígono cerrado utilizado para contornos, zonas y geometría mecánica.</summary>
public sealed class PolygonElement : BoardElement
{
    public PolygonElement(
        string id,
        string layerId,
        IEnumerable<Point2D> vertices,
        bool isFilled,
        string? netId = null)
        : this(id, layerId, CopyVertices(vertices), isFilled, netId)
    {
    }

    private PolygonElement(
        string id,
        string layerId,
        IReadOnlyList<Point2D> vertices,
        bool isFilled,
        string? netId)
        : base(id, layerId, Bounds2D.FromPoints(vertices), netId)
    {
        if (vertices.Count < 3)
        {
            throw new ArgumentException("Un polígono requiere al menos tres vértices.", nameof(vertices));
        }

        Vertices = vertices;
        IsFilled = isFilled;
    }

    public IReadOnlyList<Point2D> Vertices { get; }
    public bool IsFilled { get; }

    private static IReadOnlyList<Point2D> CopyVertices(IEnumerable<Point2D> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        return Array.AsReadOnly(vertices.ToArray());
    }
}
