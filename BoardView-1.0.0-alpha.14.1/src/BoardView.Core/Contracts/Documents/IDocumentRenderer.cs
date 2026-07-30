using BoardView.Core.Documents.Common;

namespace BoardView.Core.Contracts.Documents;

/// <summary>
/// Contrato agnóstico de UI para renderizadores. El contexto concreto pertenece al módulo de renderizado.
/// </summary>
public interface IDocumentRenderer<in TContext>
{
    void Render(TechnicalDocument document, TContext context);
}
