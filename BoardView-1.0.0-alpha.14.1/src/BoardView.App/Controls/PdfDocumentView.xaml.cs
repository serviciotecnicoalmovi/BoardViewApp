using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace BoardView.App.Controls;

/// <summary>
/// Control reutilizable para visualizar un documento PDF local mediante
/// WebView2. Gestiona estados vacío, carga y error sin depender de la ventana
/// que lo contiene.
/// </summary>
public partial class PdfDocumentView : UserControl
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(PdfDocumentView),
        new PropertyMetadata(null, OnFilePathChanged));

    public static readonly DependencyProperty PageNumberProperty = DependencyProperty.Register(
        nameof(PageNumber), typeof(int), typeof(PdfDocumentView),
        new PropertyMetadata(1, OnPageNumberChanged));

    public static readonly DependencyProperty SearchTermProperty = DependencyProperty.Register(
        nameof(SearchTerm), typeof(string), typeof(PdfDocumentView),
        new PropertyMetadata(string.Empty, OnSearchTermChanged));

    private readonly WebView2DisplayScaleSynchronizer displayScaleSynchronizer;
    private bool isInitialized;
    private string? loadedPath;

    public PdfDocumentView()
    {
        InitializeComponent();

        displayScaleSynchronizer = new WebView2DisplayScaleSynchronizer(Browser, this);
        Loaded += OnLoaded;
        LayoutUpdated += OnLayoutUpdated;
    }

    /// <summary>Ruta absoluta del archivo PDF mostrado por el control.</summary>
    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    /// <summary>Número de página solicitado, comenzando en uno.</summary>
    public int PageNumber
    {
        get => (int)GetValue(PageNumberProperty);
        set => SetValue(PageNumberProperty, value);
    }

    /// <summary>Término que el visor PDF debe localizar y resaltar.</summary>
    public string SearchTerm
    {
        get => (string)GetValue(SearchTermProperty);
        set => SetValue(SearchTermProperty, value);
    }

    private static void OnFilePathChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        PdfDocumentView control = (PdfDocumentView)dependencyObject;
        _ = control.LoadPdfAsync(e.NewValue as string, forceNavigation: true);
    }

    private static void OnPageNumberChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        PdfDocumentView control = (PdfDocumentView)dependencyObject;
        _ = control.LoadPdfAsync(control.FilePath, forceNavigation: false);
    }

    private static void OnSearchTermChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        PdfDocumentView control = (PdfDocumentView)dependencyObject;
        _ = control.LoadPdfAsync(control.FilePath, forceNavigation: false);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateEmptyState();
        if (!string.IsNullOrWhiteSpace(FilePath))
        {
            _ = LoadPdfAsync(FilePath, forceNavigation: true);
        }
    }

    private async Task LoadPdfAsync(string? filePath, bool forceNavigation)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            loadedPath = null;
            Browser.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Visible;
            return;
        }

        string absolutePath = Path.GetFullPath(filePath);
        if (!File.Exists(absolutePath))
        {
            ShowError($"El archivo no existe: {absolutePath}");
            return;
        }

        LoadingPanel.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;

        try
        {
            if (!isInitialized)
            {
                await Browser.EnsureCoreWebView2Async();
                ConfigureBrowser();
                displayScaleSynchronizer.Synchronize();
                isInitialized = true;
            }

            int page = Math.Max(1, PageNumber);
            bool pathChanged = !string.Equals(loadedPath, absolutePath, StringComparison.OrdinalIgnoreCase);
            string source = BuildViewerUri(absolutePath, page, SearchTerm);

            if (forceNavigation || pathChanged || Browser.Source is null)
            {
                loadedPath = absolutePath;
            }

            // El visor PDF de Edge interpreta #page y #search al navegar.
            Browser.Source = new Uri(source, UriKind.Absolute);
            Browser.Visibility = Visibility.Visible;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowError("No se encontró Microsoft Edge WebView2 Runtime. Instálalo o repara Microsoft Edge para habilitar el visor PDF.");
        }
        catch (Exception exception)
        {
            ShowError($"Error al abrir el PDF: {exception.Message}");
        }
    }

    private static string BuildViewerUri(string absolutePath, int pageNumber, string? searchTerm)
    {
        string fileUri = new Uri(absolutePath, UriKind.Absolute).AbsoluteUri;
        string pageFragment = $"page={Math.Max(1, pageNumber)}";
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return $"{fileUri}#{pageFragment}";
        }

        return $"{fileUri}#{pageFragment}&search={Uri.EscapeDataString(searchTerm.Trim())}";
    }

    private void ConfigureBrowser()
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
        Browser.CoreWebView2.Settings.IsZoomControlEnabled = true;

        // El zoom del navegador se conserva en 100 %. El zoom interno del
        // visor PDF seguirá funcionando sin añadir una segunda ampliación.
        Browser.ZoomFactor = 1D;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!isInitialized)
        {
            return;
        }

        // LayoutUpdated también se ejecuta al mover la ventana entre monitores.
        // El sincronizador ignora llamadas repetidas mientras el DPI no cambie.
        displayScaleSynchronizer.Synchronize();
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        if (!e.IsSuccess)
        {
            ShowError($"WebView2 no pudo cargar el documento. Código: {e.WebErrorStatus}.");
        }
    }

    private void UpdateEmptyState()
    {
        bool hasDocument = !string.IsNullOrWhiteSpace(FilePath);
        EmptyPanel.Visibility = hasDocument ? Visibility.Collapsed : Visibility.Visible;
        Browser.Visibility = hasDocument ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Collapsed;
        Browser.Visibility = Visibility.Collapsed;
        ErrorMessage.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
