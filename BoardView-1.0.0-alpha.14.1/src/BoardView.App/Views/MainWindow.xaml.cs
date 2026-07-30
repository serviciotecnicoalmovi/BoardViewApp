using System.ComponentModel;
using System.Windows;
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

    /// <summary>Estado del espacio de trabajo de reparación integrado.</summary>
    public RepairWorkspaceViewModel Workspace { get; }

    public MainWindow(
        MainWindowViewModel viewModel,
        IApplicationLogger logger,
        RepairWorkspaceViewModel repairWorkspaceViewModel)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Workspace = repairWorkspaceViewModel ?? throw new ArgumentNullException(nameof(repairWorkspaceViewModel));

        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
        InitializeComponent();
        DataContext = viewModel;

        // La Shell siempre inicia maximizada para evitar recuperar dimensiones
        // antiguas que puedan dejar filas superiores fuera de la pantalla.
        WindowState = WindowState.Maximized;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        Workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        viewModel.SaveSettings(ActualWidth, ActualHeight, WindowState == WindowState.Maximized);
        logger.Information("Estado de MainShell guardado.");
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RepairWorkspaceViewModel.BoardFilePath)
            && !string.IsNullOrWhiteSpace(Workspace.BoardFilePath))
        {
            // La placa alimenta también el pipeline técnico ya estabilizado.
            viewModel.OpenPath(Workspace.BoardFilePath);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            viewModel.OpenPath(files[0]);
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnGeometryInspectorClick(object sender, RoutedEventArgs e)
    {
        GeometryInspectorWindow inspector = new(
            viewModel.RecognitionResult,
            viewModel.SemanticAnalysis,
            viewModel.RecognitionAnalysis)
        {
            Owner = this,
        };
        inspector.ShowDialog();
    }
}
