using System.Text.RegularExpressions;
using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;
using BoardView.Recognition.Footprints;

namespace BoardView.Recognition.Components;

/// <summary>Asocia referencias textuales próximas y construye componentes electrónicos.</summary>
public sealed partial class ComponentBuilder
{
    public IReadOnlyList<RecognizedComponentModel> Build(BoardDocument document, IReadOnlyList<RecognizedFootprintModel> footprints)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(footprints);
        TextElement[] references = document.Elements.OfType<TextElement>()
            .Where(static text => ReferenceRegex().IsMatch(text.Text.Trim()))
            .ToArray();
        HashSet<string> used = new(StringComparer.Ordinal);
        List<RecognizedComponentModel> components = [];
        foreach (RecognizedFootprintModel footprint in footprints)
        {
            TextElement? nearest = references
                .Where(text => !used.Contains(text.Id))
                .Select(text => new { Text = text, Distance = Distance(text.Bounds.Center, footprint.Center) })
                .Where(item => item.Distance <= Math.Max(footprint.Bounds.Width, footprint.Bounds.Height) * 2.5D + 2D)
                .OrderBy(static item => item.Distance)
                .Select(static item => item.Text)
                .FirstOrDefault();
            string reference = nearest?.Text.Trim() ?? $"X{components.Count + 1:D4}";
            if (nearest is not null) used.Add(nearest.Id);
            double confidence = nearest is null ? footprint.Confidence * 0.8D : Math.Min(1D, footprint.Confidence + 0.08D);
            components.Add(new RecognizedComponentModel(
                $"component-{components.Count + 1:D4}", reference, footprint,
                footprint.Center, footprint.Bounds, footprint.Metrics.RotationDegrees, confidence));
        }
        return components;
    }

    private static double Distance(Point2D a, Point2D b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    [GeneratedRegex(@"^(?:R|C|L|D|Q|U|IC|J|P|CN|TP|XW|PP|F|Y|K|M)\d+[A-Z0-9_-]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceRegex();
}
