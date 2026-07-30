using BoardView.Core.Documents;

namespace BoardView.Core.Validation;

/// <summary>Aplica reglas deterministas de integridad al modelo interno.</summary>
public sealed class BoardDocumentValidator
{
    public BoardValidationResult Validate(BoardDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<BoardValidationIssue> issues = [];

        if (document.Layers.Count == 0)
        {
            issues.Add(new(BoardValidationSeverity.Error, "CORE001", "El documento no contiene capas."));
        }

        foreach (var page in document.Pages)
        {
            if (page.Bounds.IsEmpty)
            {
                issues.Add(new(BoardValidationSeverity.Error, "CORE006", "La página tiene límites vacíos.", page.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            foreach (string layerId in page.LayerIds)
            {
                if (!document.TryGetLayer(layerId, out _))
                {
                    issues.Add(new(BoardValidationSeverity.Error, "CORE007", "La página referencia una capa inexistente.", page.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }
            }
        }

        foreach (var element in document.Elements)
        {
            if (element.Bounds.IsEmpty)
            {
                issues.Add(new(BoardValidationSeverity.Warning, "CORE002", "El elemento tiene límites vacíos.", element.Id));
            }

            if (!document.TryGetLayer(element.LayerId, out _))
            {
                issues.Add(new(BoardValidationSeverity.Error, "CORE003", "El elemento referencia una capa inexistente.", element.Id));
            }

            if (element.NetId is not null && !document.TryGetNet(element.NetId, out _))
            {
                issues.Add(new(BoardValidationSeverity.Error, "CORE004", "El elemento referencia una red inexistente.", element.Id));
            }
        }

        foreach (var component in document.Components)
        {
            foreach (string elementId in component.ElementIds)
            {
                if (!document.TryGetElement(elementId, out _))
                {
                    issues.Add(new(BoardValidationSeverity.Error, "CORE005", "El componente referencia un elemento inexistente.", component.Id));
                }
            }
        }

        return new BoardValidationResult(issues);
    }
}
