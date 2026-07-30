namespace BoardView.Core.Documents.Common;

/// <summary>Unidades admitidas por el modelo documental común.</summary>
public enum MeasurementUnit
{
    /// <summary>Milímetros; unidad normalizada del núcleo.</summary>
    Millimeter,

    /// <summary>Pulgadas.</summary>
    Inch,

    /// <summary>Puntos tipográficos de PDF (1/72 de pulgada).</summary>
    PdfPoint,

    /// <summary>Píxeles sin una escala física conocida.</summary>
    Pixel,
}
