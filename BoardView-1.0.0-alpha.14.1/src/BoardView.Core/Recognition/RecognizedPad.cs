using BoardView.Core.Elements;
using BoardView.Core.Geometry;

namespace BoardView.Core.Recognition;

/// <summary>Pad inferido a partir de una primitiva geométrica del documento normalizado.</summary>
public sealed record RecognizedPad(
    string Id,
    string SourceElementId,
    Point2D Center,
    Bounds2D Bounds,
    PadShape Shape,
    double Confidence);
