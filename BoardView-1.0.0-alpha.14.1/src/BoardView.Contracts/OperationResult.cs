namespace BoardView.Contracts;

/// <summary>Representa el resultado inmutable de una operación sin utilizar excepciones para errores esperados.</summary>
public sealed record OperationResult(bool IsSuccessful, string? ErrorCode, string? ErrorMessage)
{
    /// <summary>Crea un resultado satisfactorio.</summary>
    public static OperationResult Success() => new(true, null, null);

    /// <summary>Crea un resultado fallido con un código y un mensaje descriptivos.</summary>
    public static OperationResult Failure(string errorCode, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new OperationResult(false, errorCode, errorMessage);
    }
}
