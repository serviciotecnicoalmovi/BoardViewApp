namespace BoardView.Core.Recognition;

/// <summary>Motivo determinista por el que una primitiva no fue aceptada como pad.</summary>
public enum PadCandidateRejectionReason
{
    None,
    TooSmall,
    TooLarge,
    InvalidAspectRatio,
    UnsupportedGeometry,
    OutlineWithoutPattern,
    LowConfidence,
    Duplicate,
}
