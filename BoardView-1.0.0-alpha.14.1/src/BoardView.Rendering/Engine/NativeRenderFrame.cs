using BoardView.Core.Elements;
using BoardView.Rendering.Viewport;

namespace BoardView.Rendering.Engine;

/// <summary>Immutable result of the visibility stage for one native render frame.</summary>
public sealed record NativeRenderFrame(
    ViewportTransform Transform,
    IReadOnlyList<BoardElement> VisibleElements,
    IReadOnlyDictionary<string, BoardView.Core.Documents.BoardLayer> Layers);
