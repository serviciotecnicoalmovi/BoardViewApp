using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Clasifica componentes geométricos como primitivas eléctricas reutilizables
/// por el constructor del grafo esquemático.
/// </summary>
/// <remarks>
/// La clasificación lineal se delega a <see cref="SchematicWireDetector"/>.
/// Esta clase conserva la decisión global entre cuerpo, conductor, unión,
/// perforación, pad y geometría desconocida.
/// </remarks>
public sealed class SchematicPrimitiveClassifier
{
    private readonly SchematicWireDetector wireDetector;

    public SchematicPrimitiveClassifier()
        : this(new SchematicWireDetector())
    {
    }

    public SchematicPrimitiveClassifier(
        SchematicWireDetector wireDetector)
    {
        this.wireDetector =
            wireDetector ??
            throw new ArgumentNullException(nameof(wireDetector));
    }

    /// <summary>
    /// Clasifica una geometría utilizando la configuración del grafo.
    /// </summary>
    public SchematicPrimitiveClassification Classify(
        BoardGeometryIndexedComponent component,
        SchematicElectricalGraphBuilderOptions options)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(options);

        BoardGeometryBounds bounds = component.Bounds;

        double width = Math.Max(1D, bounds.Width);
        double height = Math.Max(1D, bounds.Height);
        double minimumDimension = Math.Min(width, height);
        double maximumDimension = Math.Max(width, height);
        double aspectRatio = maximumDimension / minimumDimension;
        double area = width * height;

        SchematicPrimitiveOrientation orientation =
            ResolveOrientation(width, height);

        if (component.Type == BoardGeometryComponentType.Hole)
        {
            return CreateClassification(
                component,
                SchematicElectricalNodeKind.Hole,
                orientation,
                component.Confidence,
                CreateEndpoints(bounds, orientation),
                "Componente clasificado como perforación.");
        }

        SchematicWireDetection wire =
            wireDetector.Detect(component, options);

        /*
         * La firma lineal se evalúa antes de aceptar ComponentBody. En PDFs
         * esquemáticos, el clasificador geométrico base puede etiquetar líneas
         * largas y placas de símbolos como ComponentBody.
         */
        if (wire.IsDetected)
        {
            return CreateClassification(
                component,
                wire.Kind,
                wire.Orientation,
                wire.Confidence,
                wire.Endpoints,
                wire.Reason);
        }

        if (component.Type == BoardGeometryComponentType.ComponentBody)
        {
            return CreateClassification(
                component,
                SchematicElectricalNodeKind.SymbolBody,
                orientation,
                component.Confidence,
                CreateEndpoints(bounds, orientation),
                "Componente no lineal clasificado como cuerpo por el kernel geométrico.");
        }

        if (component.Type == BoardGeometryComponentType.Pad)
        {
            bool compactJunction =
                area <= options.MaximumJunctionAreaPixels &&
                aspectRatio <= options.MaximumJunctionAspectRatio;

            return CreateClassification(
                component,
                compactJunction
                    ? SchematicElectricalNodeKind.Junction
                    : SchematicElectricalNodeKind.Pad,
                orientation,
                compactJunction
                    ? Math.Max(component.Confidence, 0.72D)
                    : component.Confidence,
                CreateEndpoints(bounds, orientation),
                compactJunction
                    ? "Pad compacto compatible con nodo de unión."
                    : "Pad geométrico no compatible con unión compacta.");
        }

        if (area <= options.MaximumJunctionAreaPixels &&
            aspectRatio <= options.MaximumJunctionAspectRatio)
        {
            double densityScore =
                CalculateCompactDensityScore(component.Density);

            double confidence = Clamp01(
                component.Confidence * 0.58D +
                densityScore * 0.42D);

            return CreateClassification(
                component,
                SchematicElectricalNodeKind.Junction,
                orientation,
                Math.Max(confidence, 0.56D),
                CreateEndpoints(bounds, orientation),
                "Geometría compacta con densidad compatible con unión eléctrica.");
        }

        if (component.Type == BoardGeometryComponentType.Copper)
        {
            return CreateClassification(
                component,
                SchematicElectricalNodeKind.Wire,
                orientation,
                Math.Max(component.Confidence, 0.50D),
                CreateEndpoints(bounds, orientation),
                "Cobre no lineal conservado como conductor eléctrico.");
        }

        return CreateClassification(
            component,
            SchematicElectricalNodeKind.Unknown,
            orientation,
            component.Confidence * 0.65D,
            CreateEndpoints(bounds, orientation),
            "La geometría no satisface una firma eléctrica estable.");
    }

    private static SchematicPrimitiveClassification CreateClassification(
        BoardGeometryIndexedComponent component,
        SchematicElectricalNodeKind kind,
        SchematicPrimitiveOrientation orientation,
        double confidence,
        IReadOnlyList<SchematicPrimitiveEndpoint> endpoints,
        string reason)
    {
        return new SchematicPrimitiveClassification(
            component,
            kind,
            orientation,
            Clamp01(confidence),
            endpoints,
            reason);
    }

    private static double CalculateCompactDensityScore(double density)
    {
        if (!double.IsFinite(density))
        {
            return 0D;
        }

        /*
         * Los puntos de unión suelen ser compactos y relativamente densos,
         * pero no se exige un relleno perfecto para tolerar antialiasing.
         */
        if (density >= 0.28D && density <= 1D)
        {
            return 1D;
        }

        if (density >= 0.12D)
        {
            return 0.64D;
        }

        return 0.22D;
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

            _ =>
            [
                new SchematicPrimitiveEndpoint(bounds.Left, centerY),
                new SchematicPrimitiveEndpoint(bounds.Right, centerY),
                new SchematicPrimitiveEndpoint(centerX, bounds.Top),
                new SchematicPrimitiveEndpoint(centerX, bounds.Bottom)
            ]
        };
    }

    private static double Clamp01(double value) =>
        Math.Max(0D, Math.Min(1D, value));
}

/// <summary>
/// Resultado estable de la clasificación de una primitiva esquemática.
/// </summary>
public sealed record SchematicPrimitiveClassification(
    BoardGeometryIndexedComponent Component,
    SchematicElectricalNodeKind Kind,
    SchematicPrimitiveOrientation Orientation,
    double Confidence,
    IReadOnlyList<SchematicPrimitiveEndpoint> Endpoints,
    string Reason);

/// <summary>
/// Orientación dominante de una primitiva.
/// </summary>
public enum SchematicPrimitiveOrientation
{
    Compact = 0,
    Horizontal = 1,
    Vertical = 2
}

/// <summary>
/// Extremo lógico de una primitiva lineal.
/// </summary>
public readonly record struct SchematicPrimitiveEndpoint(
    double X,
    double Y);