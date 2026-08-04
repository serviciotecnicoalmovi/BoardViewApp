namespace BoardView.Rendering.Recognition;

/// <summary>
/// Une lógicamente las etiquetas que representan la misma red eléctrica.
/// </summary>
/// <remarks>
/// Los conductores asociados a etiquetas idénticas pueden encontrarse en
/// regiones geométricamente separadas de la página. Esta clase crea una cadena
/// semántica entre los nodos NetLabel que comparten exactamente el mismo nombre
/// normalizado.
///
/// No une etiquetas distintas, no interpreta similitud parcial y no crea
/// conexiones basadas en distancia.
/// </remarks>
public sealed class SchematicNetMergeBuilder
{
    private const double SemanticMergeConfidence = 0.96D;

    /// <summary>
    /// Construye las aristas semánticas entre etiquetas de red equivalentes.
    /// </summary>
    public IReadOnlyList<SchematicElectricalEdge> Build(
        IReadOnlyList<SchematicElectricalNode> nodes,
        IReadOnlyList<SchematicElectricalEdge> existingEdges,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(existingEdges);

        SchematicElectricalNode[] labels =
            nodes
                .Where(node =>
                    node.Kind ==
                    SchematicElectricalNodeKind.NetLabel)
                .Where(node =>
                    !string.IsNullOrWhiteSpace(
                        node.SemanticText))
                .OrderBy(node =>
                    node.SemanticText,
                    StringComparer.Ordinal)
                .ThenBy(node => node.Id)
                .ToArray();

        if (labels.Length < 2)
        {
            return Array.Empty<SchematicElectricalEdge>();
        }

        var existingPairs =
            existingEdges
                .Select(edge =>
                    (
                        Math.Min(
                            edge.FirstNodeId,
                            edge.SecondNodeId),
                        Math.Max(
                            edge.FirstNodeId,
                            edge.SecondNodeId)))
                .ToHashSet();

        var result =
            new List<SchematicElectricalEdge>();

        foreach (IGrouping<string, SchematicElectricalNode> group in
                 labels.GroupBy(
                     node => node.SemanticText!,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            SchematicElectricalNode[] ordered =
                group
                    .OrderBy(node => node.Id)
                    .ToArray();

            if (ordered.Length < 2)
            {
                continue;
            }

            /*
             * Una cadena es suficiente para convertir todas las apariciones de
             * la etiqueta en una sola componente conexa. Evita crear una clique
             * de aristas redundantes.
             */
            for (int index = 0;
                 index < ordered.Length - 1;
                 index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                SchematicElectricalNode first =
                    ordered[index];

                SchematicElectricalNode second =
                    ordered[index + 1];

                int firstId =
                    Math.Min(
                        first.Id,
                        second.Id);

                int secondId =
                    Math.Max(
                        first.Id,
                        second.Id);

                if (!existingPairs.Add(
                        (firstId, secondId)))
                {
                    continue;
                }

                double distance =
                    Distance(
                        first.CenterX,
                        first.CenterY,
                        second.CenterX,
                        second.CenterY);

                result.Add(
                    new SchematicElectricalEdge(
                        firstId,
                        secondId,
                        SchematicElectricalEdgeKind.Proximity,
                        SemanticMergeConfidence,
                        distance,
                        (first.CenterX +
                         second.CenterX) / 2D,
                        (first.CenterY +
                         second.CenterY) / 2D));
            }
        }

        return result
            .OrderBy(edge => edge.FirstNodeId)
            .ThenBy(edge => edge.SecondNodeId)
            .ToArray();
    }

    private static double Distance(
        double firstX,
        double firstY,
        double secondX,
        double secondY)
    {
        double deltaX =
            firstX - secondX;

        double deltaY =
            firstY - secondY;

        return Math.Sqrt(
            deltaX * deltaX +
            deltaY * deltaY);
    }
}