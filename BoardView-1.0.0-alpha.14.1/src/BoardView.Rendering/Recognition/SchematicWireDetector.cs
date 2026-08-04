using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Detecta primitivas lineales que representan conductores, pines o terminales
/// dentro de un esquema eléctrico.
/// </summary>
/// <remarks>
/// El detector no crea aristas ni modifica el grafo. Su responsabilidad es
/// decidir si una geometría posee una firma lineal suficientemente estable y
/// describir su orientación, extremos, espesor y confianza.
/// </remarks>
public sealed class SchematicWireDetector
{
    /// <summary>
    /// Analiza una geometría y devuelve una detección lineal inmutable.
    /// </summary>
    public SchematicWireDetection Detect(
        BoardGeometryIndexedComponent component,
        SchematicElectricalGraphBuilderOptions options)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(options);

        BoardGeometryBounds bounds = component.Bounds;

        double width = Math.Max(1D, bounds.Width);
        double height = Math.Max(1D, bounds.Height);
        double length = Math.Max(width, height);
        double thickness = Math.Min(width, height);
        double aspectRatio = length / thickness;
        double area = width * height;
        double fillRatio = Clamp01(component.PixelCount / Math.Max(1D, area));

        SchematicPrimitiveOrientation orientation =
            ResolveOrientation(width, height);

        bool orientationIsLinear =
            orientation is
                SchematicPrimitiveOrientation.Horizontal or
                SchematicPrimitiveOrientation.Vertical;

        bool thicknessIsValid =
            thickness <= options.MaximumWireThicknessPixels;

        bool aspectIsValid =
            aspectRatio >= options.MinimumWireAspectRatio;

        bool typeSupportsConductor =
            component.Type is
                BoardGeometryComponentType.Copper or
                BoardGeometryComponentType.Unknown or
                BoardGeometryComponentType.ComponentBody or
                BoardGeometryComponentType.Pad;

        /*
         * Una línea esquemática puede tener una densidad baja debido al
         * antialiasing o a que el componente conectado contiene huecos. Se usa
         * una banda amplia, pero se penalizan rectángulos completamente llenos
         * que suelen corresponder a cuerpos o puntos de unión.
         */
        bool fillIsPlausible =
            fillRatio >= 0.04D &&
            fillRatio <= 0.92D;

        bool isLinear =
            orientationIsLinear &&
            thicknessIsValid &&
            aspectIsValid &&
            typeSupportsConductor &&
            fillIsPlausible;

        if (!isLinear)
        {
            return SchematicWireDetection.NotDetected(
                orientation,
                length,
                thickness,
                aspectRatio,
                fillRatio);
        }

        double aspectScore = Clamp01(
            aspectRatio /
            Math.Max(1D, options.MinimumWireAspectRatio * 2.25D));

        double thicknessScore = Clamp01(
            1D -
            (thickness /
             Math.Max(1D, options.MaximumWireThicknessPixels * 1.30D)));

        double densityScore = CalculateDensityScore(fillRatio);

        double typeScore = component.Type switch
        {
            BoardGeometryComponentType.Copper => 1D,
            BoardGeometryComponentType.Unknown => 0.82D,
            BoardGeometryComponentType.Pad => 0.52D,
            BoardGeometryComponentType.ComponentBody => 0.38D,
            _ => 0.20D
        };

        double confidence = Clamp01(
            component.Confidence * 0.30D +
            aspectScore * 0.28D +
            thicknessScore * 0.20D +
            densityScore * 0.12D +
            typeScore * 0.10D);

        SchematicElectricalNodeKind kind = ResolveKind(
            length,
            thickness,
            aspectRatio,
            component,
            options);

        return new SchematicWireDetection(
            true,
            kind,
            orientation,
            confidence,
            length,
            thickness,
            aspectRatio,
            fillRatio,
            CreateEndpoints(bounds, orientation),
            CreateReason(kind, length, thickness, aspectRatio, fillRatio));
    }

    private static SchematicElectricalNodeKind ResolveKind(
        double length,
        double thickness,
        double aspectRatio,
        BoardGeometryIndexedComponent component,
        SchematicElectricalGraphBuilderOptions options)
    {
        bool pinLength =
            length <= options.MaximumPinLengthPixels;

        bool terminalLength =
            length <= options.MaximumTerminalLengthPixels;

        bool thinPin =
            thickness <= options.MaximumPinThicknessPixels;

        if (pinLength && thinPin)
        {
            return SchematicElectricalNodeKind.Pin;
        }

        if (terminalLength &&
            thinPin &&
            aspectRatio >= options.MinimumTerminalAspectRatio)
        {
            return SchematicElectricalNodeKind.Terminal;
        }

        if (component.Type == BoardGeometryComponentType.Pad &&
            length <= options.MaximumRecoverableTerminalLengthPixels)
        {
            return SchematicElectricalNodeKind.Terminal;
        }

        return SchematicElectricalNodeKind.Wire;
    }

    private static double CalculateDensityScore(double fillRatio)
    {
        const double preferred = 0.28D;
        double distance = Math.Abs(fillRatio - preferred);

        return Clamp01(
            1D -
            (distance / 0.64D));
    }

    private static string CreateReason(
        SchematicElectricalNodeKind kind,
        double length,
        double thickness,
        double aspectRatio,
        double fillRatio)
    {
        return $"Firma lineal {kind}: longitud={length:0.##}, " +
               $"espesor={thickness:0.##}, relación={aspectRatio:0.##}, " +
               $"relleno={fillRatio:0.###}.";
    }

    private static SchematicPrimitiveOrientation ResolveOrientation(
        double width,
        double height)
    {
        if (width >= height * 1.50D)
        {
            return SchematicPrimitiveOrientation.Horizontal;
        }

        if (height >= width * 1.50D)
        {
            return SchematicPrimitiveOrientation.Vertical;
        }

        return SchematicPrimitiveOrientation.Compact;
    }

    private static IReadOnlyList<SchematicPrimitiveEndpoint> CreateEndpoints(
        BoardGeometryBounds bounds,
        SchematicPrimitiveOrientation orientation)
    {
        double centerX = bounds.Left + bounds.Width / 2D;
        double centerY = bounds.Top + bounds.Height / 2D;

        return orientation switch
        {
            SchematicPrimitiveOrientation.Horizontal =>
            [
                new SchematicPrimitiveEndpoint(bounds.Left, centerY),
                new SchematicPrimitiveEndpoint(bounds.Right, centerY)
            ],

            SchematicPrimitiveOrientation.Vertical =>
            [
                new SchematicPrimitiveEndpoint(centerX, bounds.Top),
                new SchematicPrimitiveEndpoint(centerX, bounds.Bottom)
            ],

            _ => Array.Empty<SchematicPrimitiveEndpoint>()
        };
    }

    private static double Clamp01(double value) =>
        Math.Max(0D, Math.Min(1D, value));
}

/// <summary>
/// Resultado de la detección de una primitiva lineal.
/// </summary>
public sealed record SchematicWireDetection(
    bool IsDetected,
    SchematicElectricalNodeKind Kind,
    SchematicPrimitiveOrientation Orientation,
    double Confidence,
    double Length,
    double Thickness,
    double AspectRatio,
    double FillRatio,
    IReadOnlyList<SchematicPrimitiveEndpoint> Endpoints,
    string Reason)
{
    public static SchematicWireDetection NotDetected(
        SchematicPrimitiveOrientation orientation,
        double length,
        double thickness,
        double aspectRatio,
        double fillRatio)
    {
        return new SchematicWireDetection(
            false,
            SchematicElectricalNodeKind.Unknown,
            orientation,
            0D,
            length,
            thickness,
            aspectRatio,
            fillRatio,
            Array.Empty<SchematicPrimitiveEndpoint>(),
            "La geometría no satisface la firma lineal mínima.");
    }
}