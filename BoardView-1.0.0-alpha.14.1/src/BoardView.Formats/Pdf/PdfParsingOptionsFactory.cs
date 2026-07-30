using System.Reflection;
using UglyToad.PdfPig;

namespace BoardView.Formats.Pdf;

/// <summary>
/// Crea opciones uniformes y tolerantes para todos los lectores PDF de BoardView.
/// Los documentos técnicos de terceros suelen contener fuentes, anotaciones o
/// destinos internos no conformes que los visores comerciales ignoran.
/// </summary>
internal static class PdfParsingOptionsFactory
{
    /// <summary>
    /// Crea opciones orientadas a extracción técnica. Se omiten fuentes dañadas
    /// y anotaciones porque BoardView obtiene texto, vectores y dimensiones de
    /// página, pero no necesita ejecutar acciones ni destinos de anotación.
    /// </summary>
    public static ParsingOptions CreateResilient()
    {
        ParsingOptions options = new()
        {
            UseLenientParsing = true,
            SkipMissingFonts = true,
        };

        // PdfPig incorporó la omisión completa de anotaciones después de que la
        // propiedad ParsingOptions fuese publicada. La reflexión mantiene esta
        // fábrica compatible con revisiones menores y evita una dependencia
        // rígida del nombre de la propiedad.
        SetBooleanOption(options, "SkipAnnotations", true);
        SetBooleanOption(options, "SkipPageAnnotations", true);

        return options;
    }

    private static void SetBooleanOption(ParsingOptions options, string propertyName, bool value)
    {
        PropertyInfo? property = typeof(ParsingOptions).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);

        if (property?.CanWrite == true && property.PropertyType == typeof(bool))
        {
            property.SetValue(options, value);
        }
    }
}
