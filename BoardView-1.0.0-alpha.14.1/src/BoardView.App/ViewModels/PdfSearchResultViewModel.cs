namespace BoardView.App.ViewModels;

/// <summary>Resultado de búsqueda dentro del índice técnico de un PDF.</summary>
public sealed record PdfSearchResultViewModel(int PageNumber, string Preview)
{
    public string DisplayText => $"Página {PageNumber}: {Preview}";
}
