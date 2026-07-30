using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using BoardView.App.Services;
using BoardView.Core.Contracts;
using BoardView.Core.Pdf;
using BoardView.Core.Repair;

namespace BoardView.App.ViewModels.Repair;

/// <summary>Coordina documentos, búsquedas y notas de una sesión de reparación.</summary>
public sealed class RepairWorkspaceViewModel : ObservableObject
{
    private readonly IFileDialogService fileDialogService;
    private readonly IRepairWorkspaceStore workspaceStore;
    private readonly IApplicationLogger logger;
    private readonly ISafePdfDocumentIndexer safePdfDocumentIndexer;
    private readonly PdfReferenceSearchService referenceSearchService;
    private RepairWorkspaceProject project = new();
    private PdfDocumentIndex? boardIndex;
    private PdfDocumentIndex? schematicIndex;
    private string? projectFilePath;
    private string referenceQuery = string.Empty;
    private string activeReference = string.Empty;
    private string annotationTitle = string.Empty;
    private string annotationNotes = string.Empty;
    private RepairStatus selectedStatus = RepairStatus.Review;
    private int boardPage = 1;
    private int schematicPage = 1;
    private int boardMatchPage;
    private int schematicMatchPage;
    private int boardOccurrenceCount;
    private int schematicOccurrenceCount;
    private string crossProbeStatus = "Sin referencia seleccionada.";
    private string statusMessage = "Seleccione una placa y un esquemático.";
    private bool isBusy;
    private RepairSearchResultViewModel? selectedSearchResult;

    public RepairWorkspaceViewModel(
        IFileDialogService fileDialogService,
        IRepairWorkspaceStore workspaceStore,
        IApplicationLogger logger,
        ISafePdfDocumentIndexer safePdfDocumentIndexer,
        PdfReferenceSearchService referenceSearchService)
    {
        this.fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        this.workspaceStore = workspaceStore ?? throw new ArgumentNullException(nameof(workspaceStore));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.safePdfDocumentIndexer = safePdfDocumentIndexer ?? throw new ArgumentNullException(nameof(safePdfDocumentIndexer));
        this.referenceSearchService = referenceSearchService ?? throw new ArgumentNullException(nameof(referenceSearchService));

        OpenBoardCommand = new RelayCommand(() => _ = OpenBoardAsync());
        OpenSchematicCommand = new RelayCommand(() => _ = OpenSchematicAsync());
        SearchCommand = new RelayCommand(Search, CanSearch);
        AddAnnotationCommand = new RelayCommand(AddAnnotation, CanAddAnnotation);
        SaveCommand = new RelayCommand(Save, () => !IsBusy);
        SaveAsCommand = new RelayCommand(SaveAs, () => !IsBusy);
        OpenProjectCommand = new RelayCommand(() => _ = OpenProjectAsync());
        RemoveAnnotationCommand = new RelayCommand(RemoveSelectedAnnotation, () => SelectedAnnotation is not null);

        SearchResultsView = CollectionViewSource.GetDefaultView(SearchResults);
        SearchResultsView.SortDescriptions.Add(
            new SortDescription(nameof(RepairSearchResultViewModel.DocumentOrder), ListSortDirection.Ascending));
        SearchResultsView.SortDescriptions.Add(
            new SortDescription(nameof(RepairSearchResultViewModel.PageNumber), ListSortDirection.Ascending));
        SearchResultsView.GroupDescriptions.Add(
            new PropertyGroupDescription(nameof(RepairSearchResultViewModel.DocumentRole)));

        NewProject();
    }

    public ICommand OpenBoardCommand { get; }
    public ICommand OpenSchematicCommand { get; }
    public RelayCommand SearchCommand { get; }
    public RelayCommand AddAnnotationCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public RelayCommand RemoveAnnotationCommand { get; }
    public ObservableCollection<RepairSearchResultViewModel> SearchResults { get; } = [];
    public ICollectionView SearchResultsView { get; }
    public ObservableCollection<RepairAnnotationViewModel> Annotations { get; } = [];
    public IReadOnlyList<RepairStatus> AvailableStatuses { get; } = Enum.GetValues<RepairStatus>();

    public string? BoardFilePath
    {
        get => project.BoardFilePath;
        private set { if (project.BoardFilePath != value) { project.BoardFilePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(BoardFileName)); } }
    }

    public string? SchematicFilePath
    {
        get => project.SchematicFilePath;
        private set { if (project.SchematicFilePath != value) { project.SchematicFilePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(SchematicFileName)); } }
    }

    public string BoardFileName => string.IsNullOrWhiteSpace(BoardFilePath) ? "Placa no seleccionada" : Path.GetFileName(BoardFilePath);
    public string SchematicFileName => string.IsNullOrWhiteSpace(SchematicFilePath) ? "Esquemático no seleccionado" : Path.GetFileName(SchematicFilePath);

    public string ReferenceQuery
    {
        get => referenceQuery;
        set { if (SetProperty(ref referenceQuery, value)) { SearchCommand.RaiseCanExecuteChanged(); AddAnnotationCommand.RaiseCanExecuteChanged(); } }
    }

    /// <summary>Referencia activa que ambos visores deben resaltar.</summary>
    public string ActiveReference
    {
        get => activeReference;
        private set => SetProperty(ref activeReference, value);
    }

    public string AnnotationTitle { get => annotationTitle; set => SetProperty(ref annotationTitle, value); }
    public string AnnotationNotes { get => annotationNotes; set => SetProperty(ref annotationNotes, value); }
    public RepairStatus SelectedStatus { get => selectedStatus; set => SetProperty(ref selectedStatus, value); }

    public int BoardPage
    {
        get => boardPage;
        set { int normalized = Math.Max(1, value); if (SetProperty(ref boardPage, normalized)) { project.BoardPage = normalized; } }
    }

    public int SchematicPage
    {
        get => schematicPage;
        set { int normalized = Math.Max(1, value); if (SetProperty(ref schematicPage, normalized)) { project.SchematicPage = normalized; } }
    }

    public int BoardMatchPage
    {
        get => boardMatchPage;
        private set => SetProperty(ref boardMatchPage, value);
    }

    public int SchematicMatchPage
    {
        get => schematicMatchPage;
        private set => SetProperty(ref schematicMatchPage, value);
    }

    public int BoardOccurrenceCount
    {
        get => boardOccurrenceCount;
        private set => SetProperty(ref boardOccurrenceCount, value);
    }

    public int SchematicOccurrenceCount
    {
        get => schematicOccurrenceCount;
        private set => SetProperty(ref schematicOccurrenceCount, value);
    }

    public bool IsFoundOnBoard => BoardMatchPage > 0;
    public bool IsFoundOnSchematic => SchematicMatchPage > 0;

    public string BoardLocationText =>
        IsFoundOnBoard
            ? $"Página {BoardMatchPage} · {BoardOccurrenceCount} coincidencia(s)"
            : "No encontrada";

    public string SchematicLocationText =>
        IsFoundOnSchematic
            ? $"Página {SchematicMatchPage} · {SchematicOccurrenceCount} coincidencia(s)"
            : "No encontrada";

    public string CrossProbeStatus
    {
        get => crossProbeStatus;
        private set => SetProperty(ref crossProbeStatus, value);
    }

    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                SearchCommand.RaiseCanExecuteChanged();
                SaveCommand.RaiseCanExecuteChanged();
                SaveAsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RepairSearchResultViewModel? SelectedSearchResult
    {
        get => selectedSearchResult;
        set
        {
            if (SetProperty(ref selectedSearchResult, value) && value is not null)
            {
                SynchronizeReference(value.Reference, value);
                AddHistory("Navegación sincronizada", value.Reference);
            }
        }
    }

    private RepairAnnotationViewModel? selectedAnnotation;
    public RepairAnnotationViewModel? SelectedAnnotation
    {
        get => selectedAnnotation;
        set { if (SetProperty(ref selectedAnnotation, value)) RemoveAnnotationCommand.RaiseCanExecuteChanged(); }
    }

    private void NewProject()
    {
        project = new RepairWorkspaceProject();
        boardIndex = null;
        schematicIndex = null;
        projectFilePath = null;
        BoardPage = 1;
        SchematicPage = 1;
        SearchResults.Clear();
        Annotations.Clear();
        ClearCrossProbe();
    }

    private async Task OpenBoardAsync()
    {
        string? path = fileDialogService.SelectPdfFile("Seleccionar PDF de placa", GetDirectory(BoardFilePath));
        if (path is null) return;
        BoardFilePath = Path.GetFullPath(path);
        boardIndex = await BuildIndexAsync(BoardFilePath, "placa");
        SearchCommand.RaiseCanExecuteChanged();
    }

    private async Task OpenSchematicAsync()
    {
        string? path = fileDialogService.SelectPdfFile("Seleccionar PDF de esquemático", GetDirectory(SchematicFilePath));
        if (path is null) return;
        SchematicFilePath = Path.GetFullPath(path);
        schematicIndex = await BuildIndexAsync(SchematicFilePath, "esquemático");
        SearchCommand.RaiseCanExecuteChanged();
    }

    private async Task<PdfDocumentIndex?> BuildIndexAsync(string path, string role)
    {
        IsBusy = true;
        StatusMessage = $"Indexando {role}: {Path.GetFileName(path)}...";

        try
        {
            SafePdfIndexResult result = await safePdfDocumentIndexer.BuildIndexAsync(path);
            string warningText = result.Warnings.Count == 0
                ? string.Empty
                : $" · {result.Warnings.Count} advertencia(s)";
            string modeText = " · PDFium";

            StatusMessage = result.HasSearchableText
                ? $"{role} indexado: {result.Index.WordCount:N0} palabras en " +
                  $"{result.IndexedPageCount:N0}/{result.Index.PageCount:N0} páginas{modeText}{warningText}."
                : $"{role} visible, pero sin texto indexable{modeText}{warningText}.";

            foreach (string warning in result.Warnings)
            {
                logger.Warning($"Indexación PDF ({role}): {warning}");
            }

            AddHistory($"Documento {role} indexado", string.Empty);
            return result.Index;
        }
        catch (Exception exception)
        {
            logger.Error($"No se pudo indexar el PDF de {role}.", exception);
            StatusMessage = $"{role} visible. La búsqueda no está disponible para este archivo: {exception.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
            SearchCommand.RaiseCanExecuteChanged();
        }
    }

    private bool CanSearch() => !IsBusy && !string.IsNullOrWhiteSpace(ReferenceQuery) && (boardIndex is not null || schematicIndex is not null);

    private void Search()
    {
        string term = ReferenceQuery.Trim();
        IReadOnlyList<PdfReferenceMatch> boardMatches =
            referenceSearchService.Search(boardIndex, term);
        IReadOnlyList<PdfReferenceMatch> schematicMatches =
            referenceSearchService.Search(schematicIndex, term);

        SearchResults.Clear();
        AddMatches(boardMatches, "Placa", term);
        AddMatches(schematicMatches, "Esquemático", term);
        SearchResultsView.Refresh();

        project.LastReference = term;
        UpdateCrossProbeSummary(term, boardMatches, schematicMatches);

        if (boardMatches.Count > 0)
        {
            BoardPage = boardMatches[0].PageNumber;
        }

        if (schematicMatches.Count > 0)
        {
            SchematicPage = schematicMatches[0].PageNumber;
        }

        ActiveReference = term;
        StatusMessage = SearchResults.Count == 0
            ? $"Sin coincidencias para {term}."
            : $"{term}: {boardMatches.Count} página(s) en placa y " +
              $"{schematicMatches.Count} página(s) en esquema.";

        AddHistory("Búsqueda sincronizada", term);
    }

    private void SynchronizeReference(
        string reference,
        RepairSearchResultViewModel selectedResult)
    {
        IReadOnlyList<PdfReferenceMatch> boardMatches =
            referenceSearchService.Search(boardIndex, reference);
        IReadOnlyList<PdfReferenceMatch> schematicMatches =
            referenceSearchService.Search(schematicIndex, reference);

        UpdateCrossProbeSummary(reference, boardMatches, schematicMatches);

        if (string.Equals(selectedResult.DocumentRole, "Placa", StringComparison.Ordinal))
        {
            BoardPage = selectedResult.PageNumber;
            if (schematicMatches.Count > 0)
            {
                SchematicPage = schematicMatches[0].PageNumber;
            }
        }
        else
        {
            SchematicPage = selectedResult.PageNumber;
            if (boardMatches.Count > 0)
            {
                BoardPage = boardMatches[0].PageNumber;
            }
        }

        ReferenceQuery = reference;
        ActiveReference = reference;
        StatusMessage = CrossProbeStatus;
    }

    private void AddMatches(
        IReadOnlyList<PdfReferenceMatch> matches,
        string role,
        string term)
    {
        foreach (PdfReferenceMatch match in matches)
        {
            SearchResults.Add(new RepairSearchResultViewModel
            {
                DocumentRole = role,
                PageNumber = match.PageNumber,
                Occurrences = match.Occurrences,
                Reference = term,
            });
        }
    }

    private void UpdateCrossProbeSummary(
        string reference,
        IReadOnlyList<PdfReferenceMatch> boardMatches,
        IReadOnlyList<PdfReferenceMatch> schematicMatches)
    {
        BoardMatchPage = boardMatches.Count > 0 ? boardMatches[0].PageNumber : 0;
        SchematicMatchPage = schematicMatches.Count > 0 ? schematicMatches[0].PageNumber : 0;
        BoardOccurrenceCount = boardMatches.Sum(match => match.Occurrences);
        SchematicOccurrenceCount = schematicMatches.Sum(match => match.Occurrences);

        OnPropertyChanged(nameof(IsFoundOnBoard));
        OnPropertyChanged(nameof(IsFoundOnSchematic));
        OnPropertyChanged(nameof(BoardLocationText));
        OnPropertyChanged(nameof(SchematicLocationText));

        CrossProbeStatus = (boardMatches.Count > 0, schematicMatches.Count > 0) switch
        {
            (true, true) => $"{reference} encontrada en placa y esquema.",
            (true, false) => $"{reference} encontrada únicamente en la placa.",
            (false, true) => $"{reference} encontrada únicamente en el esquema.",
            _ => $"{reference} no fue encontrada.",
        };
    }

    private void ClearCrossProbe()
    {
        ActiveReference = string.Empty;
        BoardMatchPage = 0;
        SchematicMatchPage = 0;
        BoardOccurrenceCount = 0;
        SchematicOccurrenceCount = 0;
        CrossProbeStatus = "Sin referencia seleccionada.";

        OnPropertyChanged(nameof(IsFoundOnBoard));
        OnPropertyChanged(nameof(IsFoundOnSchematic));
        OnPropertyChanged(nameof(BoardLocationText));
        OnPropertyChanged(nameof(SchematicLocationText));
    }

    private bool CanAddAnnotation() => !string.IsNullOrWhiteSpace(ReferenceQuery);

    private void AddAnnotation()
    {
        RepairAnnotation annotation = new()
        {
            Reference = ReferenceQuery.Trim(),
            Title = string.IsNullOrWhiteSpace(AnnotationTitle) ? "Observación" : AnnotationTitle.Trim(),
            Notes = AnnotationNotes.Trim(),
            Status = SelectedStatus,
            BoardPage = BoardPage,
            SchematicPage = SchematicPage,
        };
        project.Annotations.Add(annotation);
        Annotations.Add(new RepairAnnotationViewModel(annotation));
        AnnotationTitle = string.Empty;
        AnnotationNotes = string.Empty;
        AddHistory("Nota agregada", annotation.Reference);
        StatusMessage = $"Nota guardada para {annotation.Reference}.";
    }

    private void RemoveSelectedAnnotation()
    {
        if (SelectedAnnotation is null) return;
        project.Annotations.Remove(SelectedAnnotation.Model);
        Annotations.Remove(SelectedAnnotation);
        SelectedAnnotation = null;
        StatusMessage = "Nota eliminada.";
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(projectFilePath)) { SaveAs(); return; }
        SaveTo(projectFilePath);
    }

    private void SaveAs()
    {
        string? path = fileDialogService.SelectRepairProjectSavePath(projectFilePath);
        if (path is not null) SaveTo(path);
    }

    private void SaveTo(string path)
    {
        try
        {
            workspaceStore.Save(path, project);
            projectFilePath = path;
            StatusMessage = $"Proyecto guardado: {Path.GetFileName(path)}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"No se pudo guardar el proyecto: {exception.Message}";
            logger.Error("Error al guardar proyecto de reparación.", exception);
        }
    }

    private async Task OpenProjectAsync()
    {
        string? path = fileDialogService.SelectRepairProjectFile();
        if (path is null) return;
        try
        {
            RepairWorkspaceProject loaded = workspaceStore.Load(path);
            project = loaded;
            projectFilePath = path;
            OnPropertyChanged(nameof(BoardFilePath)); OnPropertyChanged(nameof(SchematicFilePath));
            OnPropertyChanged(nameof(BoardFileName)); OnPropertyChanged(nameof(SchematicFileName));
            BoardPage = loaded.BoardPage; SchematicPage = loaded.SchematicPage; ReferenceQuery = loaded.LastReference;
            Annotations.Clear();
            foreach (RepairAnnotation annotation in loaded.Annotations) Annotations.Add(new RepairAnnotationViewModel(annotation));
            boardIndex = File.Exists(BoardFilePath) ? await BuildIndexAsync(BoardFilePath!, "placa") : null;
            schematicIndex = File.Exists(SchematicFilePath) ? await BuildIndexAsync(SchematicFilePath!, "esquemático") : null;
            SearchCommand.RaiseCanExecuteChanged();
            StatusMessage = $"Proyecto abierto: {Path.GetFileName(path)}";

            if (!string.IsNullOrWhiteSpace(ReferenceQuery) && CanSearch())
            {
                Search();
            }
        }
        catch (Exception exception)
        {
            StatusMessage = $"No se pudo abrir el proyecto: {exception.Message}";
            logger.Error("Error al abrir proyecto de reparación.", exception);
        }
    }

    private static string? GetDirectory(string? filePath) =>
        string.IsNullOrWhiteSpace(filePath) ? null : Path.GetDirectoryName(filePath);

    private void AddHistory(string action, string reference)
    {
        project.History.Add(new RepairHistoryEntry { Action = action, Reference = reference, BoardPage = BoardPage, SchematicPage = SchematicPage });
        if (project.History.Count > 500) project.History.RemoveRange(0, project.History.Count - 500);
    }
}
