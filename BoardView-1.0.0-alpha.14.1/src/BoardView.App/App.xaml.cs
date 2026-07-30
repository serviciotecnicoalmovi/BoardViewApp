using System.Windows;
using System.Windows.Threading;
using BoardView.App.Services;
using BoardView.Application;
using BoardView.Configuration;
using BoardView.Contracts;
using BoardView.App.ViewModels;
using BoardView.App.Views;
using BoardView.Core.Contracts;
using BoardView.Core.Contracts.Documents;
using BoardView.Formats;
using BoardView.Formats.Pdf;
using BoardView.Core.Pdf;
using BoardView.Core.Recognition;
using BoardView.Core.GeometryDatabase;
using BoardView.SemanticKernel;
using BoardView.Recognition;
using BoardView.Core.Repair;
using BoardView.Infrastructure.Repair;
using BoardView.App.ViewModels.Repair;
using BoardView.Infrastructure.Configuration;
using BoardView.Infrastructure.DependencyInjection;
using BoardView.Infrastructure.Logging;
using BoardView.Plugins;
using System.IO;

namespace BoardView.App;

/// <summary>Punto de entrada y raíz de composición de BoardView.</summary>
public partial class App : System.Windows.Application

{
    private ServiceProvider? serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        //CODIGO ORIGINAL
        serviceProvider = ConfigureServices();
        RegisterGlobalExceptionHandlers();

        serviceProvider.GetRequiredService<ApplicationStartupCoordinator>().Initialize();

        MainWindow mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        //CODIGO TEMPORAL
        var testWindow =
        new Views.PdfTileTestWindow();

        testWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        serviceProvider?.GetRequiredService<IApplicationLogger>().Information("BoardView finalizado.");
        serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices()
    {
        ApplicationPathProvider paths = new();

        ServiceRegistry services = new();
        services.AddSingleton<IApplicationPathProvider>(paths);
        services.AddSingleton<ISystemClock>(_ => new SystemClock());
        services.AddSingleton<IApplicationLogger>(_ => new FileApplicationLogger(paths.LogDirectory));
        services.AddSingleton<ISettingsService>(provider =>
            new JsonSettingsService(paths.ApplicationDataDirectory, provider.GetRequiredService<IApplicationLogger>()));
        services.AddSingleton<IFileFormatRegistry>(_ => new BuiltInFileFormatRegistry());
        services.AddSingleton<IPdfDocumentIndexer>(_ => new PdfDocumentIndexer());
        services.AddSingleton<ISafePdfDocumentIndexer>(_ => new SafePdfDocumentIndexer());
        services.AddSingleton(_ => new PdfReferenceSearchService());
        services.AddSingleton<IPdfDocumentInspector>(_ => new PdfDocumentInspector());
        services.AddSingleton<PdfTechnicalDocumentParser>(_ => new PdfTechnicalDocumentParser());
        services.AddSingleton<IBoardDocumentConverter>(_ => new PdfBoardDocumentConverter());
        services.AddSingleton<IGeometryDatabaseBuilder>(_ => new GeometryDatabaseBuilder());
        services.AddSingleton<IGeometryClassificationEngine>(_ => new GeometryClassificationEngine());
        services.AddSingleton<IPadDetectionEngine>(provider => new PadDetectionEngine(
            provider.GetRequiredService<IGeometryClassificationEngine>(),
            provider.GetRequiredService<IGeometryDatabaseBuilder>()));
        services.AddSingleton<ISemanticKernel>(_ => new SemanticKernelEngine());
        services.AddSingleton<IRecognitionEngine>(_ => new RecognitionEngine());
        services.AddSingleton(provider => new PdfBoardDocumentLoader(
            provider.GetRequiredService<PdfTechnicalDocumentParser>(),
            provider.GetRequiredService<IBoardDocumentConverter>()));
        services.AddSingleton<IPluginCatalog>(_ => new DirectoryPluginCatalog());
        services.AddSingleton<IFileDialogService>(_ => new WindowsFileDialogService());
        services.AddSingleton<IRepairWorkspaceStore>(_ => new JsonRepairWorkspaceStore());
        services.AddSingleton(provider => new ApplicationStartupCoordinator(
            provider.GetRequiredService<IApplicationPathProvider>(),
            provider.GetRequiredService<IApplicationLogger>(),
            provider.GetRequiredService<IPluginCatalog>()));
        services.AddTransient(provider => new RepairWorkspaceViewModel(
            provider.GetRequiredService<IFileDialogService>(),
            provider.GetRequiredService<IRepairWorkspaceStore>(),
            provider.GetRequiredService<IApplicationLogger>(),
            provider.GetRequiredService<ISafePdfDocumentIndexer>(),
            provider.GetRequiredService<PdfReferenceSearchService>()));
        services.AddTransient(provider => new MainWindowViewModel(
            provider.GetRequiredService<IApplicationLogger>(),
            provider.GetRequiredService<ISettingsService>(),
            provider.GetRequiredService<IFileFormatRegistry>(),
            provider.GetRequiredService<IFileDialogService>(),
            provider.GetRequiredService<IPdfDocumentIndexer>(),
            provider.GetRequiredService<IPdfDocumentInspector>(),
            provider.GetRequiredService<PdfTechnicalDocumentParser>(),
            provider.GetRequiredService<IBoardDocumentConverter>(),
            provider.GetRequiredService<IPadDetectionEngine>(),
            provider.GetRequiredService<ISemanticKernel>(),
            provider.GetRequiredService<IRecognitionEngine>()));
        services.AddTransient(provider => new MainWindow(
            provider.GetRequiredService<MainWindowViewModel>(),
            provider.GetRequiredService<IApplicationLogger>(),
            provider.GetRequiredService<RepairWorkspaceViewModel>()));
        return services.Build();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogFatalException(e.Exception);
        MessageBox.Show(
            "BoardView encontró un error inesperado. El detalle fue guardado en el registro.",
            "BoardView",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogFatalException(exception);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogFatalException(e.Exception);
        e.SetObserved();
    }

    private void LogFatalException(Exception exception)
    {
        serviceProvider?.GetRequiredService<IApplicationLogger>().Error("Error global no controlado.", exception);
    }
}
