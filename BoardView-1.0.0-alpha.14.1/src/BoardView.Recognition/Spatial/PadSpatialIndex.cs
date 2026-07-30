using BoardView.Core.Geometry;
using BoardView.Core.Recognition;

namespace BoardView.Recognition.Spatial;

/// <summary>Índice uniforme especializado para búsquedas vecinales de pads.</summary>
internal sealed class PadSpatialIndex
{
    private readonly double cellSize;
    private readonly Dictionary<(int X, int Y), List<RecognizedPad>> cells = [];

    public PadSpatialIndex(IEnumerable<RecognizedPad> pads, double cellSize)
    {
        ArgumentNullException.ThrowIfNull(pads);
        if (cellSize <= 0D) throw new ArgumentOutOfRangeException(nameof(cellSize));
        this.cellSize = cellSize;
        foreach (RecognizedPad pad in pads)
        {
            (int X, int Y) key = Key(pad.Center);
            if (!cells.TryGetValue(key, out List<RecognizedPad>? bucket))
            {
                bucket = [];
                cells.Add(key, bucket);
            }
            bucket.Add(pad);
        }
    }

    public IEnumerable<RecognizedPad> Query(Point2D center, double radius)
    {
        int range = Math.Max(1, (int)Math.Ceiling(radius / cellSize));
        (int X, int Y) origin = Key(center);
        double radiusSquared = radius * radius;
        for (int x = origin.X - range; x <= origin.X + range; x++)
        {
            for (int y = origin.Y - range; y <= origin.Y + range; y++)
            {
                if (!cells.TryGetValue((x, y), out List<RecognizedPad>? bucket)) continue;
                foreach (RecognizedPad pad in bucket)
                {
                    double dx = pad.Center.X - center.X;
                    double dy = pad.Center.Y - center.Y;
                    if ((dx * dx) + (dy * dy) <= radiusSquared) yield return pad;
                }
            }
        }
    }

    private (int X, int Y) Key(Point2D point) =>
        ((int)Math.Floor(point.X / cellSize), (int)Math.Floor(point.Y / cellSize));
}
