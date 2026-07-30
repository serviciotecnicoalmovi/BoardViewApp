namespace BoardView.Recognition.Templates;

/// <summary>Expone la colección validada de plantillas disponibles para el reconocimiento.</summary>
public interface IFootprintTemplateLibrary
{
    IReadOnlyList<FootprintTemplate> Templates { get; }
    string SourceDirectory { get; }
}
