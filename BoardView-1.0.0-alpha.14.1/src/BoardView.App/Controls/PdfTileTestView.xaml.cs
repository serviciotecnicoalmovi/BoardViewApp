using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BoardView.Rendering.Backends.Pdfium;
using BoardView.Rendering.Tiles;
using Microsoft.Win32;

namespace BoardView.App.Controls;

/// <summary>
/// Control aislado para comprobar visualmente el renderizado
/// de una única tesela PDF mediante PDFium.
/// </summary>
/// <remarks>
/// Este control no reemplaza todavía a PdfDocumentView.
///
/// Su responsabilidad actual es validar:
///
/// <list type="bullet">
/// <item>La orientación de la imagen.</item>
/// <item>El formato de píxeles BGRA32.</item>
/// <item>La nitidez del contenido.</item>
/// <item>Las coordenadas de la región solicitada.</item>
/// <item>Las dimensiones de la tesela.</item>
/// <item>El cálculo automático del tamaño renderizado de la página.</item>
/// <item>El contrato de renderizado basado en factor de zoom.</item>
/// </list>
/// </remarks>
public partial class PdfTileTestView : UserControl
{
    private readonly PdfiumTileRenderer _tileRenderer = new();

    private CancellationTokenSource? _renderCancellation;
    private string? _loadedDocumentPath;
    private Guid _documentId;
    private bool _disposed;

    /// <summary>
    /// Inicializa el control de prueba.
    /// </summary>
    public PdfTileTestView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Abre el selector de documentos PDF.
    /// </summary>
    private void SelectFileButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Seleccionar documento PDF",
            Filter = "Documentos PDF (*.pdf)|*.pdf",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };

        bool? result = dialog.ShowDialog();

        if (result != true)
        {
            return;
        }

        FilePathTextBox.Text = dialog.FileName;

        StatusTextBlock.Text =
            "Documento seleccionado. Presiona Renderizar.";
    }

    /// <summary>
    /// Inicia el renderizado asíncrono de la tesela solicitada.
    /// </summary>
    private async void RenderButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await RenderSelectedTileAsync();
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text =
                "El renderizado fue cancelado.";
        }
        catch (Exception exception)
        {
            TileImage.Source = null;
            EmptyMessageTextBlock.Visibility = Visibility.Visible;

            StatusTextBlock.Text =
                $"Error de renderizado: {exception.Message}";
        }
        finally
        {
            if (!_disposed)
            {
                RenderButton.IsEnabled = true;
            }
        }
    }

    /// <summary>
    /// Valida los parámetros, calcula automáticamente las dimensiones
    /// renderizadas de la página, solicita la tesela a PDFium y la
    /// convierte en una imagen compatible con WPF.
    /// </summary>
    private async Task RenderSelectedTileAsync()
    {
        ThrowIfDisposed();

        string filePath = FilePathTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new InvalidOperationException(
                "Primero debes seleccionar un documento PDF.");
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "El documento PDF seleccionado ya no existe.",
                filePath);
        }

        int pageNumber = ParsePositiveInt32(
            PageNumberTextBox.Text,
            "Página");

        double zoomFactor = ParsePositiveDouble(
            ZoomFactorTextBox.Text,
            "Zoom");

        int tileX = ParseNonNegativeInt32(
            TileXTextBox.Text,
            "Coordenada X");

        int tileY = ParseNonNegativeInt32(
            TileYTextBox.Text,
            "Coordenada Y");

        int tileWidth = ParsePositiveInt32(
            TileWidthTextBox.Text,
            "Ancho de tesela");

        int tileHeight = ParsePositiveInt32(
            TileHeightTextBox.Text,
            "Alto de tesela");

        /*
         * La interfaz utiliza números de página basados en uno.
         * PDFium y TileKey utilizan índices basados en cero.
         */
        int pageIndex = checked(pageNumber - 1);

        CancelCurrentRender();

        _renderCancellation = new CancellationTokenSource();

        CancellationToken cancellationToken =
            _renderCancellation.Token;

        RenderButton.IsEnabled = false;
        TileImage.Source = null;
        EmptyMessageTextBlock.Visibility = Visibility.Visible;

        StatusTextBlock.Text =
            "Calculando la página y renderizando la tesela mediante PDFium...";

        LoadDocumentIfRequired(filePath);

        if (pageIndex >= _tileRenderer.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageNumber),
                pageNumber,
                $"El documento contiene {_tileRenderer.PageCount} páginas.");
        }

        /*
         * PdfiumTileRenderer convierte internamente:
         *
         * puntos PDF × 96 / 72 × zoom
         *
         * El control obtiene ese mismo resultado únicamente para validar
         * que la región solicitada permanezca dentro de la página.
         */
        PdfiumRenderedPageSize renderedPageSize =
            _tileRenderer.GetRenderedPageSize(
                pageIndex,
                zoomFactor);

        ValidateTileBounds(
            renderedPageSize.Width,
            renderedPageSize.Height,
            tileX,
            tileY,
            tileWidth,
            tileHeight);

        /*
         * Para esta prueba aislada se produce una única tesela a la vez.
         *
         * Las coordenadas físicas de la región están contenidas en
         * TileBounds. Los valores TileX y TileY de TileKey representan
         * la posición lógica de la tesela dentro de una cuadrícula.
         */
        int tileColumn = tileX / tileWidth;
        int tileRow = tileY / tileHeight;

        var key = new TileKey(
            _documentId,
            pageIndex,
            0,
            tileColumn,
            tileRow);

        var tileBounds = new Int32Rect(
            tileX,
            tileY,
            tileWidth,
            tileHeight);

        /*
         * Nuevo contrato:
         *
         * PagePixelSize ya no es proporcionado por la interfaz.
         * PdfiumTileRenderer lo calcula a partir del documento,
         * la página y el factor de zoom.
         */
        var request = new TileRenderRequest(
            key,
            tileBounds,
            zoomFactor);

        string requestDiagnostic = BuildRequestDiagnostic(
            pageNumber,
            pageIndex,
            _tileRenderer.PageCount,
            zoomFactor,
            renderedPageSize,
            tileColumn,
            tileRow,
            zoomLevel: 0,
            tileBounds);

        Debug.WriteLine(requestDiagnostic);

        StatusTextBlock.Text =
            "Renderizando mediante PDFium | " +
            requestDiagnostic;

        var stopwatch = Stopwatch.StartNew();

        Tile tile = await _tileRenderer.RenderTileAsync(
            request,
            cancellationToken);

        stopwatch.Stop();

        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        WriteableBitmap bitmap =
            CreateBitmapFromTile(tile);

        TileImage.Source = bitmap;
        EmptyMessageTextBlock.Visibility = Visibility.Collapsed;

        double memoryInMegabytes =
            tile.SizeInBytes / (1024D * 1024D);

        string resultDiagnostic = BuildResultDiagnostic(
            pageNumber,
            pageIndex,
            _tileRenderer.PageCount,
            zoomFactor,
            renderedPageSize,
            tileColumn,
            tileRow,
            zoomLevel: 0,
            tileBounds,
            tile,
            memoryInMegabytes,
            stopwatch.Elapsed.TotalMilliseconds);

        Debug.WriteLine(resultDiagnostic);

        StatusTextBlock.Text = resultDiagnostic;
    }

    /// <summary>
    /// Construye el registro de diagnóstico antes de enviar
    /// la solicitud al renderizador.
    /// </summary>
    private static string BuildRequestDiagnostic(
        int pageNumber,
        int pageIndex,
        int pageCount,
        double zoomFactor,
        PdfiumRenderedPageSize renderedPageSize,
        int tileColumn,
        int tileRow,
        int zoomLevel,
        Int32Rect tileBounds)
    {
        int right = checked(tileBounds.X + tileBounds.Width);
        int bottom = checked(tileBounds.Y + tileBounds.Height);

        return
            "[PdfTileTestView] SOLICITUD | " +
            $"Página UI {pageNumber}/{pageCount} | " +
            $"Índice PDFium {pageIndex} | " +
            $"Zoom {zoomFactor:F4}× | " +
            $"Página {renderedPageSize.Width} × {renderedPageSize.Height} px | " +
            $"TileKey C={tileColumn}, F={tileRow}, Z={zoomLevel} | " +
            $"Región X={tileBounds.X}, Y={tileBounds.Y}, " +
            $"W={tileBounds.Width}, H={tileBounds.Height}, " +
            $"R={right}, B={bottom}";
    }

    /// <summary>
    /// Construye el registro de diagnóstico posterior al renderizado
    /// sin alterar el resultado producido por PDFium.
    /// </summary>
    private static string BuildResultDiagnostic(
        int pageNumber,
        int pageIndex,
        int pageCount,
        double zoomFactor,
        PdfiumRenderedPageSize renderedPageSize,
        int tileColumn,
        int tileRow,
        int zoomLevel,
        Int32Rect tileBounds,
        Tile tile,
        double memoryInMegabytes,
        double elapsedMilliseconds)
    {
        int right = checked(tileBounds.X + tileBounds.Width);
        int bottom = checked(tileBounds.Y + tileBounds.Height);

        return
            "[PdfTileTestView] RESULTADO | " +
            $"Página UI {pageNumber}/{pageCount} | " +
            $"Índice PDFium {pageIndex} | " +
            $"Zoom {zoomFactor:F4}× | " +
            $"Página {renderedPageSize.Width} × {renderedPageSize.Height} px | " +
            $"TileKey C={tileColumn}, F={tileRow}, Z={zoomLevel} | " +
            $"Región X={tileBounds.X}, Y={tileBounds.Y}, " +
            $"W={tileBounds.Width}, H={tileBounds.Height}, " +
            $"R={right}, B={bottom} | " +
            $"Salida {tile.PixelWidth} × {tile.PixelHeight} px | " +
            $"Stride {tile.Stride} | " +
            $"Bytes {tile.SizeInBytes} | " +
            $"{memoryInMegabytes:F2} MB | " +
            $"{elapsedMilliseconds:F1} ms";
    }

    /// <summary>
    /// Carga el documento únicamente cuando cambia su ruta.
    /// </summary>
    /// <remarks>
    /// Cada documento cargado recibe un identificador nuevo.
    /// Este identificador forma parte de TileKey y evita que una tesela
    /// de un documento pueda confundirse con otra perteneciente a un
    /// archivo diferente.
    /// </remarks>
    private void LoadDocumentIfRequired(string filePath)
    {
        string absolutePath = Path.GetFullPath(filePath);

        if (string.Equals(
                _loadedDocumentPath,
                absolutePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _tileRenderer.SetDocument(absolutePath);

        _loadedDocumentPath = absolutePath;
        _documentId = Guid.NewGuid();
    }

    /// <summary>
    /// Convierte los píxeles BGRA32 de una tesela
    /// en un WriteableBitmap de WPF.
    /// </summary>
    private static WriteableBitmap CreateBitmapFromTile(Tile tile)
    {
        var bitmap = new WriteableBitmap(
            tile.PixelWidth,
            tile.PixelHeight,
            96D,
            96D,
            PixelFormats.Bgra32,
            palette: null);

        var destinationRect = new Int32Rect(
            0,
            0,
            tile.PixelWidth,
            tile.PixelHeight);

        bitmap.WritePixels(
            destinationRect,
            tile.PixelData,
            tile.Stride,
            offset: 0);

        /*
         * La imagen deja de necesitar acceso al hilo que la creó.
         * Esto también evita modificaciones accidentales posteriores.
         */
        bitmap.Freeze();

        return bitmap;
    }

    /// <summary>
    /// Comprueba que la región solicitada esté dentro
    /// de las dimensiones renderizadas de la página.
    /// </summary>
    private static void ValidateTileBounds(
        int pageWidth,
        int pageHeight,
        int tileX,
        int tileY,
        int tileWidth,
        int tileHeight)
    {
        long right = checked((long)tileX + tileWidth);
        long bottom = checked((long)tileY + tileHeight);

        if (tileX >= pageWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tileX),
                tileX,
                "La coordenada X se encuentra fuera de la página renderizada.");
        }

        if (tileY >= pageHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tileY),
                tileY,
                "La coordenada Y se encuentra fuera de la página renderizada.");
        }

        if (right > pageWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tileWidth),
                tileWidth,
                "La tesela supera el límite derecho de la página.");
        }

        if (bottom > pageHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tileHeight),
                tileHeight,
                "La tesela supera el límite inferior de la página.");
        }
    }

    /// <summary>
    /// Convierte un texto en un entero mayor que cero.
    /// </summary>
    private static int ParsePositiveInt32(
        string text,
        string fieldName)
    {
        if (!int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out int value) ||
            value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                fieldName,
                $"El campo «{fieldName}» debe contener un entero mayor que cero.");
        }

        return value;
    }

    /// <summary>
    /// Convierte un texto en un entero igual o mayor que cero.
    /// </summary>
    private static int ParseNonNegativeInt32(
        string text,
        string fieldName)
    {
        if (!int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out int value) ||
            value < 0)
        {
            throw new ArgumentOutOfRangeException(
                fieldName,
                $"El campo «{fieldName}» debe contener un entero igual o mayor que cero.");
        }

        return value;
    }

    /// <summary>
    /// Convierte un texto en un número finito mayor que cero.
    /// </summary>
    /// <remarks>
    /// Primero se utiliza la configuración regional del sistema para
    /// admitir separadores decimales como coma o punto según el equipo.
    ///
    /// Como alternativa, también se acepta el formato invariable.
    /// </remarks>
    private static double ParsePositiveDouble(
        string text,
        string fieldName)
    {
        const NumberStyles styles =
            NumberStyles.Float |
            NumberStyles.AllowThousands;

        bool parsed =
            double.TryParse(
                text,
                styles,
                CultureInfo.CurrentCulture,
                out double value) ||
            double.TryParse(
                text,
                styles,
                CultureInfo.InvariantCulture,
                out value);

        if (!parsed ||
            !double.IsFinite(value) ||
            value <= 0D)
        {
            throw new ArgumentOutOfRangeException(
                fieldName,
                $"El campo «{fieldName}» debe contener un número finito mayor que cero.");
        }

        return value;
    }

    /// <summary>
    /// Cancela y libera la operación de renderizado anterior.
    /// </summary>
    private void CancelCurrentRender()
    {
        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        _renderCancellation = null;
    }

    /// <summary>
    /// Cancela cualquier operación pendiente y libera PDFium.
    /// </summary>
    private void OnUnloaded(
        object sender,
        RoutedEventArgs e)
    {
        DisposeResources();
    }

    /// <summary>
    /// Libera los recursos administrados del control.
    /// </summary>
    private void DisposeResources()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        CancelCurrentRender();

        _tileRenderer.Dispose();

        _loadedDocumentPath = null;
        _documentId = Guid.Empty;
    }

    /// <summary>
    /// Impide utilizar el control después de liberar sus recursos.
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }
}
