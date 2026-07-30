using BoardView.Core.Documents.Common;

namespace BoardView.Core.Geometry;

/// <summary>Conversiones deterministas entre unidades documentales.</summary>
public static class UnitConverter
{
    private const double MillimetersPerInch = 25.4D;
    private const double PdfPointsPerInch = 72D;

    public static double Convert(double value, MeasurementUnit source, MeasurementUnit target)
    {
        if (source == target)
        {
            return value;
        }

        if (source == MeasurementUnit.Pixel || target == MeasurementUnit.Pixel)
        {
            throw new InvalidOperationException(
                "Los píxeles requieren una escala física explícita y no pueden convertirse directamente.");
        }

        double millimeters = source switch
        {
            MeasurementUnit.Millimeter => value,
            MeasurementUnit.Inch => value * MillimetersPerInch,
            MeasurementUnit.PdfPoint => value * MillimetersPerInch / PdfPointsPerInch,
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

        return target switch
        {
            MeasurementUnit.Millimeter => millimeters,
            MeasurementUnit.Inch => millimeters / MillimetersPerInch,
            MeasurementUnit.PdfPoint => millimeters * PdfPointsPerInch / MillimetersPerInch,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
    }
}
