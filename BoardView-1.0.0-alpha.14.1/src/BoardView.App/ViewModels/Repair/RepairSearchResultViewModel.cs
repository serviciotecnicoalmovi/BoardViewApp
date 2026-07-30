namespace BoardView.App.ViewModels.Repair;

/// <summary>Coincidencia de una referencia en uno de los documentos del workspace.</summary>
public sealed class RepairSearchResultViewModel
{
    public required string DocumentRole { get; init; }
    public required int PageNumber { get; init; }
    public required int Occurrences { get; init; }
    public required string Reference { get; init; }

    /// <summary>Orden estable para mostrar primero la placa y después el esquema.</summary>
    public int DocumentOrder =>
        string.Equals(DocumentRole, "Placa", StringComparison.Ordinal) ? 0 : 1;

    public string DisplayText =>
        $"{DocumentRole} · página {PageNumber} · {Occurrences} coincidencia(s)";
}
