using BoardView.Core.Repair;

namespace BoardView.App.ViewModels.Repair;

/// <summary>Presentación de una nota persistente de reparación.</summary>
public sealed class RepairAnnotationViewModel
{
    public RepairAnnotationViewModel(RepairAnnotation model) => Model = model;
    public RepairAnnotation Model { get; }
    public string Reference => Model.Reference;
    public string Title => Model.Title;
    public string Notes => Model.Notes;
    public string Status => Model.Status.ToString();
    public string Location => $"Placa: {Model.BoardPage?.ToString() ?? "-"} · Esquema: {Model.SchematicPage?.ToString() ?? "-"}";
}
