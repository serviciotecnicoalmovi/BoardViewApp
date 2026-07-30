using BoardView.Core.Documents;

namespace BoardView.Core.Recognition;

/// <summary>
/// Detecta primitivas electrónicas de bajo nivel sin inferir referencias ni componentes.
/// </summary>
public interface IPadDetectionEngine
{
    /// <summary>Analiza un documento normalizado y devuelve pads, vías, agujeros y footprints.</summary>
    RecognitionResult Analyze(BoardDocument document, PadDetectionOptions? options = null);
}
