using BoardView.Core.Geometry;

namespace BoardView.Core.Contracts.Documents;

/// <summary>Resultado localizable dentro de un documento.</summary>
public sealed record DocumentSearchResult(
    int PageNumber,
    string ObjectId,
    string DisplayText,
    Bounds2D Bounds);
