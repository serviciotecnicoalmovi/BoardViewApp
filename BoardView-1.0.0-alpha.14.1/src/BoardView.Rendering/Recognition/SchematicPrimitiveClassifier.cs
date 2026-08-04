using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Clasifica componentes geométricos como primitivas eléctricas reutilizables
/// por el constructor del grafo esquemático.
/// </summary>
/// <remarks>
/// Esta clase concentra la interpretación geométrica que antes estaba embebida
/// en <see cref="SchematicElectricalGraphBuilder"/>. No crea conexiones ni
/// recorre el grafo; únicamente describe la primitiva observada.
/// </remarks>
public sealed class SchematicPrimitiveClassifier
{
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

        SchematicElectricalNodeKind kind;
        string reason;
        double confidence;

        if (component.Type == BoardGeometryComponentType.ComponentBody)
        {
            kind = SchematicElectricalNodeKind.SymbolBody;
            reason = "Componente clasificado como cuerpo por el kernel geométrico.";
            confidence = component.Confidence;
        }
        else if (component.Type == BoardGeometryComponentType.Hole)
        {
            kind = SchematicElectricalNodeKind.Hole;
            reason = "Componente clasificado como perforación.";
            confidence = component.Confidence;
        }
        else if (component.Type == BoardGeometryComponentType.Pad)
        {
            bool compactJunction =
                area <= options.MaximumJunctionAreaPixels &&
                aspectRatio <= options.MaximumJunctionAspectRatio;

            kind = compactJunction
                ? SchematicElectricalNodeKind.Junction
                : SchematicElectricalNodeKind.Pad;

            reason = compactJunction
                ? "Pad compacto compatible con nodo de unión."
                : "Pad geométrico no compatible con unión compacta.";

            confidence = compactJunction
                ? Math.Max(component.Confidence, 0.72D)
                : component.Confidence;
        }
        else
        {
            bool thinLinearGeometry =
                minimumDimension <= options.MaximumWireThicknessPixels &&
                aspectRatio >= options.MinimumWireAspectRatio;

            if (thinLinearGeometry)
            {
                bool shortLinearGeometry =
                    maximumDimension <= options.MaximumPinLengthPixels;

                kind = shortLinearGeometry
                    ? SchematicElectricalNodeKind.Pin
                    : SchematicElectricalNodeKind.Wire;

                reason = shortLinearGeometry
                    ? "Geometría lineal corta compatible con pin."
                    : "Geometría lineal larga compatible con segmento conductor.";

                confidence = CalculateLinearConfidence(
                    component,
                    aspectRatio,
                    minimumDimension,
                    options);
            }
            else if (area <= options.MaximumJunctionAreaPixels &&
                     aspectRatio <= options.MaximumJunctionAspectRatio)
            {
                kind = SchematicElectricalNodeKind.Junction;
                reason = "Geometría compacta compatible con unión eléctrica.";
                confidence = Math.Max(component.Confidence, 0.58D);
            }
            else if (aspectRatio >= options.MinimumTerminalAspectRatio &&
                     maximumDimension <= options.MaximumTerminalLengthPixels &&
                     minimumDimension <= options.MaximumPinThicknessPixels)
            {
                kind = SchematicElectricalNodeKind.Terminal;
                reason = "Geometría lineal intermedia compatible con terminal.";
                confidence = Math.Max(component.Confidence, 0.62D);
            }
            else if (component.Type == BoardGeometryComponentType.Copper)
            {
                kind = SchematicElectricalNodeKind.Wire;
                reason = "Cobre no lineal conservado como conductor.";
                confidence = Math.Max(component.Confidence, 0.50D);
            }
            else
            {
                kind = SchematicElectricalNodeKind.Unknown;
                reason = "La geometría no satisface una firma eléctrica estable.";
                confidence = component.Confidence * 0.65D;
            }
        }

        return new SchematicPrimitiveClassification(
            component,
            kind,
            orientation,
            Clamp01(confidence),
            CreateEndpoints(bounds, orientation),
            reason);
    }

    private static double CalculateLinearConfidence(
        BoardGeometryIndexedComponent component,
        double aspectRatio,
        double thickness,
        SchematicElectricalGraphBuilderOptions options)
    {
        double aspectScore = Clamp01(
            aspectRatio /
            Math.Max(1D, options.MinimumWireAspectRatio * 2D));

        double thicknessScore = Clamp01(
            1D -
            (thickness /
             Math.Max(1D, options.MaximumWireThicknessPixels * 1.35D)));

        return Clamp01(
            component.Confidence * 0.50D +
            aspectScore * 0.30D +
            thicknessScore * 0.20D);
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