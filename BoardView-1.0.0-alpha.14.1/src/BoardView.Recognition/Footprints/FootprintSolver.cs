using BoardView.Core.Geometry;
using BoardView.Recognition.Clustering;
using BoardView.Recognition.Templates;

namespace BoardView.Recognition.Footprints;

/// <summary>Calcula métricas físicas y delega la identificación del encapsulado al motor de plantillas.</summary>
public sealed class FootprintSolver
{
    private readonly FootprintTemplateEngine templateEngine;

    public FootprintSolver() : this(new FootprintTemplateEngine(new JsonFootprintTemplateLibrary())) { }
    public FootprintSolver(FootprintTemplateEngine templateEngine) => this.templateEngine = templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));

    public RecognizedFootprintModel Solve(PadCluster cluster, RecognitionOptions options)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(options);
        double tolerance = Math.Max(cluster.MedianPadSize * options.AxisToleranceScale, 0.03D);
        double[] xs = MergeAxis(cluster.Pads.Select(static pad => pad.Center.X), tolerance);
        double[] ys = MergeAxis(cluster.Pads.Select(static pad => pad.Center.Y), tolerance);
        double pitchX = MedianPitch(xs);
        double pitchY = MedianPitch(ys);
        int rows = ys.Length;
        int columns = xs.Length;
        double symmetry = CalculateSymmetry(cluster, tolerance);
        double rotation = cluster.Bounds.Width >= cluster.Bounds.Height ? 0D : 90D;
        FootprintMetrics metrics = new(cluster.Pads.Count, rows, columns, pitchX, pitchY, rotation, symmetry, cluster.Bounds);
        FootprintTemplateMatch match = templateEngine.Match(cluster, metrics);
        FootprintKind kind = ParseKind(match.Family);
        string name = BuildName(match, metrics);
        double confidence = Math.Clamp((cluster.Confidence + match.Score) / 2D, 0D, 1D);
        return new RecognizedFootprintModel($"footprint-{cluster.Id}", kind, name, cluster, metrics, cluster.Pads,
            cluster.Bounds, cluster.Center, confidence, match);
    }

    private static string BuildName(FootprintTemplateMatch match, FootprintMetrics metrics)
    {
        if (!match.Accepted) return $"UNRESOLVED-{metrics.PadCount}";
        return match.Family switch
        {
            "Chip2" => "CHIP-2",
            "Soic" => $"SOIC-{metrics.PadCount}",
            "Tssop" => $"TSSOP-{metrics.PadCount}",
            "Qfn" => $"QFN-{metrics.PadCount}",
            "Qfp" => $"QFP-{metrics.PadCount}",
            "Bga" => $"BGA-{metrics.PadCount}",
            "Ffc" => $"FFC-{metrics.PadCount}",
            "SingleRowConnector" => $"CONN-{metrics.PadCount}",
            "DualRowConnector" => $"DUAL-{metrics.PadCount}",
            _ => $"ARRAY-{metrics.PadCount}",
        };
    }

    private static FootprintKind ParseKind(string family) => Enum.TryParse(family, true, out FootprintKind kind) ? kind : FootprintKind.Unknown;

    private static double[] MergeAxis(IEnumerable<double> values, double tolerance)
    {
        double[] ordered = values.OrderBy(static value => value).ToArray();
        if (ordered.Length == 0) return [];
        List<double> merged = [];
        double sum = ordered[0]; int count = 1;
        for (int i = 1; i < ordered.Length; i++)
        {
            double average = sum / count;
            if (Math.Abs(ordered[i] - average) <= tolerance) { sum += ordered[i]; count++; }
            else { merged.Add(average); sum = ordered[i]; count = 1; }
        }
        merged.Add(sum / count);
        return [.. merged];
    }

    private static double MedianPitch(double[] axis) => axis.Length < 2 ? 0D : PadClusterBuilder.Median(axis.Zip(axis.Skip(1), static (a, b) => b - a));

    private static double CalculateSymmetry(PadCluster cluster, double tolerance)
    {
        int mirrored = 0;
        foreach (var pad in cluster.Pads)
        {
            double mx = (2D * cluster.Center.X) - pad.Center.X;
            double my = (2D * cluster.Center.Y) - pad.Center.Y;
            if (cluster.Pads.Any(other => Math.Abs(other.Center.X - mx) <= tolerance && Math.Abs(other.Center.Y - my) <= tolerance)) mirrored++;
        }
        return mirrored / (double)Math.Max(1, cluster.Pads.Count);
    }
}
