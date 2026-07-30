using System.Diagnostics;
using BoardView.Core.Documents;
using BoardView.Core.Recognition;
using BoardView.Recognition.Clustering;
using BoardView.Recognition.Components;
using BoardView.Recognition.Footprints;
using BoardView.SemanticKernel;

namespace BoardView.Recognition;

/// <summary>Orquesta clustering, resolución de footprints y construcción de componentes.</summary>
public sealed class RecognitionEngine : IRecognitionEngine
{
    private readonly PadClusterBuilder clusterBuilder;
    private readonly FootprintSolver footprintSolver;
    private readonly ComponentBuilder componentBuilder;

    public RecognitionEngine()
        : this(new PadClusterBuilder(), new FootprintSolver(), new ComponentBuilder())
    {
    }

    public RecognitionEngine(PadClusterBuilder clusterBuilder, FootprintSolver footprintSolver, ComponentBuilder componentBuilder)
    {
        this.clusterBuilder = clusterBuilder ?? throw new ArgumentNullException(nameof(clusterBuilder));
        this.footprintSolver = footprintSolver ?? throw new ArgumentNullException(nameof(footprintSolver));
        this.componentBuilder = componentBuilder ?? throw new ArgumentNullException(nameof(componentBuilder));
    }

    public RecognitionAnalysis Analyze(
        BoardDocument document,
        RecognitionResult lowLevelRecognition,
        SemanticAnalysisResult semanticAnalysis,
        RecognitionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(lowLevelRecognition);
        ArgumentNullException.ThrowIfNull(semanticAnalysis);
        options ??= new RecognitionOptions();
        options.Validate();
        if (lowLevelRecognition.Pads.Count == 0) return RecognitionAnalysis.Empty;

        Stopwatch stopwatch = Stopwatch.StartNew();
        IReadOnlyList<PadCluster> clusters = clusterBuilder.Build(lowLevelRecognition.Pads, options);
        RecognizedFootprintModel[] footprints = clusters.Select(cluster => footprintSolver.Solve(cluster, options)).ToArray();
        IReadOnlyList<RecognizedComponentModel> components = componentBuilder.Build(document, footprints);
        stopwatch.Stop();
        return new RecognitionAnalysis(clusters, footprints, components, stopwatch.Elapsed);
    }
}
