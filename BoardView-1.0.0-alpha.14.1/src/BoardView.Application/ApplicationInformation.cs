namespace BoardView.Application;

/// <summary>Expone la identidad y versión del producto desde un único origen de verdad.</summary>
public static class ApplicationInformation
{
    /// <summary>Nombre comercial de la aplicación.</summary>
    public const string ProductName = "BoardView";

    /// <summary>Versión funcional actual.</summary>
    public const string Version = "1.0.0-alpha.1";

    /// <summary>Nombre completo apto para diagnósticos y registros.</summary>
    public static string DisplayName => $"{ProductName} {Version}";
}
