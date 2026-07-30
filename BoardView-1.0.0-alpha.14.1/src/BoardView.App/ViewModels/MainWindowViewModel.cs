using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using BoardView.App.Samples;
using BoardView.App.Services;
using BoardView.Core.Configuration;
using BoardView.Core.Contracts;
using BoardView.Core.Documents;
using BoardView.Core.Formats;
using BoardView.Core.Pdf;
using BoardView.Core.Contracts.Documents;
using BoardView.Core.Documents.Common;
using BoardView.Formats.Pdf;
using BoardView.Core.Recognition;
using BoardView.SemanticKernel;
using BoardView.Recognition;

using System.IO;

namespace BoardView.App.ViewModels;

/// <summary>Modos de presentación disponibles para un PDF convertido al modelo interno.</summary>
public enum PdfPresentationMode
{
    Pdf,
    Model,
    Overlay,
}

/// <summary>Estado y comandos de la ventana principal.</summary>
public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IApplicationLogger logger;
    private readonly ISettingsService settingsService;
    private readonly IFileFormatRegistry formatRegistry;
    private readonly IFileDialogService fileDialogService;
    private readonly ApplicationSettings settings;
    private readonly IPdfDocumentIndexer pdfDocumentIndexer;
    private readonly IPdfDocumentInspector pdfDocumentInspector;
    private readonly PdfTechnicalDocumentParser pdfTechnicalDocumentParser;
    private readonly IBoardDocumentConverter boardDocumentConverter;
    private readonly IPadDetectionEngine padDetectionEngine;
    private readonly ISemanticKernel semanticKernel;
    private readonly IRecognitionEngine recognitionEngine;
    private string documentName = "Ningún archivo abierto";
    private string statusMessage = "Listo";
    private bool showGrid;
    private BoardDocument? document;
    private string? pdfFilePath;
    private string? openedPdfFilePath;
    private bool isPdfDocument;
    private bool isPdfViewerVisible;
    private bool isPdfUnsupportedVisible;
    private string pdfUnsupportedTitle = "Documento PDF no compatible";
    private string pdfUnsupportedMessage = string.Empty;
    private PdfDocumentIndex? pdfDocumentIndex;
    private TechnicalDocument? pdfTechnicalDocument;
    private string pdfAnalysisSummary = "Índice técnico pendiente";
    private string pdfSearchText = string.Empty;
    private string pdfSearchSummary = string.Empty;
    private bool isPdfAnalyzing;
    private PdfPresentationMode pdfDisplayMode = PdfPresentationMode.Pdf;
    private RecognitionResult recognitionResult = RecognitionResult.Empty;
    private SemanticAnalysisResult semanticAnalysis = SemanticAnalysisResult.Empty;
    private RecognitionAnalysis recognitionAnalysis = RecognitionAnalysis.Empty;
    private bool showDetectedPads = true;
    private bool showDetectedVias = true;
    private bool showDetectedHoles = true;
    private bool showRecognizedFootprints = true;

    public MainWindowViewModel(
        IApplicationLogger logger,
        ISettingsService settingsService,
        IFileFormatRegistry formatRegistry,
        IFileDialogService fileDialogService,
        IPdfDocumentIndexer pdfDocumentIndexer,
        IPdfDocumentInspector pdfDocumentInspector,
        PdfTechnicalDocumentParser pdfTechnicalDocumentParser,
        IBoardDocumentConverter boardDocumentConverter,
        IPadDetectionEngine padDetectionEngine,
        ISemanticKernel semanticKernel,
        IRecognitionEngine recognitionEngine)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.formatRegistry = formatRegistry ?? throw new ArgumentNullException(nameof(formatRegistry));
        this.fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        this.pdfDocumentIndexer = pdfDocumentIndexer ?? throw new ArgumentNullException(nameof(pdfDocumentIndexer));
        this.pdfDocumentInspector = pdfDocumentInspector ?? throw new ArgumentNullException(nameof(pdfDocumentInspector));
        this.pdfTechnicalDocumentParser = pdfTechnicalDocumentParser ?? throw new ArgumentNullException(nameof(pdfTechnicalDocumentParser));
        this.boardDocumentConverter = boardDocumentConverter ?? throw new ArgumentNullException(nameof(boardDocumentConverter));
        this.padDetectionEngine = padDetectionEngine ?? throw new ArgumentNullException(nameof(padDetectionEngine));
        this.semanticKernel = semanticKernel ?? throw new ArgumentNullException(nameof(semanticKernel));
        this.recognitionEngine = recognitionEngine ?? throw new ArgumentNullException(nameof(recognitionEngine));
        settings = settingsService.Load();
        showGrid = settings.ShowGrid;
        OpenFileCommand = new RelayCommand(OpenFile);
        ToggleGridCommand = new RelayCommand(() => ShowGrid = !ShowGrid);
        LoadDemonstrationCommand = new RelayCommand(LoadDemonstration);
        SearchPdfCommand = new RelayCommand(SearchPdf, CanSearchPdf);
        OpenExternalPdfCommand = new RelayCommand(OpenExternalPdf, CanOpenExternalPdf);
        ShowPdfModeCommand = new RelayCommand(() => SetPdfPresentationMode(PdfPresentationMode.Pdf));
        ShowModelModeCommand = new RelayCommand(() => SetPdfPresentationMode(PdfPresentationMode.Model));
        ShowOverlayModeCommand = new RelayCommand(() => SetPdfPresentationMode(PdfPresentationMode.Overlay));
        SupportedFormats = new ObservableCollection<FormatListItemViewModel>(
            formatRegistry.Formats.Select(CreateFormatListItem));
    }

    public ICommand OpenFileCommand { get; }
    public ICommand ToggleGridCommand { get; }
    public ICommand LoadDemonstrationCommand { get; }
    public RelayCommand SearchPdfCommand { get; }
    public RelayCommand OpenExternalPdfCommand { get; }
    public ICommand ShowPdfModeCommand { get; }
    public ICommand ShowModelModeCommand { get; }
    public ICommand ShowOverlayModeCommand { get; }
    public ObservableCollection<FormatListItemViewModel> SupportedFormats { get; }
    public ApplicationSettings Settings => settings;

    /// <summary>Documento normalizado mostrado actualmente por el viewport.</summary>
    public BoardDocument? Document
    {
        get => document;
        private set
        {
            if (SetProperty(ref document, value))
            {
                OnPropertyChanged(nameof(IsNativePdfModelVisible));
                OnPropertyChanged(nameof(IsPdfSurfaceVisible));
            }
        }
    }


    /// <summary>Resultado del reconocimiento electrónico del documento activo.</summary>
    public RecognitionResult RecognitionResult
    {
        get => recognitionResult;
        private set => SetProperty(ref recognitionResult, value);
    }

    /// <summary>Resultado semántico del documento activo.</summary>
    public SemanticAnalysisResult SemanticAnalysis
    {
        get => semanticAnalysis;
        private set => SetProperty(ref semanticAnalysis, value);
    }


    /// <summary>Resultado del reconocimiento de footprints y componentes.</summary>
    public RecognitionAnalysis RecognitionAnalysis
    {
        get => recognitionAnalysis;
        private set => SetProperty(ref recognitionAnalysis, value);
    }

    public bool ShowDetectedPads { get => showDetectedPads; set => SetProperty(ref showDetectedPads, value); }
    public bool ShowDetectedVias { get => showDetectedVias; set => SetProperty(ref showDetectedVias, value); }
    public bool ShowDetectedHoles { get => showDetectedHoles; set => SetProperty(ref showDetectedHoles, value); }
    public bool ShowRecognizedFootprints { get => showRecognizedFootprints; set => SetProperty(ref showRecognizedFootprints, value); }

    /// <summary>Documento PDF normalizado al modelo común del núcleo.</summary>
    public TechnicalDocument? PdfTechnicalDocument
    {
        get => pdfTechnicalDocument;
        private set => SetProperty(ref pdfTechnicalDocument, value);
    }

    public string DocumentName
    {
        get => documentName;
        private set => SetProperty(ref documentName, value);
    }

    /// <summary>Ruta absoluta del PDF que debe mostrar el visor integrado.</summary>
    public string? PdfFilePath
    {
        get => pdfFilePath;
        private set => SetProperty(ref pdfFilePath, value);
    }

    /// <summary>Indica si la superficie activa corresponde al visor PDF.</summary>
    public bool IsPdfDocument
    {
        get => isPdfDocument;
        private set
        {
            if (SetProperty(ref isPdfDocument, value))
            {
                OnPropertyChanged(nameof(IsBoardViewportVisible));
                OnPropertyChanged(nameof(IsNativePdfModelVisible));
            }
        }
    }

    /// <summary>Indica si debe mostrarse el viewport nativo de placas.</summary>
    public bool IsBoardViewportVisible => !IsPdfDocument;

    /// <summary>Indica si el PDF puede mostrarse mediante WebView2.</summary>
    public bool IsPdfViewerVisible
    {
        get => isPdfViewerVisible;
        private set
        {
            if (SetProperty(ref isPdfViewerVisible, value))
            {
                OnPropertyChanged(nameof(IsPdfSurfaceVisible));
            }
        }
    }

    /// <summary>Modo visual activo para documentos PDF normalizados.</summary>
    public PdfPresentationMode ActivePdfMode
    {
        get => pdfDisplayMode;
        set
        {
            if (SetProperty(ref pdfDisplayMode, value))
            {
                OnPropertyChanged(nameof(IsPdfOnlyMode));
                OnPropertyChanged(nameof(IsModelOnlyMode));
                OnPropertyChanged(nameof(IsOverlayMode));
                OnPropertyChanged(nameof(IsNativePdfModelVisible));
                OnPropertyChanged(nameof(IsPdfSurfaceVisible));
            }
        }
    }

    public bool IsPdfOnlyMode => ActivePdfMode == PdfPresentationMode.Pdf;
    public bool IsModelOnlyMode => ActivePdfMode == PdfPresentationMode.Model;
    public bool IsOverlayMode => ActivePdfMode == PdfPresentationMode.Overlay;
    public bool IsNativePdfModelVisible => IsPdfDocument && Document is not null && !IsPdfOnlyMode;
    public bool IsPdfSurfaceVisible => IsPdfViewerVisible && !IsModelOnlyMode;

    /// <summary>Indica si debe mostrarse el panel de incompatibilidad PDF.</summary>
    public bool IsPdfUnsupportedVisible
    {
        get => isPdfUnsupportedVisible;
        private set => SetProperty(ref isPdfUnsupportedVisible, value);
    }

    public string PdfUnsupportedTitle
    {
        get => pdfUnsupportedTitle;
        private set => SetProperty(ref pdfUnsupportedTitle, value);
    }

    public string PdfUnsupportedMessage
    {
        get => pdfUnsupportedMessage;
        private set => SetProperty(ref pdfUnsupportedMessage, value);
    }


    /// <summary>Resumen del índice técnico extraído del PDF activo.</summary>
    public string PdfAnalysisSummary
    {
        get => pdfAnalysisSummary;
        private set => SetProperty(ref pdfAnalysisSummary, value);
    }

    /// <summary>Texto que se buscará dentro de las palabras indexadas del PDF.</summary>
    public string PdfSearchText
    {
        get => pdfSearchText;
        set
        {
            if (SetProperty(ref pdfSearchText, value))
            {
                SearchPdfCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Resultado resumido de la última búsqueda técnica.</summary>
    public string PdfSearchSummary
    {
        get => pdfSearchSummary;
        private set => SetProperty(ref pdfSearchSummary, value);
    }

    /// <summary>Indica que el índice PDF se está construyendo en segundo plano.</summary>
    public bool IsPdfAnalyzing
    {
        get => isPdfAnalyzing;
        private set
        {
            if (SetProperty(ref isPdfAnalyzing, value))
            {
                SearchPdfCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public bool ShowGrid
    {
        get => showGrid;
        set
        {
            if (SetProperty(ref showGrid, value))
            {
                settings.ShowGrid = value;
            }
        }
    }

    public void OpenPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            StatusMessage = "El archivo seleccionado no existe.";
            return;
        }

        FileFormatDescriptor? format = formatRegistry.Detect(filePath);
        string extension = Path.GetExtension(filePath);
        bool isPdf = string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);

        Document = null;
        RecognitionResult = RecognitionResult.Empty;
        SemanticAnalysis = SemanticAnalysisResult.Empty;
        RecognitionAnalysis = RecognitionAnalysis.Empty;
        DocumentName = Path.GetFileName(filePath);
        openedPdfFilePath = isPdf ? Path.GetFullPath(filePath) : null;
        PdfFilePath = null;
        IsPdfDocument = isPdf;
        ActivePdfMode = PdfPresentationMode.Pdf;
        IsPdfViewerVisible = false;
        IsPdfUnsupportedVisible = false;
        OpenExternalPdfCommand.RaiseCanExecuteChanged();
        settings.LastOpenedDirectory = Path.GetDirectoryName(filePath);

        StatusMessage = isPdf
            ? $"Inspeccionando documento PDF: {DocumentName}"
            : format is null
                ? "Formato aún no reconocido; se abrirá cuando exista un parser compatible."
                : $"Detectado: {format.DisplayName}. Parser pendiente.";

        if (isPdf)
        {
            _ = BuildPdfIndexAsync(filePath);
        }
        else
        {
            ResetPdfAnalysis();
        }

        logger.Information(isPdf
            ? $"PDF cargado y enviado al indexador técnico: {filePath}"
            : $"Archivo seleccionado: {filePath}");
    }

    public void SaveSettings(double width, double height, bool isMaximized)
    {
        if (!isMaximized)
        {
            settings.WindowWidth = width;
            settings.WindowHeight = height;
        }

        settings.IsWindowMaximized = isMaximized;
        settingsService.Save(settings);
    }

    private static FormatListItemViewModel CreateFormatListItem(FileFormatDescriptor descriptor)
    {
        return descriptor.Id.ToLowerInvariant() switch
        {
            "pdf" => new FormatListItemViewModel(descriptor, "\uE8A5", "#EF3340"),
            "gerber" => new FormatListItemViewModel(descriptor, "\uE9F9", "#35A936"),
            "excellon" => new FormatListItemViewModel(descriptor, "\uE90F", "#F39A18"),
            "kicad-pcb" => new FormatListItemViewModel(descriptor, "\uE943", "#8539D5"),
            "eagle" => new FormatListItemViewModel(descriptor, "\uE7C3", "#1877C9"),
            "pcb" => new FormatListItemViewModel(descriptor, "\uE8B7", "#00A4A6"),
            "ipc2581" => new FormatListItemViewModel(descriptor, "\uE8B5", "#C8A000"),
            "odbpp" => new FormatListItemViewModel(descriptor, "\uE8B7", "#C12664"),
            _ => new FormatListItemViewModel(descriptor, "\uE8A5", "#4DA3FF")
        };
    }

    private void SetPdfPresentationMode(PdfPresentationMode mode)
    {
        ActivePdfMode = mode;
        StatusMessage = mode switch
        {
            PdfPresentationMode.Pdf => "Vista PDF original activa.",
            PdfPresentationMode.Model => Document is null
                ? "El modelo nativo todavía se está construyendo."
                : $"Render nativo activo: {Document.Elements.Count:N0} elementos desde BoardDocument.",
            PdfPresentationMode.Overlay => "Superposición diagnóstica activa.",
            _ => StatusMessage,
        };
    }

    private void LoadDemonstration()
    {
        openedPdfFilePath = null;
        PdfFilePath = null;
        IsPdfDocument = false;
        IsPdfViewerVisible = false;
        IsPdfUnsupportedVisible = false;
        OpenExternalPdfCommand.RaiseCanExecuteChanged();
        ResetPdfAnalysis();
        BoardDocument demonstration = DemonstrationBoardFactory.Create();
        Document = demonstration;
        RecognitionResult = padDetectionEngine.Analyze(demonstration);
        SemanticAnalysis = semanticKernel.Analyze(demonstration, RecognitionResult);
        RecognitionAnalysis = recognitionEngine.Analyze(demonstration, RecognitionResult, SemanticAnalysis);
        DocumentName = demonstration.Name;
        StatusMessage = $"Modelo cargado: {Document.Elements.Count} elementos, {Document.Layers.Count} capas y {Document.Nets.Count} redes.";
        logger.Information("Documento interno de demostración cargado.");
    }

    private async Task BuildPdfIndexAsync(string filePath)
    {
        IsPdfAnalyzing = true;
        PdfAnalysisSummary = "Inspeccionando estructura del PDF...";
        PdfSearchSummary = string.Empty;
        pdfDocumentIndex = null;
        PdfTechnicalDocument = null;
        RecognitionResult = RecognitionResult.Empty;
        SemanticAnalysis = SemanticAnalysisResult.Empty;
        RecognitionAnalysis = RecognitionAnalysis.Empty;

        try
        {
            PdfDocumentInspection inspection = await Task.Run(() =>
                pdfDocumentInspector.Inspect(filePath));

            if (!string.Equals(openedPdfFilePath, inspection.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            PdfAnalysisSummary = BuildInspectionSummary(inspection);
            if (!inspection.CanUseIntegratedViewer)
            {
                ShowUnsupportedPdf(inspection);
                logger.Information($"PDF clasificado como {inspection.DocumentType}: {inspection.FilePath}");
                return;
            }

            PdfFilePath = inspection.FilePath;
            IsPdfViewerVisible = true;
            IsPdfUnsupportedVisible = false;
            StatusMessage = inspection.Message;

            if (!inspection.CanAnalyzeTechnically)
            {
                PdfAnalysisSummary = $"{inspection.PageCount:N0} páginas · {GetPdfTypeDisplayName(inspection.DocumentType)} · análisis técnico no disponible";
                return;
            }

            PdfAnalysisSummary = $"{inspection.PageCount:N0} páginas · normalizando texto y vectores...";
            Task<PdfDocumentIndex> indexTask = Task.Run(() => pdfDocumentIndexer.BuildIndex(filePath));
            Task<TechnicalDocument> documentTask = pdfTechnicalDocumentParser
                .ParseAsync(new DocumentParseRequest(filePath))
                .AsTask();

            await Task.WhenAll(indexTask, documentTask);
            PdfDocumentIndex index = await indexTask;
            TechnicalDocument technicalDocument = await documentTask;

            if (!string.Equals(openedPdfFilePath, index.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            pdfDocumentIndex = index;
            PdfTechnicalDocument = technicalDocument;
            BoardDocument normalizedDocument = boardDocumentConverter.Convert(technicalDocument);
            Document = normalizedDocument;
            RecognitionResult = await Task.Run(() => padDetectionEngine.Analyze(normalizedDocument));
            SemanticAnalysis = await Task.Run(() => semanticKernel.Analyze(normalizedDocument, RecognitionResult));
            RecognitionAnalysis = await Task.Run(() => recognitionEngine.Analyze(normalizedDocument, RecognitionResult, SemanticAnalysis));
            OnPropertyChanged(nameof(IsNativePdfModelVisible));
            int textCount = technicalDocument.Pages.Sum(static page =>
                page.Graphics.Count(static graphic => graphic is BoardView.Core.Graphics.TextGraphic));
            int vectorCount = technicalDocument.Pages.Sum(static page =>
                page.Graphics.Count(static graphic => graphic is not BoardView.Core.Graphics.TextGraphic));
            int graphicCount = textCount + vectorCount;
            PdfAnalysisSummary = $"{index.PageCount} páginas · {index.WordCount:N0} palabras · {vectorCount:N0} vectores · " +
                $"{RecognitionResult.GeometryDatabase.TotalCount:N0} registros · " +
                $"{RecognitionResult.Diagnostics.ClassifiedPrimitiveCount:N0} clasificadas · " +
                $"{RecognitionResult.Diagnostics.CandidateCount:N0} candidatos · " +
                $"{RecognitionResult.Pads.Count:N0} pads · {RecognitionResult.Footprints.Count:N0} footprints · " +
                $"{SemanticAnalysis.Count(PrimitiveSemantic.Unknown):N0} semánticas desconocidas · " +
                $"{RecognitionAnalysis.Components.Count:N0} componentes";
            StatusMessage = $"{GetPdfTypeDisplayName(inspection.DocumentType)} normalizado. " +
                $"{RecognitionResult.Diagnostics.DetailedSummary}. Resultado: {RecognitionResult.Summary}. " +
                $"Alto nivel: {RecognitionAnalysis.Summary}.";
            logger.Information($"PDF normalizado: {index.FilePath}; tipo={inspection.DocumentType}; páginas={index.PageCount}; " +
                $"palabras={index.WordCount}; textos={textCount}; vectores={vectorCount}; objetos={graphicCount}; " +
                $"geometría={RecognitionResult.GeometryDatabase.Summary}; diagnóstico={RecognitionResult.Diagnostics.DetailedSummary}; " +
                $"semántica={SemanticAnalysis.Summary}.");
        }
        catch (Exception exception)
        {
            PdfAnalysisSummary = "No se pudo inspeccionar el documento PDF";
            PdfSearchSummary = string.Empty;
            PdfFilePath = null;
            IsPdfViewerVisible = false;
            IsPdfUnsupportedVisible = true;
            PdfUnsupportedTitle = "Error al abrir el PDF";
            PdfUnsupportedMessage = exception.Message;
            StatusMessage = $"El documento PDF no pudo abrirse: {exception.Message}";
            logger.Error($"Error al inspeccionar o indexar el PDF: {filePath}", exception);
        }
        finally
        {
            IsPdfAnalyzing = false;
            SearchPdfCommand.RaiseCanExecuteChanged();
        }
    }

    private void ShowUnsupportedPdf(PdfDocumentInspection inspection)
    {
        PdfFilePath = null;
        IsPdfViewerVisible = false;
        IsPdfUnsupportedVisible = true;
        PdfUnsupportedTitle = inspection.DocumentType switch
        {
            PdfDocumentType.XfaDynamic => "PDF XFA dinámico",
            PdfDocumentType.XfaStatic => "PDF con formulario XFA",
            PdfDocumentType.Protected => "PDF protegido",
            PdfDocumentType.Corrupted => "PDF dañado o no válido",
            _ => "Documento PDF no compatible"
        };
        PdfUnsupportedMessage = inspection.Message;
        StatusMessage = inspection.Message;
    }

    private static string BuildInspectionSummary(PdfDocumentInspection inspection) =>
        inspection.PageCount > 0
            ? $"{inspection.PageCount:N0} páginas · {GetPdfTypeDisplayName(inspection.DocumentType)}"
            : GetPdfTypeDisplayName(inspection.DocumentType);

    private static string GetPdfTypeDisplayName(PdfDocumentType documentType) => documentType switch
    {
        PdfDocumentType.Standard => "PDF estándar",
        PdfDocumentType.ImageOnly => "PDF rasterizado",
        PdfDocumentType.Technical => "PDF técnico",
        PdfDocumentType.AcroForm => "PDF AcroForm",
        PdfDocumentType.XfaStatic => "PDF XFA estático",
        PdfDocumentType.XfaDynamic => "PDF XFA dinámico",
        PdfDocumentType.Protected => "PDF protegido",
        PdfDocumentType.Corrupted => "PDF dañado",
        _ => "PDF sin clasificar"
    };

    private bool CanOpenExternalPdf() =>
        !string.IsNullOrWhiteSpace(openedPdfFilePath) && File.Exists(openedPdfFilePath);

    private void OpenExternalPdf()
    {
        if (!CanOpenExternalPdf())
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(openedPdfFilePath!)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            StatusMessage = $"No se pudo abrir el PDF con una aplicación externa: {exception.Message}";
            logger.Error("Error al abrir el PDF con la aplicación predeterminada.", exception);
        }
    }

    private bool CanSearchPdf() =>
        !IsPdfAnalyzing &&
        pdfDocumentIndex is not null &&
        !string.IsNullOrWhiteSpace(PdfSearchText);

    private void SearchPdf()
    {
        if (pdfDocumentIndex is null || string.IsNullOrWhiteSpace(PdfSearchText))
        {
            return;
        }

        string term = PdfSearchText.Trim();
        List<int> matchingPages = pdfDocumentIndex.Pages
            .Where(page => page.Words.Any(word => word.Text.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(page => page.Number)
            .ToList();

        int occurrenceCount = pdfDocumentIndex.Pages.Sum(page =>
            page.Words.Count(word => word.Text.Contains(term, StringComparison.OrdinalIgnoreCase)));

        PdfSearchSummary = matchingPages.Count == 0
            ? $"Sin coincidencias para “{term}”."
            : $"{occurrenceCount:N0} coincidencias en {matchingPages.Count:N0} páginas. Primeras páginas: {string.Join(", ", matchingPages.Take(8))}.";

        StatusMessage = PdfSearchSummary;
    }

    private void ResetPdfAnalysis()
    {
        pdfDocumentIndex = null;
        PdfTechnicalDocument = null;
        PdfFilePath = null;
        IsPdfViewerVisible = false;
        IsPdfUnsupportedVisible = false;
        PdfUnsupportedTitle = "Documento PDF no compatible";
        PdfUnsupportedMessage = string.Empty;
        PdfAnalysisSummary = "Índice técnico pendiente";
        PdfSearchText = string.Empty;
        PdfSearchSummary = string.Empty;
        IsPdfAnalyzing = false;
        SearchPdfCommand.RaiseCanExecuteChanged();
    }

    private void OpenFile()
    {
        string? filePath = fileDialogService.SelectFile(settings.LastOpenedDirectory);
        if (filePath is not null)
        {
            OpenPath(filePath);
        }
    }
}
