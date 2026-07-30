using BoardView.Core.Geometry;
using BoardView.Core.Recognition;
using BoardView.Recognition.Spatial;

namespace BoardView.Recognition.Clustering;

/// <summary>Agrupa pads mediante conectividad espacial y tamaño físico adaptativo.</summary>
public sealed class PadClusterBuilder
{
    public IReadOnlyList<PadCluster> Build(IReadOnlyList<RecognizedPad> pads, RecognitionOptions options)
    {
        ArgumentNullException.ThrowIfNull(pads);
        ArgumentNullException.ThrowIfNull(options);
        if (pads.Count == 0) return [];

        double median = Median(pads.Select(static pad => Math.Max(pad.Bounds.Width, pad.Bounds.Height)));
        double radius = Math.Min(options.MaximumNeighborDistanceMillimeters, Math.Max(median * options.NeighborScale, median * 1.5D));
        PadSpatialIndex index = new(pads, Math.Max(radius, 0.05D));
        HashSet<string> visited = new(StringComparer.Ordinal);
        List<PadCluster> clusters = [];

        foreach (RecognizedPad seed in pads)
        {
            if (!visited.Add(seed.Id)) continue;
            Queue<RecognizedPad> queue = new();
            queue.Enqueue(seed);
            List<RecognizedPad> members = [];
            while (queue.Count > 0)
            {
                RecognizedPad current = queue.Dequeue();
                members.Add(current);
                foreach (RecognizedPad neighbor in index.Query(current.Center, radius))
                {
                    if (neighbor.Id == current.Id || !Compatible(current, neighbor)) continue;
                    if (visited.Add(neighbor.Id)) queue.Enqueue(neighbor);
                }
            }

            if (members.Count < options.MinimumPadsPerFootprint) continue;
            Bounds2D bounds = members.Select(static pad => pad.Bounds).Aggregate(static (left, right) => left.Union(right));
            double memberMedian = Median(members.Select(static pad => Math.Max(pad.Bounds.Width, pad.Bounds.Height)));
            double compactness = Math.Clamp((members.Count * memberMedian * memberMedian) / Math.Max(bounds.Width * bounds.Height, 0.000001D), 0D, 1D);
            clusters.Add(new PadCluster($"cluster-{clusters.Count + 1:D4}", members, bounds, bounds.Center, memberMedian, 0.55D + (0.4D * compactness)));
        }

        return clusters.OrderBy(static cluster => cluster.Bounds.Top).ThenBy(static cluster => cluster.Bounds.Left).ToArray();
    }

    private static bool Compatible(RecognizedPad first, RecognizedPad second)
    {
        double firstSize = Math.Max(first.Bounds.Width, first.Bounds.Height);
        double secondSize = Math.Max(second.Bounds.Width, second.Bounds.Height);
        double ratio = Math.Max(firstSize, secondSize) / Math.Max(Math.Min(firstSize, secondSize), 0.000001D);
        return ratio <= 2.75D;
    }

    internal static double Median(IEnumerable<double> values)
    {
        double[] ordered = values.Where(static value => value > 0D).OrderBy(static value => value).ToArray();
        if (ordered.Length == 0) return 0.1D;
        int middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2D : ordered[middle];
    }
}
