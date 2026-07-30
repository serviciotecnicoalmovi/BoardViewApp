using System.IO;
using System.Text.Json;
using System.Windows;
using BoardView.Core.GeometryDatabase;
using BoardView.Core.Recognition;
using BoardView.SemanticKernel;
using BoardView.Recognition;
using BoardView.Recognition.Footprints;
using Microsoft.Win32;

namespace BoardView.App.Views;

/// <summary>Muestra la geometría completa y las decisiones del motor de reconocimiento.</summary>
public partial class GeometryInspectorWindow : Window
{
    private readonly RecognitionResult recognitionResult;
    private readonly SemanticAnalysisResult semanticAnalysis;
    private readonly RecognitionAnalysis recognitionAnalysis;

    /// <summary>Inicializa el inspector con una instantánea inmutable.</summary>
    public GeometryInspectorWindow(RecognitionResult recognitionResult, SemanticAnalysisResult semanticAnalysis, RecognitionAnalysis recognitionAnalysis)
    {
        this.recognitionResult = recognitionResult ?? throw new ArgumentNullException(nameof(recognitionResult));
        this.semanticAnalysis = semanticAnalysis ?? throw new ArgumentNullException(nameof(semanticAnalysis));
        this.recognitionAnalysis = recognitionAnalysis ?? throw new ArgumentNullException(nameof(recognitionAnalysis));
        InitializeComponent();

        GeometryDatabaseSnapshot database = recognitionResult.GeometryDatabase;
        DatabaseSummaryText.Text = $"Base geométrica: {database.Summary}. Construcción: {database.Elapsed.TotalMilliseconds:F2} ms.";
        RecognitionSummaryText.Text =
            $"Clasificadas: {recognitionResult.GeometryClassification.Primitives.Count:N0} · " +
            $"Candidatos: {recognitionResult.Diagnostics.CandidateCount:N0} · " +
            $"Pads: {recognitionResult.Pads.Count:N0} · Footprints: {recognitionResult.Footprints.Count:N0} · " +
            $"Semántica: {semanticAnalysis.Summary}.";

        TypeCountsGrid.ItemsSource = Enum.GetValues<GeometryDatabasePrimitiveKind>()
            .Select(kind => new TypeCountRow(GetIcon(kind), kind.ToString(), database.Count(kind)))
            .Where(static row => row.Count > 0)
            .OrderByDescending(static row => row.Count)
            .ToArray();
        RejectionCountsGrid.ItemsSource = Enum.GetValues<PadCandidateRejectionReason>()
            .Select(reason => new RejectionCountRow(
                reason.ToString(),
                reason == PadCandidateRejectionReason.None
                    ? recognitionResult.Diagnostics.AcceptedBeforeDeduplication
                    : recognitionResult.Diagnostics.CountRejected(reason)))
            .Where(static row => row.Count > 0)
            .OrderByDescending(static row => row.Count)
            .ToArray();
        EntriesGrid.ItemsSource = database.Entries;
        CandidatesGrid.ItemsSource = recognitionResult.Diagnostics.Candidates;
        SemanticCountsGrid.ItemsSource = Enum.GetValues<PrimitiveSemantic>()
            .Select(semantic => new SemanticCountRow(GetSemanticIcon(semantic), semantic.ToString(), semanticAnalysis.Count(semantic)))
            .Where(static row => row.Count > 0)
            .OrderByDescending(static row => row.Count)
            .ToArray();
        SemanticPrimitivesGrid.ItemsSource = semanticAnalysis.Primitives;
        FootprintCountsGrid.ItemsSource = Enum.GetValues<FootprintKind>()
            .Select(kind => new FootprintCountRow(kind.ToString(), recognitionAnalysis.Count(kind)))
            .Where(static row => row.Count > 0)
            .OrderByDescending(static row => row.Count)
            .ToArray();
        ComponentsGrid.ItemsSource = recognitionAnalysis.Components;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnExportJsonClick(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dialog = new()
        {
            Title = "Exportar diagnóstico geométrico",
            Filter = "JSON (*.json)|*.json|Todos los archivos (*.*)|*.*",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = $"boardview-geometry-{DateTime.Now:yyyyMMdd-HHmmss}.json",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        GeometryDatabaseSnapshot database = recognitionResult.GeometryDatabase;
        var report = new
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Database = new
            {
                database.Summary,
                ConstructionMilliseconds = database.Elapsed.TotalMilliseconds,
                Counts = Enum.GetValues<GeometryDatabasePrimitiveKind>()
                    .ToDictionary(static kind => kind.ToString(), database.Count),
                database.Entries,
            },
            Classification = recognitionResult.GeometryClassification.Primitives,
            Semantic = new
            {
                semanticAnalysis.Summary,
                AnalysisMilliseconds = semanticAnalysis.Elapsed.TotalMilliseconds,
                Counts = Enum.GetValues<PrimitiveSemantic>()
                    .ToDictionary(static semantic => semantic.ToString(), semanticAnalysis.Count),
                semanticAnalysis.Primitives,
            },
            Recognition = new
            {
                recognitionResult.Diagnostics.CandidateCount,
                recognitionResult.Diagnostics.AcceptedBeforeDeduplication,
                Candidates = recognitionResult.Diagnostics.Candidates,
                PadCount = recognitionResult.Pads.Count,
                FootprintCount = recognitionResult.Footprints.Count,
                HighLevel = new { recognitionAnalysis.Summary, recognitionAnalysis.Components, recognitionAnalysis.Footprints },
            },
        };

        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, options));
    }

    private static string GetIcon(GeometryDatabasePrimitiveKind kind) => kind switch
    {
        GeometryDatabasePrimitiveKind.Line => "╱",
        GeometryDatabasePrimitiveKind.Polyline => "⌁",
        GeometryDatabasePrimitiveKind.Bezier => "∿",
        GeometryDatabasePrimitiveKind.Rectangle => "▭",
        GeometryDatabasePrimitiveKind.Ellipse => "○",
        GeometryDatabasePrimitiveKind.Polygon => "⬡",
        GeometryDatabasePrimitiveKind.Arc => "⌒",
        GeometryDatabasePrimitiveKind.Text => "T",
        GeometryDatabasePrimitiveKind.RasterImage => "▧",
        GeometryDatabasePrimitiveKind.Pad => "▣",
        GeometryDatabasePrimitiveKind.Via => "⊙",
        GeometryDatabasePrimitiveKind.DrillHole => "◉",
        GeometryDatabasePrimitiveKind.Track => "━",
        _ => "·",
    };

    private static string GetSemanticIcon(PrimitiveSemantic semantic) => semantic switch
    {
        PrimitiveSemantic.Pad => "▣",
        PrimitiveSemantic.Via => "⊙",
        PrimitiveSemantic.Hole => "◉",
        PrimitiveSemantic.Copper => "━",
        PrimitiveSemantic.ComponentBody => "▭",
        PrimitiveSemantic.Silkscreen => "⌁",
        PrimitiveSemantic.BoardOutline => "⬡",
        PrimitiveSemantic.Mechanical => "⚙",
        PrimitiveSemantic.Text => "T",
        _ => "?",
    };

    private sealed record TypeCountRow(string Icon, string Kind, int Count);
    private sealed record SemanticCountRow(string Icon, string Semantic, int Count);
    private sealed record RejectionCountRow(string Reason, int Count);
    private sealed record FootprintCountRow(string Kind, int Count);
}
