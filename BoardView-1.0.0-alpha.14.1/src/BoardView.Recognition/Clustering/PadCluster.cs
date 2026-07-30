using BoardView.Core.Geometry;
using BoardView.Core.Recognition;

namespace BoardView.Recognition.Clustering;

/// <summary>Grupo conectado de pads candidato a footprint.</summary>
public sealed record PadCluster(
    string Id,
    IReadOnlyList<RecognizedPad> Pads,
    Bounds2D Bounds,
    Point2D Center,
    double MedianPadSize,
    double Confidence);
