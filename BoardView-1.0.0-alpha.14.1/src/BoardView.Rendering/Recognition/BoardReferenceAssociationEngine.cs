using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Asocia candidatos textuales de referencia con componentes geométricos
/// mediante proximidad, alineación, confianza y prioridad semántica.
/// </summary>
/// <remarks>
/// Esta clase no ejecuta OCR ni extrae texto del PDF. Recibe candidatos ya
/// normalizados mediante <see cref="BoardReferenceCandidate"/> y utiliza el
/// <see cref="BoardGeometryIndex"/> construido por el pipeline.
///
/// La asociación se ejecuta en dos fases:
///
/// <list type="number">
/// <item>
/// Genera y puntúa las posibles asociaciones de cada candidato.
/// </item>
/// <item>
/// Resuelve conflictos globalmente, priorizando las propuestas con mejor
/// puntuación.
/// </item>
/// </list>
///
/// De forma predeterminada, cada candidato y cada componente participan en una
/// sola asociación aceptada.
/// </remarks>
public sealed class BoardReferenceAssociationEngine
{
    /// <summary>
    /// Asocia referencias utilizando la configuración predeterminada.
    /// </summary>
    public BoardReferenceAssociationResult Associate(
        BoardGeometryIndex geometryIndex,
        IEnumerable<BoardReferenceCandidate> candidates)
    {
        return Associate(
            geometryIndex,
            candidates,
            BoardReferenceAssociationOptions.Default,
            CancellationToken.None);
    }

    /// <summary>
    /// Asocia referencias utilizando la configuración indicada.
    /// </summary>
    public BoardReferenceAssociationResult Associate(
        BoardGeometryIndex geometryIndex,
        IEnumerable<BoardReferenceCandidate> candidates,
        BoardReferenceAssociationOptions options)
    {
        return Associate(
            geometryIndex,
            candidates,
            options,
            CancellationToken.None);
    }

    /// <summary>
    /// Asocia referencias con componentes geométricos.
    /// </summary>
    /// <param name="geometryIndex">
    /// Índice geométrico correspondiente al render original.
    /// </param>
    /// <param name="candidates">
    /// Candidatos textuales producidos por un extractor de texto u OCR.
    /// </param>
    /// <param name="options">
    /// Umbrales y pesos utilizados por el motor.
    /// </param>
    /// <param name="cancellationToken">
    /// Token de cancelación de la operación.
    /// </param>
    public BoardReferenceAssociationResult Associate(
        BoardGeometryIndex geometryIndex,
        IEnumerable<BoardReferenceCandidate> candidates,
        BoardReferenceAssociationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            geometryIndex);

        ArgumentNullException.ThrowIfNull(
            candidates);

        ArgumentNullException.ThrowIfNull(
            options);

        options.Validate();

        cancellationToken.ThrowIfCancellationRequested();

        BoardReferenceCandidate[] candidateArray =
            candidates
                .OrderBy(candidate => candidate.Id)
                .ToArray();

        ValidateCandidateIdentifiers(
            candidateArray);

        if (candidateArray.Length == 0 ||
            geometryIndex.Count == 0)
        {
            return new BoardReferenceAssociationResult(
                candidateArray,
                Array.Empty<BoardReferenceAssociation>());
        }

        IReadOnlySet<BoardGeometryComponentType> excludedTypes =
            CreateExcludedTypes(
                options);

        var queryOptions =
            new BoardGeometryIndexQueryOptions
            {
                MinimumConfidence =
                    options.MinimumComponentConfidence,

                AllowedTypes =
                    options.AllowedComponentTypes,

                ExcludedTypes =
                    excludedTypes
            };

        var proposals =
            new List<AssociationProposal>();

        foreach (BoardReferenceCandidate candidate in candidateArray)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!CanEvaluateCandidate(
                    candidate,
                    geometryIndex,
                    options))
            {
                continue;
            }

            IReadOnlyList<BoardGeometryIndexedComponent> nearbyComponents =
                geometryIndex.QueryNearest(
                    candidate.CenterX,
                    candidate.CenterY,
                    options.MaximumDistancePixels,
                    options.MaximumComponentsPerCandidate,
                    queryOptions);

            foreach (BoardGeometryIndexedComponent component
                     in nearbyComponents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AssociationProposal proposal =
                    CreateProposal(
                        candidate,
                        component,
                        options);

                if (proposal.Score <
                    options.MinimumAssociationScore)
                {
                    continue;
                }

                proposals.Add(
                    proposal);
            }
        }

        IReadOnlyList<BoardReferenceAssociation> associations =
            ResolveProposals(
                proposals,
                options,
                cancellationToken);

        return new BoardReferenceAssociationResult(
            candidateArray,
            associations);
    }

    /// <summary>
    /// Determina si un candidato puede evaluarse dentro del índice actual.
    /// </summary>
    private static bool CanEvaluateCandidate(
        BoardReferenceCandidate candidate,
        BoardGeometryIndex geometryIndex,
        BoardReferenceAssociationOptions options)
    {
        if (!candidate.IsReferenceLike)
        {
            return false;
        }

        if (candidate.Confidence <
            options.MinimumCandidateConfidence)
        {
            return false;
        }

        return candidate.CenterX >= 0D &&
               candidate.CenterY >= 0D &&
               candidate.CenterX <= geometryIndex.PageWidth &&
               candidate.CenterY <= geometryIndex.PageHeight;
    }

    /// <summary>
    /// Construye una propuesta puntuada para un par candidato-componente.
    /// </summary>
    private static AssociationProposal CreateProposal(
        BoardReferenceCandidate candidate,
        BoardGeometryIndexedComponent component,
        BoardReferenceAssociationOptions options)
    {
        double horizontalDistance =
            Math.Abs(
                candidate.CenterX -
                component.CenterX);

        double verticalDistance =
            Math.Abs(
                candidate.CenterY -
                component.CenterY);

        double centerDistance =
            Math.Sqrt(
                (horizontalDistance * horizontalDistance) +
                (verticalDistance * verticalDistance));

        double boundsDistance =
            DistanceBetweenBounds(
                candidate.Bounds,
                component.Bounds);

        double effectiveDistance =
            Math.Min(
                centerDistance,
                boundsDistance);

        double distanceScore =
            Clamp01(
                1D -
                (effectiveDistance /
                 options.MaximumDistancePixels));

        double horizontalAlignment =
            CalculateAxisAlignment(
                candidate.CenterY,
                component.CenterY,
                candidate.Bounds.Height,
                component.Bounds.Height,
                options.MaximumDistancePixels);

        double verticalAlignment =
            CalculateAxisAlignment(
                candidate.CenterX,
                component.CenterX,
                candidate.Bounds.Width,
                component.Bounds.Width,
                options.MaximumDistancePixels);

        bool intersects =
            Intersects(
                candidate.Bounds,
                component.Bounds);

        double overlapRatio =
            CalculateOverlapRatio(
                candidate.Bounds,
                component.Bounds);

        double textCapturePenalty =
            CalculateTextCapturePenalty(
                candidate.Bounds,
                component.Bounds,
                overlapRatio);

        /*
         * Una intersección ya no obtiene automáticamente puntuación perfecta.
         * Cuando la geometría está casi completamente contenida dentro del
         * texto de la referencia, normalmente representa un fragmento del
         * glifo y no el símbolo electrónico.
         */
        double intersectionScore =
            intersects
                ? Clamp01(
                    0.72D +
                    (0.28D * overlapRatio) -
                    textCapturePenalty)
                : 0D;

        double alignmentScore =
            intersects
                ? Math.Max(
                    intersectionScore,
                    Math.Max(
                        horizontalAlignment,
                        verticalAlignment))
                : Math.Max(
                    horizontalAlignment,
                    verticalAlignment);

        double typeSemanticScore =
            GetSemanticPriority(
                candidate.NormalizedReference,
                component.Type);

        double sizeCompatibilityScore =
            CalculateSizeCompatibility(
                candidate.Bounds,
                component.Bounds);

        double directionScore =
            CalculateReferenceDirectionScore(
                candidate,
                component);

        /*
         * El valor semántico combina:
         *
         *  - compatibilidad entre prefijo y tipo geométrico;
         *  - escala esperada del símbolo frente al texto;
         *  - posición habitual del símbolo respecto a su referencia;
         *  - penalización de fragmentos capturados dentro del texto.
         */
        double semanticScore =
            Clamp01(
                (typeSemanticScore * 0.55D) +
                (sizeCompatibilityScore * 0.25D) +
                (directionScore * 0.20D) -
                textCapturePenalty);

        double weightedScore =
            (distanceScore *
             options.DistanceWeight) +
            (alignmentScore *
             options.AlignmentWeight) +
            (candidate.Confidence *
             options.CandidateConfidenceWeight) +
            (component.Confidence *
             options.ComponentConfidenceWeight) +
            (semanticScore *
             options.SemanticWeight);

        double totalWeight =
            options.DistanceWeight +
            options.AlignmentWeight +
            options.CandidateConfidenceWeight +
            options.ComponentConfidenceWeight +
            options.SemanticWeight;

        double normalizedScore =
            totalWeight <= 0D
                ? 0D
                : Clamp01(
                    weightedScore /
                    totalWeight);

        /*
         * Penalización final fuerte para geometrías que parecen formar parte
         * del propio texto. Este ajuste evita que "C3700" termine asociado al
         * pequeño contorno de los dígitos en lugar del condensador próximo.
         */
        normalizedScore =
            Clamp01(
                normalizedScore -
                (textCapturePenalty * 0.35D));

        BoardReferenceAssociationRule rule =
            ResolveRule(
                intersects &&
                textCapturePenalty < 0.25D,
                horizontalAlignment,
                verticalAlignment,
                semanticScore,
                distanceScore);

        return new AssociationProposal(
            candidate,
            component,
            normalizedScore,
            effectiveDistance,
            rule);
    }

    /// <summary>
    /// Calcula la proporción de intersección respecto al área más pequeña.
    /// </summary>
    private static double CalculateOverlapRatio(
        BoardGeometryBounds first,
        BoardGeometryBounds second)
    {
        double left =
            Math.Max(
                first.Left,
                second.Left);

        double top =
            Math.Max(
                first.Top,
                second.Top);

        double right =
            Math.Min(
                first.Right,
                second.Right);

        double bottom =
            Math.Min(
                first.Bottom,
                second.Bottom);

        if (right <= left ||
            bottom <= top)
        {
            return 0D;
        }

        double intersectionArea =
            (right - left) *
            (bottom - top);

        double firstArea =
            Math.Max(
                1D,
                first.Width *
                first.Height);

        double secondArea =
            Math.Max(
                1D,
                second.Width *
                second.Height);

        return Clamp01(
            intersectionArea /
            Math.Min(
                firstArea,
                secondArea));
    }

    /// <summary>
    /// Penaliza componentes pequeños capturados dentro del rectángulo textual.
    /// </summary>
    private static double CalculateTextCapturePenalty(
        BoardGeometryBounds candidateBounds,
        BoardGeometryBounds componentBounds,
        double overlapRatio)
    {
        double candidateArea =
            Math.Max(
                1D,
                candidateBounds.Width *
                candidateBounds.Height);

        double componentArea =
            Math.Max(
                1D,
                componentBounds.Width *
                componentBounds.Height);

        double areaRatio =
            componentArea /
            candidateArea;

        double componentCenterX =
            componentBounds.Left +
            (componentBounds.Width / 2D);

        double componentCenterY =
            componentBounds.Top +
            (componentBounds.Height / 2D);

        bool componentCenterInsideText =
            componentCenterX >= candidateBounds.Left &&
            componentCenterX <= candidateBounds.Right &&
            componentCenterY >= candidateBounds.Top &&
            componentCenterY <= candidateBounds.Bottom;

        if (!componentCenterInsideText ||
            overlapRatio < 0.70D)
        {
            return 0D;
        }

        if (areaRatio <= 0.35D)
        {
            return 0.95D;
        }

        if (areaRatio <= 0.75D)
        {
            return 0.70D;
        }

        if (areaRatio <= 1.25D)
        {
            return 0.45D;
        }

        return 0.15D;
    }

    /// <summary>
    /// Puntúa si la escala de la geometría es razonable para un símbolo
    /// electrónico asociado a una etiqueta textual.
    /// </summary>
    private static double CalculateSizeCompatibility(
        BoardGeometryBounds candidateBounds,
        BoardGeometryBounds componentBounds)
    {
        double candidateArea =
            Math.Max(
                1D,
                candidateBounds.Width *
                candidateBounds.Height);

        double componentArea =
            Math.Max(
                1D,
                componentBounds.Width *
                componentBounds.Height);

        double ratio =
            componentArea /
            candidateArea;

        if (ratio < 0.25D)
        {
            return 0.05D;
        }

        if (ratio < 0.75D)
        {
            return 0.25D;
        }

        if (ratio < 1.50D)
        {
            return 0.55D;
        }

        if (ratio <= 20D)
        {
            return 1D;
        }

        if (ratio <= 80D)
        {
            return 0.70D;
        }

        return 0.30D;
    }

    /// <summary>
    /// Favorece la disposición habitual de los esquemáticos: la referencia
    /// suele estar encima o al lado del símbolo, no dentro de sus trazos.
    /// </summary>
    private static double CalculateReferenceDirectionScore(
        BoardReferenceCandidate candidate,
        BoardGeometryIndexedComponent component)
    {
        double deltaX =
            component.CenterX -
            candidate.CenterX;

        double deltaY =
            component.CenterY -
            candidate.CenterY;

        double candidateHeight =
            Math.Max(
                1D,
                candidate.Bounds.Height);

        double candidateWidth =
            Math.Max(
                1D,
                candidate.Bounds.Width);

        bool belowReference =
            deltaY >=
            candidateHeight * 0.20D;

        bool besideReference =
            Math.Abs(deltaX) >=
            candidateWidth * 0.35D;

        if (belowReference)
        {
            return 1D;
        }

        if (besideReference)
        {
            return 0.80D;
        }

        return 0.35D;
    }

    /// <summary>
    /// Devuelve la prioridad semántica combinando el prefijo eléctrico y el
    /// tipo de geometría detectado.
    /// </summary>
    private static double GetSemanticPriority(
        string normalizedReference,
        BoardGeometryComponentType type)
    {
        string prefix =
            GetReferencePrefix(
                normalizedReference);

        /*
         * Puntos de prueba y pads explícitos sí deben favorecer geometrías Pad.
         */
        bool padOrTestReference =
            prefix is "TP" or "PP" or "P";

        if (padOrTestReference)
        {
            return type switch
            {
                BoardGeometryComponentType.Pad => 1.00D,
                BoardGeometryComponentType.ComponentBody => 0.88D,
                BoardGeometryComponentType.Copper => 0.76D,
                BoardGeometryComponentType.Unknown => 0.58D,
                BoardGeometryComponentType.Hole => 0.45D,
                BoardGeometryComponentType.BoardOutline => 0.10D,
                BoardGeometryComponentType.Text => 0.04D,
                BoardGeometryComponentType.Silkscreen => 0.03D,
                BoardGeometryComponentType.Noise => 0D,
                _ => 0D
            };
        }

        /*
         * Referencias de componentes electrónicos deben favorecer cuerpos,
         * símbolos o geometrías aún no clasificadas antes que pads diminutos.
         */
        bool componentReference =
            prefix is
                "C" or "R" or "L" or "D" or "Q" or "U" or
                "J" or "F" or "Y" or "X" or "B" or "K" or
                "T" or "SW" or "LED";

        if (componentReference)
        {
            return type switch
            {
                BoardGeometryComponentType.ComponentBody => 1.00D,
                BoardGeometryComponentType.Unknown => 0.88D,
                BoardGeometryComponentType.Copper => 0.78D,
                BoardGeometryComponentType.Hole => 0.44D,
                BoardGeometryComponentType.Pad => 0.32D,
                BoardGeometryComponentType.BoardOutline => 0.10D,
                BoardGeometryComponentType.Text => 0.03D,
                BoardGeometryComponentType.Silkscreen => 0.02D,
                BoardGeometryComponentType.Noise => 0D,
                _ => 0D
            };
        }

        return type switch
        {
            BoardGeometryComponentType.ComponentBody => 1.00D,
            BoardGeometryComponentType.Unknown => 0.76D,
            BoardGeometryComponentType.Copper => 0.70D,
            BoardGeometryComponentType.Pad => 0.62D,
            BoardGeometryComponentType.Hole => 0.48D,
            BoardGeometryComponentType.BoardOutline => 0.15D,
            BoardGeometryComponentType.Text => 0.05D,
            BoardGeometryComponentType.Silkscreen => 0.04D,
            BoardGeometryComponentType.Noise => 0D,
            _ => 0D
        };
    }

    /// <summary>
    /// Extrae el prefijo alfabético normalizado de una referencia.
    /// </summary>
    private static string GetReferencePrefix(
        string normalizedReference)
    {
        if (string.IsNullOrWhiteSpace(
                normalizedReference))
        {
            return string.Empty;
        }

        return new string(
            normalizedReference
                .TakeWhile(
                    static character =>
                        char.IsLetter(character))
                .ToArray());
    }

    /// <summary>
    /// Resuelve conflictos entre propuestas utilizando un orden global.
    /// </summary>
    private static IReadOnlyList<BoardReferenceAssociation> ResolveProposals(
        IEnumerable<AssociationProposal> proposals,
        BoardReferenceAssociationOptions options,
        CancellationToken cancellationToken)
    {
        AssociationProposal[] ordered =
            proposals
                .OrderByDescending(proposal =>
                    proposal.Score)
                .ThenBy(proposal =>
                    proposal.DistancePixels)
                .ThenByDescending(proposal =>
                    proposal.Candidate.Confidence)
                .ThenByDescending(proposal =>
                    proposal.Component.Confidence)
                .ThenBy(proposal =>
                    proposal.Candidate.Id)
                .ThenBy(proposal =>
                    proposal.Component.Id)
                .ToArray();

        var usedCandidateIds =
            new HashSet<int>();

        var usedComponentIds =
            new HashSet<int>();

        var associations =
            new List<BoardReferenceAssociation>();

        foreach (AssociationProposal proposal in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!usedCandidateIds.Add(
                    proposal.Candidate.Id))
            {
                continue;
            }

            if (!options.AllowMultipleReferencesPerComponent &&
                !usedComponentIds.Add(
                    proposal.Component.Id))
            {
                usedCandidateIds.Remove(
                    proposal.Candidate.Id);

                continue;
            }

            if (options.AllowMultipleReferencesPerComponent)
            {
                usedComponentIds.Add(
                    proposal.Component.Id);
            }

            associations.Add(
                new BoardReferenceAssociation(
                    proposal.Candidate,
                    proposal.Component,
                    proposal.Score,
                    proposal.DistancePixels,
                    proposal.Rule));
        }

        return associations;
    }

    /// <summary>
    /// Crea el conjunto de tipos excluidos de las consultas espaciales.
    /// </summary>
    private static IReadOnlySet<BoardGeometryComponentType> CreateExcludedTypes(
        BoardReferenceAssociationOptions options)
    {
        var excluded =
            new HashSet<BoardGeometryComponentType>();

        if (options.ExcludeNoise)
        {
            excluded.Add(
                BoardGeometryComponentType.Noise);
        }

        if (options.ExcludeTextLikeComponents)
        {
            excluded.Add(
                BoardGeometryComponentType.Text);

            excluded.Add(
                BoardGeometryComponentType.Silkscreen);
        }

        return excluded;
    }

    /// <summary>
    /// Calcula la alineación de dos centros sobre un eje.
    /// </summary>
    private static double CalculateAxisAlignment(
        double firstCenter,
        double secondCenter,
        double firstSize,
        double secondSize,
        double maximumDistance)
    {
        double tolerance =
            Math.Max(
                1D,
                Math.Max(
                    firstSize,
                    secondSize));

        double difference =
            Math.Abs(
                firstCenter -
                secondCenter);

        if (difference <= tolerance)
        {
            return 1D;
        }

        double normalizedDifference =
            (difference - tolerance) /
            Math.Max(
                1D,
                maximumDistance - tolerance);

        return Clamp01(
            1D -
            normalizedDifference);
    }

    /// <summary>
    /// Determina la regla que mejor describe la propuesta.
    /// </summary>
    private static BoardReferenceAssociationRule ResolveRule(
        bool intersects,
        double horizontalAlignment,
        double verticalAlignment,
        double semanticScore,
        double distanceScore)
    {
        if (intersects)
        {
            return BoardReferenceAssociationRule.BoundsIntersection;
        }

        if (horizontalAlignment >= 0.90D &&
            horizontalAlignment >= verticalAlignment)
        {
            return BoardReferenceAssociationRule.HorizontalAlignment;
        }

        if (verticalAlignment >= 0.90D)
        {
            return BoardReferenceAssociationRule.VerticalAlignment;
        }

        if (semanticScore >= 0.90D &&
            semanticScore >= distanceScore)
        {
            return BoardReferenceAssociationRule.SemanticPriority;
        }

        return BoardReferenceAssociationRule.NearestComponent;
    }

    /// <summary>
    /// Calcula la distancia mínima entre dos rectángulos.
    /// </summary>
    private static double DistanceBetweenBounds(
        BoardGeometryBounds first,
        BoardGeometryBounds second)
    {
        double horizontalDistance =
            first.Right < second.Left
                ? second.Left - first.Right
                : second.Right < first.Left
                    ? first.Left - second.Right
                    : 0D;

        double verticalDistance =
            first.Bottom < second.Top
                ? second.Top - first.Bottom
                : second.Bottom < first.Top
                    ? first.Top - second.Bottom
                    : 0D;

        return Math.Sqrt(
            (horizontalDistance * horizontalDistance) +
            (verticalDistance * verticalDistance));
    }

    /// <summary>
    /// Determina si dos rectángulos se intersectan.
    /// </summary>
    private static bool Intersects(
        BoardGeometryBounds first,
        BoardGeometryBounds second)
    {
        return first.Left < second.Right &&
               first.Right > second.Left &&
               first.Top < second.Bottom &&
               first.Bottom > second.Top;
    }

    /// <summary>
    /// Valida que los identificadores de candidatos sean únicos.
    /// </summary>
    private static void ValidateCandidateIdentifiers(
        IReadOnlyList<BoardReferenceCandidate> candidates)
    {
        int uniqueCount =
            candidates
                .Select(candidate => candidate.Id)
                .Distinct()
                .Count();

        if (uniqueCount != candidates.Count)
        {
            throw new ArgumentException(
                "Los identificadores de candidatos deben ser únicos.",
                nameof(candidates));
        }
    }

    private static double Clamp01(
        double value)
    {
        return Math.Max(
            0D,
            Math.Min(
                1D,
                value));
    }

    /// <summary>
    /// Propuesta interna pendiente de resolución global.
    /// </summary>
    private readonly record struct AssociationProposal(
        BoardReferenceCandidate Candidate,
        BoardGeometryIndexedComponent Component,
        double Score,
        double DistancePixels,
        BoardReferenceAssociationRule Rule);
}
