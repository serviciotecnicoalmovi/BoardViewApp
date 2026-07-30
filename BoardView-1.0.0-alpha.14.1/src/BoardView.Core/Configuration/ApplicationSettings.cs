namespace BoardView.Core.Configuration;

/// <summary>Opciones persistentes de la aplicación.</summary>
public sealed class ApplicationSettings
{
    public double WindowWidth { get; set; } = 1440;
    public double WindowHeight { get; set; } = 900;
    public bool IsWindowMaximized { get; set; }
    public string? LastOpenedDirectory { get; set; }
    public bool ShowGrid { get; set; } = true;
}
