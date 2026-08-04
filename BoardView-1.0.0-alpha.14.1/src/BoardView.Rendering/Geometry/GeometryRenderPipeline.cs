using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BoardView.Core.Pdf;
using BoardView.Formats.Pdf;
using BoardView.Rendering.Recognition;

namespace BoardView.Rendering.Geometry;

/// <summary>
/// Coordina el renderizado completo de una página PDF y su procesamiento
/// geométrico automático.
/// </summary>
/// <remarks>
/// Flujo ejecutado:
///
/// <list type="number">
/// <item>Renderiza la página completa mediante PDFium.</item>
/// <item>Genera una máscara binaria oscura y cromáticamente neutra.</item>
/// <item>Detecta todos los componentes conectados.</item>
/// <item>Clasifica todos los componentes detectados.</item>
/// <item>Construye un índice geométrico y espacial inmutable.</item>
/// <item>Crea el motor de búsqueda espacial asociado al índice.</item>
/// <item>Extrae las palabras nativas de la página PDF.</item>
/// <item>Convierte las palabras en observaciones del render.</item>
/// <item>Detecta referencias electrónicas y las asocia con la geometría.</item>
/// <item>Construye un índice bidireccional de referencias.</item>
/// <item>Selecciona y agrupa los componentes relevantes.</item>
/// <item>Calcula los límites combinados de la región útil.</item>
/// <item>Recorta la imagen original usando esos límites.</item>
/// </list>
///
/// La clase no contiene dependencias de WPF ni lógica de interfaz.
/// </remarks>
public sealed class GeometryRenderPipeline : IDisposable
{
    private const double PdfPointsPerInch = 72D;
    private const double BasePixelsPerInch = 96D;

    private readonly PdfiumDocumentRenderer _documentRenderer;
    private readonly bool _ownsDocumentRenderer;
    private readonly BoardGeometryCropper _cropper;
    private readonly BoardGeometryConnectedComponents _connectedComponents;
    private readonly BoardGeometryComponentClassifier _componentClassifier;
    private readonly BoardGeometryComponentSelector _componentSelector;
    private readonly BoardPdfTextObservationFactory _textObservationFactory;
    private readonly BoardReferenceDetector _referenceDetector;
    private readonly BoardReferenceAssociationEngine _boardReferenceAssociationEngine;
    private readonly SchematicReferenceAssociationEngine _schematicReferenceAssociationEngine;
    private readonly SchematicSymbolAssembler _schematicSymbolAssembler;
    private readonly string? _documentFilePath;
    private readonly object _documentIndexSyncRoot = new();

    private PdfDocumentIndex? _documentIndex;
    private bool _documentIndexAttempted;
    private bool _disposed;

    /// <summary>
    /// Inicializa el pipeline y abre el documento PDF indicado.
    /// </summary>
    public GeometryRenderPipeline(string filePath)
        : this(
            new PdfiumDocumentRenderer(filePath),
            documentFilePath: filePath,
            documentIndex: null,
            ownsDocumentRenderer: true)
    {
    }

    /// <summary>
    /// Inicializa el pipeline utilizando un renderizador PDF existente.
    /// </summary>
    /// <remarks>
    /// Cuando no se proporciona la ruta ni un índice textual, el pipeline
    /// conserva el comportamiento geométrico completo, pero las colecciones
    /// de observaciones y referencias automáticas permanecen vacías.
    /// </remarks>
    public GeometryRenderPipeline(
        PdfiumDocumentRenderer documentRenderer,
        bool ownsDocumentRenderer = false)
        : this(
            documentRenderer,
            documentFilePath: null,
            documentIndex: null,
            ownsDocumentRenderer)
    {
    }

    /// <summary>
    /// Inicializa el pipeline con un renderizador y un índice textual externo.
    /// </summary>
    public GeometryRenderPipeline(
        PdfiumDocumentRenderer documentRenderer,
        PdfDocumentIndex documentIndex,
        bool ownsDocumentRenderer = false)
        : this(
            documentRenderer,
            documentIndex?.FilePath,
            documentIndex,
            ownsDocumentRenderer)
    {
    }

    /// <summary>
    /// Constructor central del pipeline.
    /// </summary>
    private GeometryRenderPipeline(
        PdfiumDocumentRenderer documentRenderer,
        string? documentFilePath,
        PdfDocumentIndex? documentIndex,
        bool ownsDocumentRenderer)
    {
        ArgumentNullException.ThrowIfNull(documentRenderer);

        _documentRenderer = documentRenderer;
        _ownsDocumentRenderer = ownsDocumentRenderer;
        _documentFilePath =
            string.IsNullOrWhiteSpace(documentFilePath)
                ? null
                : Path.GetFullPath(documentFilePath);

        _documentIndex = documentIndex;
        _documentIndexAttempted = documentIndex is not null;

        _cropper =
            new BoardGeometryCropper();

        _connectedComponents =
            new BoardGeometryConnectedComponents();

        _componentClassifier =
            new BoardGeometryComponentClassifier();

        _componentSelector =
            new BoardGeometryComponentSelector();

        _textObservationFactory =
            new BoardPdfTextObservationFactory();

        _referenceDetector =
            new BoardReferenceDetector();

        _boardReferenceAssociationEngine =
            new BoardReferenceAssociationEngine();

        _schematicReferenceAssociationEngine =
            new SchematicReferenceAssociationEngine();

        _schematicSymbolAssembler =
            new SchematicSymbolAssembler();
    }

    /// <summary>
    /// Obtiene la cantidad de páginas del documento.
    /// </summary>
    public int PageCount
    {
        get
        {
            ThrowIfDisposed();
            return _documentRenderer.PageCount;
        }
    }

    /// <summary>
    /// Renderiza una página completa sin aplicar análisis geométrico.
    /// </summary>
    public Task<GeometryPageRenderResult> RenderOriginalAsync(
        int pageIndex,
        double zoomFactor,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateZoomFactor(zoomFactor);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => RenderOriginal(
                pageIndex,
                zoomFactor,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Renderiza una página y ejecuta el pipeline geométrico completo.
    /// </summary>
    public Task<GeometryRenderResult> RenderGeometryAsync(
        int pageIndex,
        double zoomFactor,
        GeometryRenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateZoomFactor(zoomFactor);

        GeometryRenderOptions effectiveOptions =
            options ?? GeometryRenderOptions.Default;

        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => RenderGeometry(
                pageIndex,
                zoomFactor,
                effectiveOptions,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Libera los recursos propiedad del pipeline.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsDocumentRenderer)
        {
            _documentRenderer.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Renderiza la página completa en formato BGRA32.
    /// </summary>
    private GeometryPageRenderResult RenderOriginal(
        int pageIndex,
        double zoomFactor,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        ValidatePageIndex(pageIndex);

        PdfiumPageSize pageSize =
            _documentRenderer.GetPageSize(pageIndex);

        GeometryPagePixelSize pixelSize =
            CalculateRenderedPageSize(
                pageSize,
                zoomFactor);

        cancellationToken.ThrowIfCancellationRequested();

        PdfiumRenderResult renderResult =
            _documentRenderer.RenderRegion(
                pageIndex: pageIndex,
                pagePixelWidth: pixelSize.Width,
                pagePixelHeight: pixelSize.Height,
                regionX: 0,
                regionY: 0,
                regionWidth: pixelSize.Width,
                regionHeight: pixelSize.Height,
                cancellationToken: cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        return new GeometryPageRenderResult(
            pageIndex,
            zoomFactor,
            pageSize,
            renderResult);
    }

    /// <summary>
    /// Ejecuta el pipeline:
    ///
    /// Render
    /// → Mask
    /// → Connected Components
    /// → Component Classifier
    /// → Geometry Index
    /// → Spatial Search Engine
    /// → PDF Text Observations
    /// → Reference Detector
    /// → Reference Association
    /// → Reference Index
    /// → Component Selector
    /// → Bounding Box final
    /// → Crop.
    /// </summary>
    private GeometryRenderResult RenderGeometry(
        int pageIndex,
        double zoomFactor,
        GeometryRenderOptions options,
        CancellationToken cancellationToken)
    {
        GeometryPageRenderResult original =
            RenderOriginal(
                pageIndex,
                zoomFactor,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        PdfiumRenderResult image =
            original.Image;

        BoardGeometryMask mask =
            BoardGeometryMask.CreateFromBgra32(
                image.PixelData,
                image.PixelWidth,
                image.PixelHeight,
                image.Stride,
                options.DarkChannelThreshold,
                options.MinimumAlpha,
                options.MaximumChannelDifference);

        cancellationToken.ThrowIfCancellationRequested();

        BoardGeometryComponentsResult components =
            _connectedComponents.Analyze(mask);

        cancellationToken.ThrowIfCancellationRequested();

        BoardGeometryComponentClassificationResult classification =
            _componentClassifier.Classify(
                components,
                options.ComponentClassifierOptions);

        cancellationToken.ThrowIfCancellationRequested();

        BoardGeometryIndex geometryIndex =
            new(
                classification,
                options.GeometryIndexOptions);

        cancellationToken.ThrowIfCancellationRequested();

        BoardGeometrySpatialSearchEngine spatialSearch =
            new(
                geometryIndex);

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<BoardTextObservation> textObservations =
            CreateTextObservations(
                original,
                options,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<BoardReferenceCandidate> detectedCandidates =
            _referenceDetector.Detect(
                textObservations,
                options.ReferenceDetectorOptions,
                cancellationToken);

        IReadOnlyList<BoardReferenceCandidate> referenceCandidates =
            MergeReferenceCandidates(
                detectedCandidates,
                options.ReferenceCandidates);

        cancellationToken.ThrowIfCancellationRequested();

        GeometryDocumentRole documentRole =
            ResolveDocumentRole(
                options.DocumentRole);

        BoardReferenceAssociationResult referenceAssociation;
        SchematicSymbolAssemblyResult schematicSymbols;

        if (documentRole == GeometryDocumentRole.Schematic)
        {
            var electricalGraphBuilder =
                new SchematicElectricalGraphBuilder();

            SchematicElectricalGraph electricalGraph =
                electricalGraphBuilder.Build(
                    geometryIndex,
                    textObservations,
                    options.SchematicSymbolAssemblerOptions
                        .ElectricalGraphBuilderOptions,
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            SchematicReferenceAnchorResult referenceAnchors =
                _schematicReferenceAssociationEngine.Anchor(
                    electricalGraph,
                    referenceCandidates,
                    options.ReferenceAssociationOptions,
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            referenceAssociation =
                _schematicReferenceAssociationEngine.CreateAssociationResult(
                    referenceCandidates,
                    referenceAnchors);

            schematicSymbols =
                _schematicSymbolAssembler.Assemble(
                    geometryIndex,
                    electricalGraph,
                    referenceAnchors,
                    options.SchematicSymbolAssemblerOptions,
                    cancellationToken);
        }
        else
        {
            referenceAssociation =
                _boardReferenceAssociationEngine.Associate(
                    geometryIndex,
                    referenceCandidates,
                    options.ReferenceAssociationOptions,
                    cancellationToken);

            schematicSymbols =
                SchematicSymbolAssemblyResult.Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();

        BoardReferenceIndex referenceIndex =
            new(
                referenceAssociation);

        cancellationToken.ThrowIfCancellationRequested();

        BoardGeometryComponentSelectionResult selection =
            _componentSelector.Select(
                classification,
                options.ComponentSelectorOptions);

        cancellationToken.ThrowIfCancellationRequested();

        BoardGeometryAnalysisResult analysis =
            CreateAnalysisResult(selection);

        BoardGeometryCropResult? cropResult = null;

        if (analysis.HasGeometry)
        {
            cropResult =
                _cropper.Crop(
                    image.PixelData,
                    image.PixelWidth,
                    image.PixelHeight,
                    image.Stride,
                    analysis.Bounds);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new GeometryRenderResult(
            original,
            mask,
            components,
            classification,
            geometryIndex,
            spatialSearch,
            textObservations,
            referenceCandidates,
            referenceAssociation,
            referenceIndex,
            schematicSymbols,
            selection,
            analysis,
            cropResult);
    }

    /// <summary>
    /// Resuelve el tipo de documento utilizado para seleccionar el motor de
    /// asociación de referencias.
    /// </summary>
    private GeometryDocumentRole ResolveDocumentRole(
        GeometryDocumentRole requestedRole)
    {
        if (requestedRole !=
            GeometryDocumentRole.Auto)
        {
            return requestedRole;
        }

        if (string.IsNullOrWhiteSpace(
                _documentFilePath))
        {
            return GeometryDocumentRole.Board;
        }

        string fileName =
            Path.GetFileNameWithoutExtension(
                _documentFilePath);

        if (fileName.Contains(
                "schematic",
                StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(
                "schema",
                StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(
                "esquematico",
                StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(
                "esquemático",
                StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(
                "circuit",
                StringComparison.OrdinalIgnoreCase))
        {
            return GeometryDocumentRole.Schematic;
        }

        return GeometryDocumentRole.Board;
    }

    /// <summary>
    /// Obtiene la página textual correspondiente y transforma sus palabras en
    /// observaciones expresadas en píxeles del render original.
    /// </summary>
    private IReadOnlyList<BoardTextObservation> CreateTextObservations(
        GeometryPageRenderResult original,
        GeometryRenderOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.EnableNativePdfTextExtraction)
        {
            return Array.Empty<BoardTextObservation>();
        }

        PdfDocumentIndex? documentIndex =
            GetOrBuildDocumentIndex(
                cancellationToken);

        if (documentIndex is null ||
            original.PageIndex < 0 ||
            original.PageIndex >= documentIndex.Pages.Count)
        {
            return Array.Empty<BoardTextObservation>();
        }

        PdfPage page =
            documentIndex.Pages[original.PageIndex];

        if (page.WidthPoints <= 0D ||
            page.HeightPoints <= 0D ||
            page.Words.Count == 0)
        {
            return Array.Empty<BoardTextObservation>();
        }

        return _textObservationFactory.Create(
            page,
            original.Image.PixelWidth,
            original.Image.PixelHeight,
            original.PageIndex,
            options.TextObservationOptions,
            cancellationToken);
    }

    /// <summary>
    /// Construye una sola vez el índice textual seguro del documento mediante PDFium.
    /// </summary>
    private PdfDocumentIndex? GetOrBuildDocumentIndex(
        CancellationToken cancellationToken)
    {
        if (_documentIndexAttempted)
        {
            return _documentIndex;
        }

        lock (_documentIndexSyncRoot)
        {
            if (_documentIndexAttempted)
            {
                return _documentIndex;
            }

            cancellationToken.ThrowIfCancellationRequested();

            _documentIndexAttempted = true;

            if (string.IsNullOrWhiteSpace(_documentFilePath))
            {
                return null;
            }

            try
            {
                ISafePdfDocumentIndexer indexer =
                    new SafePdfDocumentIndexer();

                SafePdfIndexResult indexResult =
                    indexer
                        .BuildIndexAsync(
                            _documentFilePath,
                            cancellationToken)
                        .GetAwaiter()
                        .GetResult();

                _documentIndex =
                    indexResult.Index;
            }
            catch (OperationCanceledException)
            {
                _documentIndexAttempted = false;
                throw;
            }
            catch
            {
                // El texto nativo es una capacidad adicional. Un PDF sin
                // índice textual válido no debe impedir el análisis geométrico.
                _documentIndex =
                    null;
            }

            return _documentIndex;
        }
    }

    /// <summary>
    /// Combina referencias detectadas automáticamente con candidatos externos,
    /// reasignando identificadores para garantizar unicidad.
    /// </summary>
    private static IReadOnlyList<BoardReferenceCandidate> MergeReferenceCandidates(
        IReadOnlyList<BoardReferenceCandidate> detectedCandidates,
        IReadOnlyList<BoardReferenceCandidate> externalCandidates)
    {
        ArgumentNullException.ThrowIfNull(detectedCandidates);
        ArgumentNullException.ThrowIfNull(externalCandidates);

        if (detectedCandidates.Count == 0 &&
            externalCandidates.Count == 0)
        {
            return Array.Empty<BoardReferenceCandidate>();
        }

        var merged =
            new List<BoardReferenceCandidate>(
                detectedCandidates.Count +
                externalCandidates.Count);

        var duplicateKeys =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        AddCandidates(
            detectedCandidates,
            merged,
            duplicateKeys);

        AddCandidates(
            externalCandidates,
            merged,
            duplicateKeys);

        return merged;
    }

    /// <summary>
    /// Añade candidatos evitando duplicados equivalentes.
    /// </summary>
    private static void AddCandidates(
        IEnumerable<BoardReferenceCandidate> source,
        ICollection<BoardReferenceCandidate> destination,
        ISet<string> duplicateKeys)
    {
        foreach (BoardReferenceCandidate candidate in source)
        {
            string duplicateKey =
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{candidate.PageIndex}:{candidate.NormalizedReference}:" +
                    $"{candidate.Bounds.Left}:{candidate.Bounds.Top}:" +
                    $"{candidate.Bounds.Width}:{candidate.Bounds.Height}");

            if (!duplicateKeys.Add(duplicateKey))
            {
                continue;
            }

            destination.Add(
                new BoardReferenceCandidate(
                    destination.Count,
                    candidate.RawText,
                    candidate.Bounds,
                    candidate.Confidence,
                    candidate.PageIndex,
                    candidate.RotationDegrees,
                    candidate.SourceId));
        }
    }

    /// <summary>
    /// Convierte la selección agrupada en el contrato de análisis usado por
    /// el recortador y por la interfaz.
    /// </summary>
    private static BoardGeometryAnalysisResult CreateAnalysisResult(
        BoardGeometryComponentSelectionResult selection)
    {
        if (!selection.HasSelection)
        {
            return BoardGeometryAnalysisResult.Empty;
        }

        return new BoardGeometryAnalysisResult(
            selection.Bounds,
            selection.SelectedPixelCount);
    }

    /// <summary>
    /// Convierte las dimensiones físicas PDF en píxeles.
    /// </summary>
    private static GeometryPagePixelSize CalculateRenderedPageSize(
        PdfiumPageSize pageSize,
        double zoomFactor)
    {
        double pixelScale =
            BasePixelsPerInch /
            PdfPointsPerInch *
            zoomFactor;

        int width =
            ConvertRenderedDimension(
                pageSize.Width,
                pixelScale,
                nameof(pageSize.Width));

        int height =
            ConvertRenderedDimension(
                pageSize.Height,
                pixelScale,
                nameof(pageSize.Height));

        return new GeometryPagePixelSize(
            width,
            height);
    }

    /// <summary>
    /// Convierte una dimensión PDF en una dimensión entera de píxeles.
    /// </summary>
    private static int ConvertRenderedDimension(
        double pointDimension,
        double pixelScale,
        string parameterName)
    {
        if (!double.IsFinite(pointDimension) ||
            pointDimension <= 0D)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                pointDimension,
                "La dimensión física de la página no es válida.");
        }

        double pixelDimension =
            pointDimension *
            pixelScale;

        if (!double.IsFinite(pixelDimension) ||
            pixelDimension <= 0D ||
            pixelDimension > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                pixelDimension,
                "La dimensión renderizada queda fuera del rango permitido.");
        }

        return checked(
            (int)Math.Ceiling(pixelDimension));
    }

    /// <summary>
    /// Valida el factor de zoom.
    /// </summary>
    private static void ValidateZoomFactor(
        double zoomFactor)
    {
        if (!double.IsFinite(zoomFactor) ||
            zoomFactor <= 0D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(zoomFactor),
                zoomFactor,
                "El factor de zoom debe ser finito y positivo.");
        }
    }

    /// <summary>
    /// Valida el índice de página.
    /// </summary>
    private void ValidatePageIndex(
        int pageIndex)
    {
        if (pageIndex < 0 ||
            pageIndex >= _documentRenderer.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                pageIndex,
                $"El índice debe estar entre 0 y {_documentRenderer.PageCount - 1}.");
        }
    }

    /// <summary>
    /// Impide utilizar el pipeline después de liberarlo.
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }
}

/// <summary>
/// Tipo lógico de documento utilizado para elegir el motor de asociación.
/// </summary>
public enum GeometryDocumentRole
{
    /// <summary>
    /// Detecta el tipo de documento mediante su nombre de archivo.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Documento de placa o boardview físico.
    /// </summary>
    Board = 1,

    /// <summary>
    /// Documento de diagrama esquemático.
    /// </summary>
    Schematic = 2
}

/// <summary>
/// Opciones del pipeline geométrico.
/// </summary>
public sealed record GeometryRenderOptions
{
    /// <summary>
    /// Configuración predeterminada.
    /// </summary>
    public static GeometryRenderOptions Default { get; } =
        new();

    /// <summary>
    /// Valor máximo permitido para el canal RGB más claro.
    /// </summary>
    public byte DarkChannelThreshold { get; init; } =
        BoardGeometryAnalyzer.DefaultDarkChannelThreshold;

    /// <summary>
    /// Canal alfa mínimo considerado visible.
    /// </summary>
    public byte MinimumAlpha { get; init; } =
        BoardGeometryAnalyzer.DefaultMinimumAlpha;

    /// <summary>
    /// Diferencia máxima permitida entre canales RGB.
    /// </summary>
    public byte MaximumChannelDifference { get; init; } =
        BoardGeometryAnalyzer.DefaultMaximumChannelDifference;

    /// <summary>
    /// Tipo lógico del documento procesado.
    /// </summary>
    /// <remarks>
    /// En modo <see cref="GeometryDocumentRole.Auto"/>, el pipeline inspecciona
    /// el nombre del archivo. Los nombres que contienen "schematic", "schema",
    /// "esquemático" o "circuit" utilizan el motor esquemático. Los demás
    /// documentos utilizan el motor de placa.
    /// </remarks>
    public GeometryDocumentRole DocumentRole
    {
        get;
        init;
    } = GeometryDocumentRole.Auto;

    /// <summary>
    /// Configuración del clasificador geométrico.
    /// </summary>
    public BoardGeometryComponentClassifierOptions ComponentClassifierOptions
    {
        get;
        init;
    } = BoardGeometryComponentClassifier.DefaultOptions;

    /// <summary>
    /// Configuración del índice geométrico y espacial.
    /// </summary>
    public BoardGeometryIndexOptions GeometryIndexOptions
    {
        get;
        init;
    } = BoardGeometryIndexOptions.Default;

    /// <summary>
    /// Habilita la extracción automática de palabras nativas del PDF.
    /// </summary>
    public bool EnableNativePdfTextExtraction
    {
        get;
        init;
    } = true;

    /// <summary>
    /// Configuración de la conversión de palabras PDF a observaciones.
    /// </summary>
    public BoardPdfTextObservationOptions TextObservationOptions
    {
        get;
        init;
    } = BoardPdfTextObservationOptions.Default;

    /// <summary>
    /// Configuración del detector de referencias electrónicas.
    /// </summary>
    public BoardReferenceDetectorOptions ReferenceDetectorOptions
    {
        get;
        init;
    } = BoardReferenceDetectorOptions.Default;

    /// <summary>
    /// Candidatos externos opcionales que se combinan con los detectados
    /// automáticamente.
    /// </summary>
    public IReadOnlyList<BoardReferenceCandidate> ReferenceCandidates
    {
        get;
        init;
    } = Array.Empty<BoardReferenceCandidate>();

    /// <summary>
    /// Configuración del motor de asociación de referencias.
    /// </summary>
    public BoardReferenceAssociationOptions ReferenceAssociationOptions
    {
        get;
        init;
    } = BoardReferenceAssociationOptions.Default;

    /// <summary>
    /// Configuración del ensamblador de símbolos esquemáticos.
    /// </summary>
    public SchematicSymbolAssemblerOptions SchematicSymbolAssemblerOptions
    {
        get;
        init;
    } = SchematicSymbolAssemblerOptions.Default;

    /// <summary>
    /// Configuración del selector y agrupador de componentes.
    /// </summary>
    public BoardGeometryComponentSelectorOptions ComponentSelectorOptions
    {
        get;
        init;
    } = BoardGeometryComponentSelector.DefaultOptions;
}

/// <summary>
/// Dimensiones renderizadas de una página en píxeles.
/// </summary>
public readonly record struct GeometryPagePixelSize(
    int Width,
    int Height);

/// <summary>
/// Página completa renderizada mediante PDFium.
/// </summary>
public sealed record GeometryPageRenderResult(
    int PageIndex,
    double ZoomFactor,
    PdfiumPageSize PageSize,
    PdfiumRenderResult Image);

/// <summary>
/// Resultado completo del pipeline geométrico.
/// </summary>
public sealed record GeometryRenderResult(
    GeometryPageRenderResult Original,
    BoardGeometryMask Mask,
    BoardGeometryComponentsResult Components,
    BoardGeometryComponentClassificationResult Classification,
    BoardGeometryIndex GeometryIndex,
    BoardGeometrySpatialSearchEngine SpatialSearch,
    IReadOnlyList<BoardTextObservation> TextObservations,
    IReadOnlyList<BoardReferenceCandidate> ReferenceCandidates,
    BoardReferenceAssociationResult ReferenceAssociation,
    BoardReferenceIndex ReferenceIndex,
    SchematicSymbolAssemblyResult SchematicSymbols,
    BoardGeometryComponentSelectionResult Selection,
    BoardGeometryAnalysisResult Analysis,
    BoardGeometryCropResult? CropResult)
{
    /// <summary>
    /// Intenta obtener el mejor componente bajo una coordenada del render
    /// original.
    /// </summary>
    public bool TryHitTest(
        double x,
        double y,
        out BoardGeometryIndexedComponent? component,
        BoardGeometrySpatialSearchOptions? options = null)
    {
        return SpatialSearch.TryHitTestBest(
            x,
            y,
            out component,
            options);
    }

    /// <summary>
    /// Busca componentes próximos a una coordenada del render original.
    /// </summary>
    public BoardGeometrySpatialSearchResult FindNearest(
        double x,
        double y,
        double maximumDistancePixels,
        int maximumResults = 10,
        BoardGeometrySpatialSearchOptions? options = null)
    {
        return SpatialSearch.FindNearest(
            x,
            y,
            maximumDistancePixels,
            maximumResults,
            options);
    }

    /// <summary>
    /// Busca componentes que intersectan una región del render original.
    /// </summary>
    public BoardGeometryRegionSearchResult FindInBounds(
        BoardGeometryBounds bounds,
        BoardGeometrySpatialSearchOptions? options = null)
    {
        return SpatialSearch.FindInBounds(
            bounds,
            options);
    }

    /// <summary>
    /// Busca una referencia exacta dentro del índice.
    /// </summary>
    public bool TryGetReference(
        string reference,
        out BoardReferenceEntry? entry)
    {
        return ReferenceIndex.TryGetByReference(
            reference,
            out entry);
    }

    /// <summary>
    /// Busca la referencia principal asociada a un componente.
    /// </summary>
    public bool TryGetReferenceByComponentId(
        int componentId,
        out BoardReferenceEntry? entry)
    {
        return ReferenceIndex.TryGetByComponentId(
            componentId,
            out entry);
    }

    /// <summary>
    /// Ejecuta una búsqueda textual dentro del índice de referencias.
    /// </summary>
    public BoardReferenceLookupResult SearchReferences(
        string query,
        int maximumResults = 50)
    {
        return ReferenceIndex.Search(
            query,
            maximumResults);
    }

    /// <summary>
    /// Resuelve una referencia exacta y devuelve directamente su componente
    /// geométrico asociado.
    /// </summary>
    public bool TryFindReference(
        string reference,
        out BoardGeometryIndexedComponent? component)
    {
        if (ReferenceIndex.TryGetByReference(
                reference,
                out BoardReferenceEntry? entry) &&
            entry is not null)
        {
            component =
                entry.Component;

            return true;
        }

        component =
            null;

        return false;
    }

    /// <summary>
    /// Resuelve una referencia exacta y devuelve tanto la entrada semántica
    /// como el componente geométrico asociado.
    /// </summary>
    public bool TryFindReference(
        string reference,
        out BoardReferenceEntry? entry,
        out BoardGeometryIndexedComponent? component)
    {
        if (ReferenceIndex.TryGetByReference(
                reference,
                out entry) &&
            entry is not null)
        {
            component =
                entry.Component;

            return true;
        }

        entry =
            null;

        component =
            null;

        return false;
    }

    /// <summary>
    /// Busca referencias parciales y devuelve sus componentes geométricos,
    /// eliminando componentes duplicados.
    /// </summary>
    public IReadOnlyList<BoardGeometryIndexedComponent> SearchReferenceComponents(
        string query,
        int maximumResults = 20)
    {
        BoardReferenceLookupResult lookup =
            ReferenceIndex.Search(
                query,
                maximumResults);

        if (!lookup.HasMatches)
        {
            return Array.Empty<BoardGeometryIndexedComponent>();
        }

        return lookup.Matches
            .Select(entry =>
                entry.Component)
            .DistinctBy(component =>
                component.Id)
            .ToArray();
    }

    /// <summary>
    /// Busca referencias parciales y devuelve las entradas semánticas
    /// correspondientes.
    /// </summary>
    public IReadOnlyList<BoardReferenceEntry> SearchReferenceEntries(
        string query,
        int maximumResults = 20)
    {
        return ReferenceIndex
            .Search(
                query,
                maximumResults)
            .Matches;
    }

    /// <summary>
    /// Obtiene los límites lógicos de un símbolo esquemático ensamblado.
    /// </summary>
    public bool TryGetSchematicSymbol(
        string reference,
        out SchematicSymbol? symbol)
    {
        return SchematicSymbols.TryGetByReference(
            reference,
            out symbol);
    }

    /// <summary>
    /// Obtiene los límites preferidos para centrar y resaltar una referencia.
    /// </summary>
    public bool TryGetReferenceSelectionBounds(
        string reference,
        out BoardGeometryBounds bounds)
    {
        if (SchematicSymbols.TryGetByReference(
                reference,
                out SchematicSymbol? symbol) &&
            symbol is not null)
        {
            bounds =
                symbol.Bounds;

            return true;
        }

        if (TryFindReference(
                reference,
                out BoardGeometryIndexedComponent? component) &&
            component is not null)
        {
            bounds =
                component.Bounds;

            return true;
        }

        bounds =
            default;

        return false;
    }

    /// <summary>
    /// Indica si se seleccionó y recortó una región geométrica válida.
    /// </summary>
    public bool HasGeometry =>
        Selection.HasSelection &&
        Analysis.HasGeometry &&
        CropResult is not null;
}