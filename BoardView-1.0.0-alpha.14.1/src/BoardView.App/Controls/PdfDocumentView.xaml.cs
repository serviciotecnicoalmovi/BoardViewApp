using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using BoardView.Rendering.Geometry;
using BoardView.Rendering.Recognition;
using Microsoft.Web.WebView2.Core;

namespace BoardView.App.Controls;

/// <summary>
/// Visualiza documentos PDF utilizando el pipeline geométrico de PDFium.
/// </summary>
public partial class PdfDocumentView : UserControl
{
    private const double GeometryZoomFactor = 1D;

    public static readonly DependencyProperty FilePathProperty =
        DependencyProperty.Register(
            nameof(FilePath),
            typeof(string),
            typeof(PdfDocumentView),
            new PropertyMetadata(
                null,
                OnFilePathChanged));

    public static readonly DependencyProperty PageNumberProperty =
        DependencyProperty.Register(
            nameof(PageNumber),
            typeof(int),
            typeof(PdfDocumentView),
            new PropertyMetadata(
                1,
                OnPageNumberChanged));

    public static readonly DependencyProperty SearchTermProperty =
        DependencyProperty.Register(
            nameof(SearchTerm),
            typeof(string),
            typeof(PdfDocumentView),
            new PropertyMetadata(
                string.Empty,
                OnSearchTermChanged));

    private readonly WebView2DisplayScaleSynchronizer displayScaleSynchronizer;

    private CancellationTokenSource? renderCancellation;
    private GeometryRenderPipeline? geometryPipeline;
    private GeometryRenderResult? geometryResult;
    private BoardGeometryIndexedComponent? hoveredComponent;
    private BoardGeometryIndexedComponent? selectedComponent;
    private BoardGeometryBounds? selectedSelectionBounds;
    private BoardReferenceEntry? selectedReferenceEntry;
    private string? pendingReference;
    private bool isBrowserInitialized;
    private string? loadedPath;

    public PdfDocumentView()
    {
        InitializeComponent();

        displayScaleSynchronizer =
            new WebView2DisplayScaleSynchronizer(
                Browser,
                this);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        LayoutUpdated += OnLayoutUpdated;
    }

    /// <summary>
    /// Se produce cuando cambia el componente situado bajo el cursor.
    /// </summary>
    public event EventHandler<BoardGeometryComponentEventArgs>? GeometryComponentHovered;

    /// <summary>
    /// Se produce cuando el usuario selecciona un componente.
    /// </summary>
    public event EventHandler<BoardGeometryComponentEventArgs>? GeometryComponentSelected;

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public int PageNumber
    {
        get => (int)GetValue(PageNumberProperty);
        set => SetValue(PageNumberProperty, value);
    }

    public string SearchTerm
    {
        get => (string)GetValue(SearchTermProperty);
        set => SetValue(SearchTermProperty, value);
    }

    public BoardGeometryIndexedComponent? HoveredComponent =>
        hoveredComponent;

    public BoardGeometryIndexedComponent? SelectedComponent =>
        selectedComponent;

    /// <summary>
    /// Referencia semántica asociada a la selección actual, cuando existe.
    /// </summary>
    public BoardReferenceEntry? SelectedReferenceEntry =>
        selectedReferenceEntry;

    /// <summary>
    /// Resultado geométrico actualmente mostrado.
    /// </summary>
    public GeometryRenderResult? GeometryResult =>
        geometryResult;

    /// <summary>
    /// Busca una referencia exacta, fija su componente como selección,
    /// actualiza el overlay y centra el visor sobre la geometría.
    /// </summary>
    public bool SelectReference(
        string reference,
        bool centerOnComponent = true)
    {
        string normalizedReference =
            NormalizeReference(
                reference);

        Debug.WriteLine(
            $"[PdfDocumentView] SelectReference: '{normalizedReference}'.");

        if (normalizedReference.Length == 0)
        {
            Debug.WriteLine(
                "[PdfDocumentView] Referencia vacía.");

            return false;
        }

        /*
         * La solicitud se almacena antes de consultar el resultado actual.
         * Así no se pierde cuando MainWindow cambia PageNumber y el control
         * todavía conserva temporalmente el GeometryRenderResult anterior.
         */
        pendingReference =
            normalizedReference;

        GeometryRenderResult? result =
            geometryResult;

        int requestedPageIndex =
            Math.Max(
                1,
                PageNumber) -
            1;

        if (result is null)
        {
            Debug.WriteLine(
                "[PdfDocumentView] El render geométrico todavía no está " +
                "disponible; la referencia queda pendiente.");

            return false;
        }

        if (result.Original.PageIndex != requestedPageIndex)
        {
            Debug.WriteLine(
                $"[PdfDocumentView] El resultado disponible pertenece a la " +
                $"página {result.Original.PageIndex + 1}, pero se solicitó la " +
                $"página {requestedPageIndex + 1}; la referencia queda pendiente.");

            return false;
        }

        Debug.WriteLine(
            "[PdfDocumentView] ===== REFERENCE INDEX DIAGNOSTIC =====");

        Debug.WriteLine(
            $"[PdfDocumentView] Documento: {FilePath}");

        Debug.WriteLine(
            $"[PdfDocumentView] Página solicitada: {requestedPageIndex + 1}");

        Debug.WriteLine(
            $"[PdfDocumentView] Página del resultado: " +
            $"{result.Original.PageIndex + 1}");

        Debug.WriteLine(
            $"[PdfDocumentView] Observaciones textuales: " +
            $"{result.TextObservations.Count:N0}");

        Debug.WriteLine(
            $"[PdfDocumentView] Candidatos de referencia: " +
            $"{result.ReferenceCandidates.Count:N0}");

        Debug.WriteLine(
            $"[PdfDocumentView] Asociaciones de referencia: " +
            $"{result.ReferenceAssociation.Statistics.AssociationCount:N0}");

        Debug.WriteLine(
            $"[PdfDocumentView] Cobertura de referencias: " +
            $"{result.ReferenceAssociation.Statistics.CandidateCoverage:P2}");

        Debug.WriteLine(
            $"[PdfDocumentView] Entradas totales: " +
            $"{result.ReferenceIndex.Statistics.EntryCount:N0}");

        Debug.WriteLine(
            $"[PdfDocumentView] Referencias únicas: " +
            $"{result.ReferenceIndex.Statistics.UniqueReferenceCount:N0}");

        Debug.WriteLine(
            $"[PdfDocumentView] Componentes geométricos detectados: " +
            $"{result.Components.Components.Count:N0}");

        Debug.WriteLine(
            $"[PdfDocumentView] Componentes geométricos indexados: " +
            $"{result.GeometryIndex.Components.Count:N0}");

        bool exactReferenceExists =
            result.ReferenceIndex.TryGetByReference(
                normalizedReference,
                out BoardReferenceEntry? diagnosticEntry);

        Debug.WriteLine(
            $"[PdfDocumentView] Referencia exacta '{normalizedReference}': " +
            $"{(exactReferenceExists ? "SÍ" : "NO")}");

        string failedStage =
            result.TextObservations.Count == 0
                ? "EXTRACCIÓN DE TEXTO PDF"
                : result.ReferenceCandidates.Count == 0
                    ? "DETECCIÓN DE REFERENCIAS"
                    : result.ReferenceAssociation.Statistics.AssociationCount == 0
                        ? "ASOCIACIÓN TEXTO-GEOMETRÍA"
                        : result.ReferenceIndex.Count == 0
                            ? "CONSTRUCCIÓN DEL REFERENCE INDEX"
                            : exactReferenceExists
                                ? "NINGUNA: REFERENCIA DISPONIBLE"
                                : "REFERENCIA ESPECÍFICA NO ASOCIADA";

        Debug.WriteLine(
            $"[PdfDocumentView] Primera etapa problemática: {failedStage}");

        if (result.TextObservations.Count > 0)
        {
            Debug.WriteLine(
                "[PdfDocumentView] Primeras observaciones textuales:");

            foreach (BoardTextObservation observation
                     in result.TextObservations.Take(25))
            {
                Debug.WriteLine(
                    $"[PdfDocumentView]   '{observation.Text}' " +
                    $"Página={observation.PageIndex + 1}, " +
                    $"Bounds={observation.Bounds}, " +
                    $"Confianza={observation.Confidence:P1}");
            }
        }

        if (result.ReferenceCandidates.Count > 0)
        {
            Debug.WriteLine(
                "[PdfDocumentView] Primeros candidatos de referencia:");

            foreach (BoardReferenceCandidate candidate
                     in result.ReferenceCandidates.Take(25))
            {
                Debug.WriteLine(
                    $"[PdfDocumentView]   {candidate.NormalizedReference} " +
                    $"Página={candidate.PageIndex + 1}, " +
                    $"Bounds={candidate.Bounds}, " +
                    $"Confianza={candidate.Confidence:P1}");
            }
        }

        BoardReferenceLookupResult diagnosticSearch =
            result.ReferenceIndex.Search(
                normalizedReference,
                maximumResults: 25);

        Debug.WriteLine(
            $"[PdfDocumentView] Coincidencias de búsqueda: " +
            $"{diagnosticSearch.Matches.Count:N0}");

        foreach (BoardReferenceEntry indexedEntry
                 in diagnosticSearch.Matches)
        {
            Debug.WriteLine(
                $"[PdfDocumentView]   {indexedEntry.Reference} → " +
                $"ID={indexedEntry.ComponentId}, " +
                $"Tipo={indexedEntry.ComponentType}, " +
                $"Página={indexedEntry.PageIndex + 1}, " +
                $"Confianza={indexedEntry.Confidence:P1}, " +
                $"Regla={indexedEntry.AssociationRule}");
        }

        if (!exactReferenceExists)
        {
            string prefix =
                new(
                    normalizedReference
                        .TakeWhile(
                            static character =>
                                !char.IsDigit(character))
                        .ToArray());

            if (!string.IsNullOrWhiteSpace(prefix))
            {
                IReadOnlyList<BoardReferenceEntry> prefixEntries =
                    result.ReferenceIndex.FindByPrefix(
                        prefix);

                Debug.WriteLine(
                    $"[PdfDocumentView] Primeras referencias con prefijo " +
                    $"'{prefix}':");

                foreach (BoardReferenceEntry prefixEntry
                         in prefixEntries.Take(25))
                {
                    Debug.WriteLine(
                        $"[PdfDocumentView]   {prefixEntry.Reference} → " +
                        $"ID={prefixEntry.ComponentId}, " +
                        $"Tipo={prefixEntry.ComponentType}");
                }
            }
        }

        Debug.WriteLine(
            "[PdfDocumentView] ======================================");

        if (!result.TryFindReference(
                normalizedReference,
                out BoardReferenceEntry? entry,
                out BoardGeometryIndexedComponent? component) ||
            entry is null ||
            component is null)
        {
            /*
             * No se elimina pendingReference. La búsqueda del Workspace puede
             * haber cambiado PageNumber justo antes de que el nuevo índice
             * geométrico esté disponible.
             */
            Debug.WriteLine(
                $"[PdfDocumentView] Referencia todavía no resuelta: " +
                $"'{normalizedReference}'. Página={requestedPageIndex + 1}, " +
                $"Índice={result.ReferenceIndex.Count:N0}. " +
                "La solicitud permanece pendiente.");

            return false;
        }

        pendingReference =
            null;

        Debug.WriteLine(
            $"[PdfDocumentView] Encontrada: Ref={entry.Reference}, " +
            $"ID={component.Id}, Tipo={component.Type}, " +
            $"Bounds={component.Bounds}, " +
            $"Centro=({component.CenterX:N1}, {component.CenterY:N1}).");

        if (result.CropResult is BoardGeometryCropResult crop)
        {
            Debug.WriteLine(
                $"[PdfDocumentView] Crop: {crop.SourceBounds}; " +
                $"Bitmap={crop.PixelWidth}x{crop.PixelHeight}.");
        }

        BoardGeometryBounds selectionBounds =
            component.Bounds;

        bool usesReconstructedBounds =
            result.TryGetReferenceSelectionBounds(
                normalizedReference,
                out BoardGeometryBounds reconstructedBounds);

        if (usesReconstructedBounds)
        {
            selectionBounds =
                reconstructedBounds;
        }

        double selectionCenterX =
            selectionBounds.Left +
            (selectionBounds.Width / 2D);

        double selectionCenterY =
            selectionBounds.Top +
            (selectionBounds.Height / 2D);

        Debug.WriteLine(
            $"[PdfDocumentView] Bounds de selección: {selectionBounds}; " +
            $"Reconstruido={usesReconstructedBounds}; " +
            $"Centro=({selectionCenterX:N1}, {selectionCenterY:N1}).");

        SelectComponent(
            component,
            entry,
            selectionBounds,
            selectionCenterX,
            selectionCenterY,
            centerOnComponent,
            raiseEvent: true);

        Debug.WriteLine(
            $"[PdfDocumentView] Overlay seleccionado: " +
            $"ID={selectedComponent?.Id.ToString() ?? "ninguno"}, " +
            $"Visible={SelectedComponentRectangle.Visibility}.");

        return true;
    }

    /// <summary>
    /// Busca referencias parciales dentro del documento cargado.
    /// </summary>
    public IReadOnlyList<BoardReferenceEntry> SearchReferences(
        string query,
        int maximumResults = 20)
    {
        GeometryRenderResult? result =
            geometryResult;

        return result is null
            ? Array.Empty<BoardReferenceEntry>()
            : result.SearchReferenceEntries(
                query,
                maximumResults);
    }

    /// <summary>
    /// Limpia la selección persistente y conserva únicamente el hover actual.
    /// </summary>
    public void ClearSelection()
    {
        pendingReference =
            null;

        selectedComponent =
            null;

        selectedSelectionBounds =
            null;

        selectedReferenceEntry =
            null;

        SelectedComponentRectangle.Visibility =
            Visibility.Collapsed;

        if (hoveredComponent is not null)
        {
            UpdateInformationPanel(
                hoveredComponent,
                isSelected: false,
                referenceEntry: TryResolveReferenceEntry(
                    hoveredComponent));
        }
        else
        {
            GeometryHitInfoPanel.Visibility =
                Visibility.Collapsed;
        }
    }

    private static void OnFilePathChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        var control =
            (PdfDocumentView)dependencyObject;

        _ = control.LoadDocumentAsync(
            e.NewValue as string,
            forceReload: true);
    }

    private static void OnPageNumberChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        var control =
            (PdfDocumentView)dependencyObject;

        _ = control.LoadDocumentAsync(
            control.FilePath,
            forceReload: false);
    }

    private static void OnSearchTermChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        var control =
            (PdfDocumentView)dependencyObject;

        string? reference =
            e.NewValue as string;

        if (!string.IsNullOrWhiteSpace(reference))
        {
            control.SelectReference(
                reference);
        }
    }

    private void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        UpdateEmptyState();

        if (!string.IsNullOrWhiteSpace(FilePath))
        {
            _ = LoadDocumentAsync(
                FilePath,
                forceReload: true);
        }
    }

    private void OnUnloaded(
        object sender,
        RoutedEventArgs e)
    {
        CancelCurrentRender();
        DisposeGeometryPipeline();
        ClearGeometryInteraction();
    }

    private async Task LoadDocumentAsync(
        string? filePath,
        bool forceReload)
    {
        ErrorPanel.Visibility =
            Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            ResetDocumentState();
            return;
        }

        string absolutePath =
            Path.GetFullPath(filePath);

        if (!File.Exists(absolutePath))
        {
            ShowError(
                $"El archivo no existe: {absolutePath}");
            return;
        }

        LoadingPanel.Visibility =
            Visibility.Visible;

        EmptyPanel.Visibility =
            Visibility.Collapsed;

        CancelCurrentRender();

        renderCancellation =
            new CancellationTokenSource();

        CancellationToken cancellationToken =
            renderCancellation.Token;

        try
        {
            await LoadGeometryAsync(
                absolutePath,
                forceReload,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Una navegación o cambio de página reemplazó esta operación.
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowError(
                "No se encontró Microsoft Edge WebView2 Runtime.");
        }
        catch (Exception exception)
        {
            ShowError(
                $"Error al abrir el PDF: {exception.Message}");
        }
    }

    private async Task LoadGeometryAsync(
        string absolutePath,
        bool forceReload,
        CancellationToken cancellationToken)
    {
        bool pathChanged =
            !string.Equals(
                loadedPath,
                absolutePath,
                StringComparison.OrdinalIgnoreCase);

        if (forceReload ||
            pathChanged ||
            geometryPipeline is null)
        {
            DisposeGeometryPipeline();

            geometryPipeline =
                new GeometryRenderPipeline(
                    absolutePath);

            loadedPath =
                absolutePath;
        }

        int pageIndex =
            Math.Max(1, PageNumber) - 1;

        if (pageIndex >= geometryPipeline.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PageNumber),
                PageNumber,
                $"El documento contiene {geometryPipeline.PageCount} página(s).");
        }

        GeometryRenderResult result =
            await geometryPipeline.RenderGeometryAsync(
                pageIndex,
                GeometryZoomFactor,
                cancellationToken: cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (!result.HasGeometry ||
            result.CropResult is null)
        {
            throw new InvalidOperationException(
                "No se detectó una región geométrica válida en la página.");
        }

        BitmapSource bitmap =
            CreateBitmap(
                result.CropResult);

        geometryResult =
            result;

        ClearGeometryInteraction();

        GeometryImage.Source =
            bitmap;

        GeometryImage.Cursor =
            Cursors.Cross;

        GeometryOverlayCanvas.Width =
            bitmap.PixelWidth;

        GeometryOverlayCanvas.Height =
            bitmap.PixelHeight;

        GeometryScrollViewer.Visibility =
            Visibility.Visible;

        Browser.Visibility =
            Visibility.Collapsed;

        LoadingPanel.Visibility =
            Visibility.Collapsed;

        string? referenceToSelect =
            !string.IsNullOrWhiteSpace(
                pendingReference)
                ? pendingReference
                : SearchTerm;

        if (!string.IsNullOrWhiteSpace(
                referenceToSelect))
        {
            GeometryRenderResult completedResult =
                result;

            string completedReference =
                referenceToSelect;

            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    /*
                     * Una navegación posterior puede haber sustituido este
                     * resultado antes de que el Dispatcher ejecute la acción.
                     */
                    if (!ReferenceEquals(
                            geometryResult,
                            completedResult) ||
                        completedResult.Original.PageIndex !=
                            Math.Max(1, PageNumber) - 1)
                    {
                        return;
                    }

                    SelectReference(
                        completedReference,
                        centerOnComponent: true);
                }));
        }
    }

    /// <summary>
    /// Convierte la posición visual dentro de la imagen recortada en una
    /// coordenada absoluta del render original.
    /// </summary>
    private bool TryConvertMouseToRenderCoordinates(
        MouseEventArgs e,
        out double renderX,
        out double renderY)
    {
        renderX = 0D;
        renderY = 0D;

        GeometryRenderResult? result =
            geometryResult;

        BoardGeometryCropResult? crop =
            result?.CropResult;

        if (result is null ||
            crop is null ||
            GeometryImage.Source is not BitmapSource bitmap ||
            GeometryImage.ActualWidth <= 0D ||
            GeometryImage.ActualHeight <= 0D)
        {
            return false;
        }

        Point imagePoint =
            e.GetPosition(
                GeometryImage);

        if (imagePoint.X < 0D ||
            imagePoint.Y < 0D ||
            imagePoint.X >= GeometryImage.ActualWidth ||
            imagePoint.Y >= GeometryImage.ActualHeight)
        {
            return false;
        }

        double cropPixelX =
            imagePoint.X *
            bitmap.PixelWidth /
            GeometryImage.ActualWidth;

        double cropPixelY =
            imagePoint.Y *
            bitmap.PixelHeight /
            GeometryImage.ActualHeight;

        renderX =
            crop.SourceBounds.Left +
            cropPixelX;

        renderY =
            crop.SourceBounds.Top +
            cropPixelY;

        return renderX >= 0D &&
               renderY >= 0D &&
               renderX < result.Original.Image.PixelWidth &&
               renderY < result.Original.Image.PixelHeight;
    }

    private void OnGeometryImageMouseMove(
        object sender,
        MouseEventArgs e)
    {
        GeometryRenderResult? result =
            geometryResult;

        if (result is null ||
            !TryConvertMouseToRenderCoordinates(
                e,
                out double renderX,
                out double renderY))
        {
            SetHoveredComponent(
                null,
                0D,
                0D);

            return;
        }

        bool found =
            result.TryHitTest(
                renderX,
                renderY,
                out BoardGeometryIndexedComponent? component);

        SetHoveredComponent(
            found
                ? component
                : null,
            renderX,
            renderY);
    }

    private void OnGeometryImageMouseLeave(
        object sender,
        MouseEventArgs e)
    {
        SetHoveredComponent(
            null,
            0D,
            0D);
    }

    private void OnGeometryImageMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        GeometryRenderResult? result =
            geometryResult;

        if (result is null ||
            !TryConvertMouseToRenderCoordinates(
                e,
                out double renderX,
                out double renderY) ||
            !result.TryHitTest(
                renderX,
                renderY,
                out BoardGeometryIndexedComponent? component) ||
            component is null)
        {
            return;
        }

        SelectComponent(
            component,
            TryResolveReferenceEntry(
                component),
            component.Bounds,
            renderX,
            renderY,
            centerOnComponent: false,
            raiseEvent: true);

        e.Handled =
            true;
    }

    private void SetHoveredComponent(
        BoardGeometryIndexedComponent? component,
        double renderX,
        double renderY)
    {
        if (hoveredComponent?.Id ==
            component?.Id)
        {
            return;
        }

        hoveredComponent =
            component;

        if (component is null)
        {
            HoveredComponentRectangle.Visibility =
                Visibility.Collapsed;

            if (selectedComponent is null)
            {
                GeometryHitInfoPanel.Visibility =
                    Visibility.Collapsed;
            }

            return;
        }

        UpdateOverlayRectangle(
            HoveredComponentRectangle,
            component);

        UpdateInformationPanel(
            component,
            isSelected: false,
            referenceEntry: TryResolveReferenceEntry(
                component));

        GeometryComponentHovered?.Invoke(
            this,
            new BoardGeometryComponentEventArgs(
                component,
                renderX,
                renderY));
    }

    /// <summary>
    /// Aplica una selección persistente procedente del mouse o del índice de
    /// referencias.
    /// </summary>
    private void SelectComponent(
        BoardGeometryIndexedComponent component,
        BoardReferenceEntry? referenceEntry,
        BoardGeometryBounds selectionBounds,
        double renderX,
        double renderY,
        bool centerOnComponent,
        bool raiseEvent)
    {
        ArgumentNullException.ThrowIfNull(
            component);

        selectedComponent =
            component;

        selectedSelectionBounds =
            selectionBounds;

        selectedReferenceEntry =
            referenceEntry;

        UpdateOverlayRectangle(
            SelectedComponentRectangle,
            selectionBounds);

        UpdateInformationPanel(
            component,
            isSelected: true,
            referenceEntry);

        if (centerOnComponent)
        {
            CenterOnBounds(
                selectionBounds);
        }

        if (raiseEvent)
        {
            GeometryComponentSelected?.Invoke(
                this,
                new BoardGeometryComponentEventArgs(
                    component,
                    renderX,
                    renderY));
        }
    }

    /// <summary>
    /// Resuelve la referencia principal asociada a un componente.
    /// </summary>
    private BoardReferenceEntry? TryResolveReferenceEntry(
        BoardGeometryIndexedComponent component)
    {
        GeometryRenderResult? result =
            geometryResult;

        if (result is not null &&
            result.TryGetReferenceByComponentId(
                component.Id,
                out BoardReferenceEntry? entry))
        {
            return entry;
        }

        return null;
    }

    /// <summary>
    /// Centra el ScrollViewer sobre el componente seleccionado.
    /// </summary>
    private void CenterOnBounds(
        BoardGeometryBounds bounds)
    {
        BoardGeometryCropResult? crop =
            geometryResult?.CropResult;

        if (crop is null)
        {
            Debug.WriteLine(
                "[PdfDocumentView] CenterOnBounds cancelado: no existe CropResult.");

            return;
        }

        double boundsCenterX =
            bounds.Left +
            (bounds.Width / 2D);

        double boundsCenterY =
            bounds.Top +
            (bounds.Height / 2D);

        double cropCenterX =
            boundsCenterX -
            crop.SourceBounds.Left;

        double cropCenterY =
            boundsCenterY -
            crop.SourceBounds.Top;

        bool boundsCenterInsideCrop =
            cropCenterX >= 0D &&
            cropCenterY >= 0D &&
            cropCenterX <= crop.PixelWidth &&
            cropCenterY <= crop.PixelHeight;

        Debug.WriteLine(
            $"[PdfDocumentView] Centro lógico relativo al crop: " +
            $"({cropCenterX:N1}, {cropCenterY:N1}); " +
            $"dentro={boundsCenterInsideCrop}; Bounds={bounds}.");

        if (!boundsCenterInsideCrop)
        {
            Debug.WriteLine(
                "[PdfDocumentView] El centro lógico está fuera del recorte actual; " +
                "no puede centrarse con este bitmap.");

            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                GeometryScrollViewer.UpdateLayout();
                GeometryOverlayCanvas.UpdateLayout();

                const double contentPadding = 20D;

                double horizontalOffset =
                    contentPadding +
                    cropCenterX -
                    (GeometryScrollViewer.ViewportWidth / 2D);

                double verticalOffset =
                    contentPadding +
                    cropCenterY -
                    (GeometryScrollViewer.ViewportHeight / 2D);

                double constrainedHorizontalOffset =
                    Math.Max(
                        0D,
                        Math.Min(
                            GeometryScrollViewer.ScrollableWidth,
                            horizontalOffset));

                double constrainedVerticalOffset =
                    Math.Max(
                        0D,
                        Math.Min(
                            GeometryScrollViewer.ScrollableHeight,
                            verticalOffset));

                Debug.WriteLine(
                    $"[PdfDocumentView] Viewport antes: " +
                    $"H={GeometryScrollViewer.HorizontalOffset:N1}, " +
                    $"V={GeometryScrollViewer.VerticalOffset:N1}, " +
                    $"VW={GeometryScrollViewer.ViewportWidth:N1}, " +
                    $"VH={GeometryScrollViewer.ViewportHeight:N1}, " +
                    $"SW={GeometryScrollViewer.ScrollableWidth:N1}, " +
                    $"SH={GeometryScrollViewer.ScrollableHeight:N1}.");

                GeometryScrollViewer.ScrollToHorizontalOffset(
                    constrainedHorizontalOffset);

                GeometryScrollViewer.ScrollToVerticalOffset(
                    constrainedVerticalOffset);

                GeometryScrollViewer.UpdateLayout();

                if (selectedSelectionBounds is BoardGeometryBounds currentBounds)
                {
                    UpdateOverlayRectangle(
                        SelectedComponentRectangle,
                        currentBounds);
                }

                Debug.WriteLine(
                    $"[PdfDocumentView] Viewport después: " +
                    $"H={GeometryScrollViewer.HorizontalOffset:N1}, " +
                    $"V={GeometryScrollViewer.VerticalOffset:N1}; " +
                    $"Overlay={SelectedComponentRectangle.Visibility}, " +
                    $"Left={Canvas.GetLeft(SelectedComponentRectangle):N1}, " +
                    $"Top={Canvas.GetTop(SelectedComponentRectangle):N1}, " +
                    $"W={SelectedComponentRectangle.Width:N1}, " +
                    $"H={SelectedComponentRectangle.Height:N1}.");
            }));
    }

    /// <summary>
    /// Posiciona un rectángulo WPF sobre los límites del componente.
    /// </summary>
    private void UpdateOverlayRectangle(
        Rectangle rectangle,
        BoardGeometryIndexedComponent component)
    {
        ArgumentNullException.ThrowIfNull(
            component);

        UpdateOverlayRectangle(
            rectangle,
            component.Bounds);
    }

    /// <summary>
    /// Posiciona un rectángulo WPF sobre límites geométricos arbitrarios.
    /// </summary>
    private void UpdateOverlayRectangle(
        Rectangle rectangle,
        BoardGeometryBounds bounds)
    {
        BoardGeometryCropResult? crop =
            geometryResult?.CropResult;

        if (crop is null)
        {
            rectangle.Visibility =
                Visibility.Collapsed;

            return;
        }

        double left =
            bounds.Left -
            crop.SourceBounds.Left;

        double top =
            bounds.Top -
            crop.SourceBounds.Top;

        double right =
            bounds.Right -
            crop.SourceBounds.Left;

        double bottom =
            bounds.Bottom -
            crop.SourceBounds.Top;

        double clippedLeft =
            Math.Max(
                0D,
                left);

        double clippedTop =
            Math.Max(
                0D,
                top);

        double clippedRight =
            Math.Min(
                crop.PixelWidth,
                right);

        double clippedBottom =
            Math.Min(
                crop.PixelHeight,
                bottom);

        double width =
            clippedRight -
            clippedLeft;

        double height =
            clippedBottom -
            clippedTop;

        if (width <= 0D ||
            height <= 0D)
        {
            rectangle.Visibility =
                Visibility.Collapsed;

            return;
        }

        Canvas.SetLeft(
            rectangle,
            clippedLeft);

        Canvas.SetTop(
            rectangle,
            clippedTop);

        rectangle.Width =
            Math.Max(
                2D,
                width);

        rectangle.Height =
            Math.Max(
                2D,
                height);

        rectangle.Visibility =
            Visibility.Visible;
    }

    private void UpdateInformationPanel(
        BoardGeometryIndexedComponent component,
        bool isSelected,
        BoardReferenceEntry? referenceEntry)
    {
        string referenceText =
            referenceEntry is null
                ? string.Empty
                : $" · {referenceEntry.Reference}";

        GeometryHitInfoTitle.Text =
            isSelected
                ? $"SELECCIONADO{referenceText} · {component.Type}"
                : referenceEntry is null
                    ? component.Type.ToString().ToUpperInvariant()
                    : $"{referenceEntry.Reference} · " +
                      component.Type.ToString().ToUpperInvariant();

        string associationText =
            referenceEntry is null
                ? string.Empty
                : $"\nReferencia: {referenceEntry.Reference}" +
                  $"\nAsociación: {referenceEntry.Confidence:P1} · " +
                  $"{referenceEntry.AssociationRule}";

        GeometryHitInfoText.Text =
            $"ID: {component.Id}\n" +
            $"Confianza: {component.Confidence:P1}\n" +
            $"Centro: {component.CenterX:N1}, {component.CenterY:N1}\n" +
            $"Bounds: X={component.Bounds.Left}, Y={component.Bounds.Top}, " +
            $"W={component.Bounds.Width}, H={component.Bounds.Height}\n" +
            $"Píxeles: {component.PixelCount:N0}" +
            associationText;

        GeometryHitInfoPanel.Visibility =
            Visibility.Visible;
    }

    /// <summary>
    /// Normaliza una referencia para que las solicitudes pendientes y el
    /// ReferenceIndex utilicen exactamente el mismo formato.
    /// </summary>
    private static string NormalizeReference(
        string? reference)
    {
        return string.IsNullOrWhiteSpace(
                reference)
            ? string.Empty
            : reference
                .Trim()
                .Replace(
                    " ",
                    string.Empty,
                    StringComparison.Ordinal)
                .ToUpperInvariant();
    }

    private static BitmapSource CreateBitmap(
        BoardGeometryCropResult crop)
    {
        byte[] pixels =
            crop.ToArray();

        var bitmap =
            BitmapSource.Create(
                crop.PixelWidth,
                crop.PixelHeight,
                96D,
                96D,
                PixelFormats.Bgra32,
                palette: null,
                pixels,
                crop.Stride);

        bitmap.Freeze();
        return bitmap;
    }

    private async Task LoadBrowserAsync(
        string absolutePath,
        bool forceReload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!isBrowserInitialized)
        {
            await Browser.EnsureCoreWebView2Async();

            ConfigureBrowser();
            displayScaleSynchronizer.Synchronize();

            isBrowserInitialized = true;
        }

        cancellationToken.ThrowIfCancellationRequested();

        int page =
            Math.Max(1, PageNumber);

        bool pathChanged =
            !string.Equals(
                loadedPath,
                absolutePath,
                StringComparison.OrdinalIgnoreCase);

        string source =
            BuildViewerUri(
                absolutePath,
                page,
                SearchTerm);

        if (forceReload ||
            pathChanged ||
            Browser.Source is null)
        {
            loadedPath =
                absolutePath;
        }

        GeometryScrollViewer.Visibility =
            Visibility.Collapsed;

        Browser.Visibility =
            Visibility.Visible;

        Browser.Source =
            new Uri(
                source,
                UriKind.Absolute);
    }

    private static string BuildViewerUri(
        string absolutePath,
        int pageNumber,
        string? searchTerm)
    {
        string fileUri =
            new Uri(
                absolutePath,
                UriKind.Absolute).AbsoluteUri;

        string pageFragment =
            $"page={Math.Max(1, pageNumber)}";

        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return $"{fileUri}#{pageFragment}";
        }

        return
            $"{fileUri}#{pageFragment}" +
            $"&search={Uri.EscapeDataString(searchTerm.Trim())}";
    }

    private void ConfigureBrowser()
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled =
            true;

        Browser.CoreWebView2.Settings.AreDevToolsEnabled =
            false;

        Browser.CoreWebView2.Settings.IsStatusBarEnabled =
            false;

        Browser.CoreWebView2.Settings.IsZoomControlEnabled =
            true;

        Browser.ZoomFactor =
            1D;
    }

    private void OnLayoutUpdated(
        object? sender,
        EventArgs e)
    {
        if (!isBrowserInitialized ||
            Browser.Visibility != Visibility.Visible)
        {
            return;
        }

        displayScaleSynchronizer.Synchronize();
    }

    private void OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        LoadingPanel.Visibility =
            Visibility.Collapsed;

        if (!e.IsSuccess)
        {
            ShowError(
                $"WebView2 no pudo cargar el documento. Código: {e.WebErrorStatus}.");
        }
    }

    private void UpdateEmptyState()
    {
        bool hasDocument =
            !string.IsNullOrWhiteSpace(FilePath);

        EmptyPanel.Visibility =
            hasDocument
                ? Visibility.Collapsed
                : Visibility.Visible;

        if (!hasDocument)
        {
            GeometryScrollViewer.Visibility =
                Visibility.Collapsed;

            Browser.Visibility =
                Visibility.Collapsed;
        }
    }

    private void ResetDocumentState()
    {
        CancelCurrentRender();
        DisposeGeometryPipeline();
        ClearGeometryInteraction();

        loadedPath =
            null;

        pendingReference =
            null;

        geometryResult =
            null;

        GeometryImage.Source =
            null;

        GeometryScrollViewer.Visibility =
            Visibility.Collapsed;

        Browser.Visibility =
            Visibility.Collapsed;

        LoadingPanel.Visibility =
            Visibility.Collapsed;

        EmptyPanel.Visibility =
            Visibility.Visible;
    }

    private void ClearGeometryInteraction()
    {
        hoveredComponent =
            null;

        selectedComponent =
            null;

        selectedSelectionBounds =
            null;

        selectedReferenceEntry =
            null;

        HoveredComponentRectangle.Visibility =
            Visibility.Collapsed;

        SelectedComponentRectangle.Visibility =
            Visibility.Collapsed;

        GeometryHitInfoPanel.Visibility =
            Visibility.Collapsed;
    }

    private void CancelCurrentRender()
    {
        CancellationTokenSource? cancellation =
            renderCancellation;

        renderCancellation =
            null;

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void DisposeGeometryPipeline()
    {
        GeometryRenderPipeline? pipeline =
            geometryPipeline;

        geometryPipeline =
            null;

        pipeline?.Dispose();
    }

    private void ShowError(
        string message)
    {
        pendingReference =
            null;

        ClearGeometryInteraction();

        geometryResult =
            null;

        LoadingPanel.Visibility =
            Visibility.Collapsed;

        EmptyPanel.Visibility =
            Visibility.Collapsed;

        GeometryScrollViewer.Visibility =
            Visibility.Collapsed;

        Browser.Visibility =
            Visibility.Collapsed;

        ErrorMessage.Text =
            message;

        ErrorPanel.Visibility =
            Visibility.Visible;
    }
}

/// <summary>
/// Datos de un componente detectado mediante interacción con el visor.
/// </summary>
public sealed class BoardGeometryComponentEventArgs : EventArgs
{
    public BoardGeometryComponentEventArgs(
        BoardGeometryIndexedComponent component,
        double renderX,
        double renderY)
    {
        ArgumentNullException.ThrowIfNull(component);

        Component = component;
        RenderX = renderX;
        RenderY = renderY;
    }

    public BoardGeometryIndexedComponent Component { get; }

    public double RenderX { get; }

    public double RenderY { get; }
}
