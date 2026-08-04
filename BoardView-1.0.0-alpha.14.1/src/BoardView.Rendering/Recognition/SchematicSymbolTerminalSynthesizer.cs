using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Recupera terminales geométricos ausentes creando nodos puente entre un
/// cuerpo de símbolo y conductores reales cercanos.
/// </summary>
/// <remarks>
/// Algunos PDF agrupan el texto, las placas y parte del símbolo en un único
/// componente conectado, pero dejan los terminales como espacios blancos.
/// En ese caso no existe un nodo que el BFS pueda atravesar.
///
/// El sintetizador no conecta por proximidad general. Sólo crea un terminal
/// cuando se cumplen simultáneamente:
/// <list type="bullet">
/// <item>el conductor está alineado con un eje principal del cuerpo;</item>
/// <item>la distancia al borde es pequeña y positiva;</item>
/// <item>el puente es fino y claramente lineal;</item>
/// <item>el conductor no atraviesa el interior del cuerpo;</item>
/// <item>se conserva como máximo un terminal por lado.</item>
/// </list>
/// </remarks>
public sealed class SchematicSymbolTerminalSynthesizer
{
    private const double SyntheticConfidence = 0.91D;
    private const double MinimumBridgeLength = 1D;
    private const double MaximumBridgeLength = 96D;
    private const double DefaultThickness = 3D;

    /// <summary>
    /// Devuelve los nodos originales más los terminales sintéticos recuperados.
    /// </summary>
    public IReadOnlyList<SchematicElectricalNode> Synthesize(
        IReadOnlyList<SchematicElectricalNode> nodes,
        SchematicElectricalGraphBuilderOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(options);

        if (nodes.Count == 0)
        {
            return Array.Empty<SchematicElectricalNode>();
        }

        var result =
            nodes
                .OrderBy(node => node.Id)
                .ToList();

        SchematicElectricalNode[] bodies =
            nodes
                .Where(node =>
                    node.Kind ==
                    SchematicElectricalNodeKind.SymbolBody)
                .OrderBy(node => node.Id)
                .ToArray();

        SchematicElectricalNode[] conductors =
            nodes
                .Where(node =>
                    node.Kind is
                        SchematicElectricalNodeKind.Wire or
                        SchematicElectricalNodeKind.Pin or
                        SchematicElectricalNodeKind.Terminal or
                        SchematicElectricalNodeKind.Junction)
                .OrderBy(node => node.Id)
                .ToArray();

        int nextId =
            nodes.Max(node => node.Id) + 1;

        foreach (SchematicElectricalNode body in bodies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (TerminalSide side in Enum.GetValues<TerminalSide>())
            {
                TerminalProposal? proposal =
                    FindBestProposal(
                        body,
                        conductors,
                        side,
                        options);

                if (proposal is null)
                {
                    continue;
                }

                BoardGeometryBounds bounds =
                    proposal.Value.Bounds;

                long pixelCount =
                    Math.Max(
                        1L,
                        (long)Math.Round(
                            Math.Max(
                                bounds.Width,
                                bounds.Height) *
                            Math.Max(
                                1D,
                                Math.Min(
                                    bounds.Width,
                                    bounds.Height))));

                double area =
                    Math.Max(
                        1D,
                        bounds.Width * bounds.Height);

                BoardGeometryIndexedComponent syntheticComponent =
                    body.Component with
                    {
                        Id = nextId,
                        Type = BoardGeometryComponentType.Copper,
                        Confidence = SyntheticConfidence,
                        Bounds = bounds,
                        PixelCount = pixelCount,
                        Density = Math.Min(1D, pixelCount / area),
                        CenterX = bounds.Left + bounds.Width / 2D,
                        CenterY = bounds.Top + bounds.Height / 2D
                    };

                result.Add(
                    new SchematicElectricalNode(
                        nextId,
                        SchematicElectricalNodeKind.Terminal,
                        syntheticComponent));

                nextId++;
            }
        }

        return result
            .OrderBy(node => node.Id)
            .ToArray();
    }

    private static TerminalProposal? FindBestProposal(
        SchematicElectricalNode body,
        IReadOnlyList<SchematicElectricalNode> conductors,
        TerminalSide side,
        SchematicElectricalGraphBuilderOptions options)
    {
        double maximumGap =
            Math.Min(
                MaximumBridgeLength,
                Math.Max(
                    options.MaximumRecoverableTerminalLengthPixels,
                    options.MaximumPinBodyEndpointGapPixels +
                    options.MaximumPinConductorEndpointGapPixels));

        TerminalProposal[] proposals =
            conductors
                .Where(conductor =>
                    conductor.Id != body.Id)
                .Select(conductor =>
                    Evaluate(
                        body,
                        conductor,
                        side,
                        maximumGap,
                        options))
                .Where(proposal =>
                    proposal.IsValid)
                .OrderByDescending(proposal =>
                    proposal.Score)
                .ThenBy(proposal =>
                    proposal.Gap)
                .ThenBy(proposal =>
                    proposal.ConductorId)
                .ToArray();

        return proposals.Length == 0
            ? null
            : proposals[0];
    }

    private static TerminalProposal Evaluate(
        SchematicElectricalNode body,
        SchematicElectricalNode conductor,
        TerminalSide side,
        double maximumGap,
        SchematicElectricalGraphBuilderOptions options)
    {
        BoardGeometryBounds bodyBounds =
            body.Bounds;

        BoardGeometryBounds conductorBounds =
            conductor.Bounds;

        bool verticalSide =
            side is TerminalSide.Top or TerminalSide.Bottom;

        double gap;
        double axisOffset;
        double bodyContactX;
        double bodyContactY;
        double conductorContactX;
        double conductorContactY;

        if (verticalSide)
        {
            double bodyAxis =
                body.CenterX;

            double conductorAxis =
                Clamp(
                    bodyAxis,
                    conductorBounds.Left,
                    conductorBounds.Right);

            axisOffset =
                Math.Abs(
                    bodyAxis -
                    conductorAxis);

            if (side == TerminalSide.Top)
            {
                gap =
                    bodyBounds.Top -
                    conductorBounds.Bottom;

                bodyContactY =
                    bodyBounds.Top;

                conductorContactY =
                    conductorBounds.Bottom;
            }
            else
            {
                gap =
                    conductorBounds.Top -
                    bodyBounds.Bottom;

                bodyContactY =
                    bodyBounds.Bottom;

                conductorContactY =
                    conductorBounds.Top;
            }

            bodyContactX =
                bodyAxis;

            conductorContactX =
                conductorAxis;
        }
        else
        {
            double bodyAxis =
                body.CenterY;

            double conductorAxis =
                Clamp(
                    bodyAxis,
                    conductorBounds.Top,
                    conductorBounds.Bottom);

            axisOffset =
                Math.Abs(
                    bodyAxis -
                    conductorAxis);

            if (side == TerminalSide.Left)
            {
                gap =
                    bodyBounds.Left -
                    conductorBounds.Right;

                bodyContactX =
                    bodyBounds.Left;

                conductorContactX =
                    conductorBounds.Right;
            }
            else
            {
                gap =
                    conductorBounds.Left -
                    bodyBounds.Right;

                bodyContactX =
                    bodyBounds.Right;

                conductorContactX =
                    conductorBounds.Left;
            }

            bodyContactY =
                bodyAxis;

            conductorContactY =
                conductorAxis;
        }

        if (gap < MinimumBridgeLength ||
            gap > maximumGap)
        {
            return TerminalProposal.Invalid;
        }

        double alignmentTolerance =
            Math.Max(
                options.PinEndpointContainmentTolerancePixels,
                verticalSide
                    ? bodyBounds.Width * 0.22D
                    : bodyBounds.Height * 0.22D);

        if (axisOffset > alignmentTolerance)
        {
            return TerminalProposal.Invalid;
        }

        /*
         * Un terminal superior/inferior suele llegar a un conductor vertical
         * o a una barra horizontal que cruza el eje. Los terminales laterales
         * aplican la regla inversa.
         */
        bool conductorOrientationCompatible =
            verticalSide
                ? conductorBounds.Height >=
                      conductorBounds.Width * 1.20D ||
                  body.CenterX >=
                      conductorBounds.Left -
                      options.PinEndpointContainmentTolerancePixels &&
                  body.CenterX <=
                      conductorBounds.Right +
                      options.PinEndpointContainmentTolerancePixels
                : conductorBounds.Width >=
                      conductorBounds.Height * 1.20D ||
                  body.CenterY >=
                      conductorBounds.Top -
                      options.PinEndpointContainmentTolerancePixels &&
                  body.CenterY <=
                      conductorBounds.Bottom +
                      options.PinEndpointContainmentTolerancePixels;

        if (!conductorOrientationCompatible)
        {
            return TerminalProposal.Invalid;
        }

        double thickness =
            Math.Max(
                1D,
                Math.Min(
                    DefaultThickness,
                    Math.Max(
                        1D,
                        Math.Min(
                            conductorBounds.Width,
                            conductorBounds.Height))));

        BoardGeometryBounds bridgeBounds =
            CreateBridgeBounds(
                bodyContactX,
                bodyContactY,
                conductorContactX,
                conductorContactY,
                thickness);

        if (bridgeBounds.Width <= 0D ||
            bridgeBounds.Height <= 0D)
        {
            return TerminalProposal.Invalid;
        }

        double distanceScore =
            Clamp01(
                1D -
                gap /
                Math.Max(1D, maximumGap));

        double alignmentScore =
            Clamp01(
                1D -
                axisOffset /
                Math.Max(1D, alignmentTolerance));

        double roleScore =
            conductor.Kind switch
            {
                SchematicElectricalNodeKind.Pin => 1D,
                SchematicElectricalNodeKind.Terminal => 0.96D,
                SchematicElectricalNodeKind.Wire => 0.90D,
                SchematicElectricalNodeKind.Junction => 0.86D,
                _ => 0D
            };

        double score =
            Clamp01(
                distanceScore * 0.48D +
                alignmentScore * 0.36D +
                roleScore * 0.16D);

        return new TerminalProposal(
            true,
            bridgeBounds,
            score,
            gap,
            conductor.Id);
    }

    private static BoardGeometryBounds CreateBridgeBounds(
        double firstX,
        double firstY,
        double secondX,
        double secondY,
        double thickness)
    {
        double halfThickness =
            thickness / 2D;

        double left =
            Math.Min(firstX, secondX) -
            halfThickness;

        double top =
            Math.Min(firstY, secondY) -
            halfThickness;

        double right =
            Math.Max(firstX, secondX) +
            halfThickness;

        double bottom =
            Math.Max(firstY, secondY) +
            halfThickness;

        int normalizedLeft =
            Math.Max(
                0,
                (int)Math.Floor(left));

        int normalizedTop =
            Math.Max(
                0,
                (int)Math.Floor(top));

        int normalizedRight =
            Math.Max(
                normalizedLeft + 1,
                (int)Math.Ceiling(right));

        int normalizedBottom =
            Math.Max(
                normalizedTop + 1,
                (int)Math.Ceiling(bottom));

        return new BoardGeometryBounds(
            normalizedLeft,
            normalizedTop,
            normalizedRight - normalizedLeft,
            normalizedBottom - normalizedTop);
    }

    private static double Clamp(
        double value,
        double minimum,
        double maximum)
    {
        return Math.Max(
            minimum,
            Math.Min(
                maximum,
                value));
    }

    private static double Clamp01(
        double value)
    {
        return Clamp(
            value,
            0D,
            1D);
    }

    private enum TerminalSide
    {
        Top = 0,
        Bottom = 1,
        Left = 2,
        Right = 3
    }

    private readonly record struct TerminalProposal(
        bool IsValid,
        BoardGeometryBounds Bounds,
        double Score,
        double Gap,
        int ConductorId)
    {
        public static TerminalProposal Invalid { get; } =
            new(
                false,
                default,
                0D,
                0D,
                -1);
    }
}