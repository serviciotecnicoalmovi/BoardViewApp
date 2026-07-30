using BoardView.Core.Documents;
using BoardView.Core.Recognition;
using BoardView.SemanticKernel;

namespace BoardView.Recognition;

/// <summary>Construye footprints y componentes electrónicos a partir del modelo semántico.</summary>
public interface IRecognitionEngine
{
    /// <summary>Ejecuta el reconocimiento de alto nivel sin modificar el documento de origen.</summary>
    RecognitionAnalysis Analyze(
        BoardDocument document,
        RecognitionResult lowLevelRecognition,
        SemanticAnalysisResult semanticAnalysis,
        RecognitionOptions? options = null);
}
