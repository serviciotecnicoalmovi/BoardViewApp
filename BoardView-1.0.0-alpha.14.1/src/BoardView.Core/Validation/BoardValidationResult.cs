namespace BoardView.Core.Validation;

/// <summary>Resultado inmutable de la validación del modelo.</summary>
public sealed class BoardValidationResult
{
    public BoardValidationResult(IEnumerable<BoardValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = issues.ToArray();
    }

    public IReadOnlyList<BoardValidationIssue> Issues { get; }
    public bool IsValid => Issues.All(static issue => issue.Severity != BoardValidationSeverity.Error);
}
