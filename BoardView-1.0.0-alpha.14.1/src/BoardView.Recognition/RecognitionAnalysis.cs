using BoardView.Recognition.Clustering;
using BoardView.Recognition.Components;
using BoardView.Recognition.Footprints;

namespace BoardView.Recognition;

/// <summary>Resultado inmutable del motor de reconocimiento de alto nivel.</summary>
public sealed class RecognitionAnalysis
{
    public static RecognitionAnalysis Empty { get; } = new([], [], [], TimeSpan.Zero);

    public RecognitionAnalysis(
        IReadOnlyList<PadCluster> clusters,
        IReadOnlyList<RecognizedFootprintModel> footprints,
        IReadOnlyList<RecognizedComponentModel> components,
        TimeSpan elapsed)
    {
        Clusters = clusters ?? throw new ArgumentNullException(nameof(clusters));
        Footprints = footprints ?? throw new ArgumentNullException(nameof(footprints));
        Components = components ?? throw new ArgumentNullException(nameof(components));
        Elapsed = elapsed;
        Counts = footprints.GroupBy(static footprint => footprint.Kind)
            .ToDictionary(static group => group.Key, static group => group.Count());
    }

    public IReadOnlyList<PadCluster> Clusters { get; }
    public IReadOnlyList<RecognizedFootprintModel> Footprints { get; }
    public IReadOnlyList<RecognizedComponentModel> Components { get; }
    public IReadOnlyDictionary<FootprintKind, int> Counts { get; }
    public TimeSpan Elapsed { get; }
    public int Count(FootprintKind kind) => Counts.TryGetValue(kind, out int value) ? value : 0;
    public string Summary => $"{Components.Count:N0} componentes · {Footprints.Count:N0} footprints · {Clusters.Count:N0} clusters";
}
