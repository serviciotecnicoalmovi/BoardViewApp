using BoardView.Core.Documents;
using BoardView.Core.Recognition;

namespace BoardView.SemanticKernel;

/// <summary>Contrato del motor que asigna significado a la geometría normalizada.</summary>
public interface ISemanticKernel
{
    /// <summary>Analiza el documento y el resultado geométrico sin modificar ninguno de ellos.</summary>
    SemanticAnalysisResult Analyze(
        BoardDocument document,
        RecognitionResult recognition,
        SemanticKernelOptions? options = null);
}
