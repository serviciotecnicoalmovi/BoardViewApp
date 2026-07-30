using BoardView.Core.Formats;

namespace BoardView.App.ViewModels;

/// <summary>Presenta un formato registrado con metadatos exclusivamente visuales.</summary>
public sealed class FormatListItemViewModel
{
    public FormatListItemViewModel(FileFormatDescriptor descriptor, string iconGlyph, string accentColor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        IconGlyph = iconGlyph;
        AccentColor = accentColor;
    }

    public FileFormatDescriptor Descriptor { get; }
    public string DisplayName => Descriptor.DisplayName;
    public string Category => Descriptor.Category;
    public bool ParserAvailable => Descriptor.ParserAvailable;
    public string IconGlyph { get; }
    public string AccentColor { get; }
    public string StatusColor => ParserAvailable ? "#31C46C" : "#536579";

    /// <summary>Describe el estado actual del lector asociado al formato.</summary>
    public string StatusText => ParserAvailable ? "Parser disponible" : "Parser pendiente";

    /// <summary>Texto informativo mostrado sin ocupar espacio permanente en el explorador.</summary>
    public string ToolTipText
    {
        get
        {
            string extensions = string.Join(", ", Descriptor.Extensions);
            return $"{DisplayName}\nExtensiones: {extensions}\nEstado: {StatusText}";
        }
    }
}
