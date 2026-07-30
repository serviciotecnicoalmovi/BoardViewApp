namespace BoardView.Rendering.Controls;

/// <summary>Define cómo se compone el render nativo respecto al documento original.</summary>
public enum BoardViewportMode
{
    /// <summary>El render nativo ocupa toda la superficie.</summary>
    Model,

    /// <summary>El render nativo se dibuja con fondo transparente sobre el documento original.</summary>
    Overlay,
}
