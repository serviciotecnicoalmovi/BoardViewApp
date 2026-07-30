namespace BoardView.Core.Validation;

/// <summary>Hallazgo producido al validar un documento de placa.</summary>
public sealed record BoardValidationIssue(BoardValidationSeverity Severity, string Code, string Message, string? EntityId = null);
