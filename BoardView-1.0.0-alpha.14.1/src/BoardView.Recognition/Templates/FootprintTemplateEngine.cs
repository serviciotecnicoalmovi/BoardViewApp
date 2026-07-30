using BoardView.Recognition.Clustering;
using BoardView.Recognition.Footprints;

namespace BoardView.Recognition.Templates;

/// <summary>Compara métricas normalizadas contra una biblioteca extensible y devuelve la mejor coincidencia auditable.</summary>
public sealed class FootprintTemplateEngine
{
    private readonly IFootprintTemplateLibrary library;

    public FootprintTemplateEngine(IFootprintTemplateLibrary library) => this.library = library ?? throw new ArgumentNullException(nameof(library));

    public FootprintTemplateMatch Match(PadCluster cluster, FootprintMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(metrics);
        double occupancy = metrics.PadCount / (double)Math.Max(1, metrics.Rows * metrics.Columns);
        double pitch = PositiveMedian(metrics.PitchX, metrics.PitchY);
        double aspect = Math.Max(metrics.Bounds.Width, metrics.Bounds.Height) / Math.Max(.000001D, Math.Min(metrics.Bounds.Width, metrics.Bounds.Height));

        FootprintTemplateMatch? best = null;
        foreach (FootprintTemplate template in library.Templates)
        {
            Dictionary<string, double> factors = new(StringComparer.Ordinal)
            {
                ["pads"] = RangeScore(metrics.PadCount, template.MinPads, template.MaxPads),
                ["rows"] = RangeScore(metrics.Rows, template.MinRows, template.MaxRows),
                ["columns"] = RangeScore(metrics.Columns, template.MinColumns, template.MaxColumns),
                ["pitch"] = pitch <= 0D && template.MinPitch > 0D ? 0D : RangeScore(pitch, template.MinPitch, template.MaxPitch),
                ["occupancy"] = RangeScore(occupancy, template.MinOccupancy, template.MaxOccupancy),
                ["symmetry"] = MinimumScore(metrics.Symmetry, template.MinSymmetry),
                ["aspect"] = RangeScore(aspect, template.MinAspectRatio, template.MaxAspectRatio),
                ["topology"] = TopologyScore(template, metrics, occupancy),
            };
            double score = WeightedMean(factors);
            bool accepted = score >= template.AcceptanceScore && factors["pads"] > 0D && factors["topology"] > 0D;
            string status = accepted ? "Coincidencia aceptada" : $"Score inferior a {template.AcceptanceScore:P0}";
            FootprintTemplateMatch match = new(template.Name, template.Family, score, accepted, factors, status);
            if (best is null || match.Score > best.Score) best = match;
        }
        return best ?? FootprintTemplateMatch.None;
    }

    private static double TopologyScore(FootprintTemplate template, FootprintMetrics metrics, double occupancy)
    {
        if (template.RequiresTwoRows && metrics.Rows != 2 && metrics.Columns != 2) return 0D;
        if (template.RequiresSquareMatrix)
        {
            double ratio = Math.Min(metrics.Rows, metrics.Columns) / (double)Math.Max(metrics.Rows, metrics.Columns);
            if (ratio < .70D || occupancy < .50D) return 0D;
        }
        if (template.RequiresFourSides)
        {
            if (metrics.Rows < 3 || metrics.Columns < 3) return 0D;
            if (occupancy > .72D && template.Family is not "Qfn") return .35D;
        }
        return 1D;
    }

    private static double WeightedMean(IReadOnlyDictionary<string, double> factors)
    {
        (string Key, double Weight)[] weights = [("pads", 2.2), ("rows", 1.3), ("columns", 1.3), ("pitch", 1.5),
            ("occupancy", 1.2), ("symmetry", 1.0), ("aspect", .8), ("topology", 2.2)];
        double total = 0D, weightTotal = 0D;
        foreach ((string key, double weight) in weights) { total += factors[key] * weight; weightTotal += weight; }
        return Math.Clamp(total / weightTotal, 0D, 1D);
    }

    private static double PositiveMedian(double a, double b)
    {
        if (a > 0D && b > 0D) return (a + b) / 2D;
        return Math.Max(a, b);
    }

    private static double RangeScore(double value, double min, double max)
    {
        if (value >= min && value <= max) return 1D;
        double span = Math.Max(max - min, Math.Max(Math.Abs(max), 1D));
        double distance = value < min ? min - value : value - max;
        return Math.Clamp(1D - (distance / span), 0D, 1D);
    }

    private static double MinimumScore(double value, double minimum) => minimum <= 0D ? 1D : Math.Clamp(value / minimum, 0D, 1D);
}
