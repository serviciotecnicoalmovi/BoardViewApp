using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using BoardView.App.Services;
using BoardView.App.ViewModels;
using BoardView.App.ViewModels.Repair;
using BoardView.Core.Contracts;

namespace BoardView.App.Views;

/// <summary>
/// Ventana principal definitiva de BoardView. Integra en una sola superficie
/// el explorador, los visores de placa y esquemático, la búsqueda, las notas
/// y la barra de estado del proyecto de reparación.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private readonly IApplicationLogger logger;
    private readonly BoardNavigationService navigationService;

    /// <summary>Estado del espacio de trabajo de reparación integrado.</summary>
    public RepairWorkspaceViewModel Workspace { get; }

    public MainWindow(
        MainWindowViewModel viewModel,
        IApplicationLogger logger,
        RepairWorkspaceViewModel repairWorkspaceViewModel)
    {
        this.viewModel =
            viewModel ??
            throw new ArgumentNullException(
                nameof(viewModel));

        this.logger =
            logger ??
            throw new ArgumentNullException(
                nameof(logger));

        Workspace =
            repairWorkspaceViewModel ??
            throw new ArgumentNullException(
                nameof(repairWorkspaceViewModel));

        Workspace.PropertyChanged +=
            OnWorkspacePropertyChanged;

        InitializeComponent();

        navigationService =
            new BoardNavigationService(
                BoardPdfDocumentView,
                SchematicPdfDocumentView);

        DataContext =
            viewModel;

        WindowState =
            WindowState.Maximized;
    }

    private void OnClosing(
        object? sender,
        CancelEventArgs e)
    {
        Workspace.PropertyChanged -=
            OnWorkspacePropertyChanged;

        viewModel.SaveSettings(
            ActualWidth,
            ActualHeight,
            WindowState == WindowState.Maximized);

        logger.Information(
            "Estado de MainShell guardado.");
    }

    private void OnWorkspacePropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName ==
                nameof(RepairWorkspaceViewModel.BoardFilePath) &&
            !string.IsNullOrWhiteSpace(
                Workspace.BoardFilePath))
        {
            viewModel.OpenPath(
                Workspace.BoardFilePath);
        }
    }

    /// <summary>
    /// Ejecuta la búsqueda al presionar el botón Buscar.
    /// </summary>
    private void OnReferenceSearchClick(
        object sender,
        RoutedEventArgs e)
    {
        ExecuteReferenceSearch();
    }

    /// <summary>
    /// Ejecuta la misma búsqueda al presionar Enter en el cuadro Referencia.
    /// </summary>
    private void OnReferenceTextBoxKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ExecuteReferenceSearch();

        e.Handled =
            true;
    }

    /// <summary>
    /// Conserva la búsqueda del RepairWorkspace y navega hacia la referencia
    /// exacta en los visores que ya tengan un resultado geométrico cargado.
    /// </summary>
    private void ExecuteReferenceSearch()
    {
        string reference =
            ReferenceTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                reference))
        {
            navigationService.ClearSelection();

            ReferenceTextBox.Focus();

            return;
        }

        Workspace.ReferenceQuery =
            reference;

        /*
         * El Workspace resuelve primero las páginas donde aparece la
         * referencia. Al cambiar BoardPage o SchematicPage, cada
         * PdfDocumentView comienza su carga geométrica.
         */
        if (Workspace.SearchCommand.CanExecute(
                parameter: null))
        {
            Workspace.SearchCommand.Execute(
                parameter: null);
        }

        /*
         * El servicio solicita la navegación en ambos visores. Si alguno aún
         * está cargando la página nueva, PdfDocumentView conserva internamente
         * la referencia pendiente y la aplica al finalizar el render.
         */
        BoardNavigationResult navigation =
            navigationService.NavigateToReference(
                reference,
                centerOnComponent: true);

        logger.Information(
            $"Navegación '{navigation.Reference}': " +
            $"placa inmediata={navigation.BoardSelectedImmediately}, " +
            $"esquemático inmediato={navigation.SchematicSelectedImmediately}, " +
            $"pendiente={navigation.HasPendingNavigation}.");

        ReferenceTextBox.SelectAll();
        ReferenceTextBox.Focus();
    }

    private void OnDragOver(
        object sender,
        DragEventArgs e)
    {
        e.Effects =
            e.Data.GetDataPresent(
                DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;

        e.Handled =
            true;
    }

    private void OnDrop(
        object sender,
        DragEventArgs e)
    {
        if (e.Data.GetData(
                DataFormats.FileDrop) is string[] files &&
            files.Length > 0)
        {
            viewModel.OpenPath(
                files[0]);
        }
    }

    private void OnExitClick(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private void OnGeometryInspectorClick(
        object sender,
        RoutedEventArgs e)
    {
        GeometryInspectorWindow inspector =
            new(
                viewModel.RecognitionResult,
                viewModel.SemanticAnalysis,
                viewModel.RecognitionAnalysis)
            {
                Owner = this,
            };

        inspector.ShowDialog();
    }
}
