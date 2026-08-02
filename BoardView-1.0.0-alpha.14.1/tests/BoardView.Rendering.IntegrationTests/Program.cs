using System.Globalization;
using BoardView.Formats.Pdf;
using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.IntegrationTests;

/// <summary>
/// Prueba ejecutable de integración para GeometryRenderPipeline.
/// </summary>
/// <remarks>
/// Uso:
///
/// dotnet run --project tests/BoardView.Rendering.IntegrationTests --
///     "C:\ruta\archivo.pdf" [pagina] [zoom] [directorio-salida]
///
/// La página se recibe con numeración humana basada en uno.
/// </remarks>
internal static class Program
{
    private const int SuccessExitCode = 0;
    private const int FailureExitCode = 1;
    private const int InvalidArgumentsExitCode = 2;

    private static async Task<int> Main(string[] args)
    {
        try
        {
            IntegrationTestArguments arguments =
                IntegrationTestArguments.Parse(args);

            GeometryIntegrationTestResult result =
                await ExecuteAsync(arguments);

            PrintResult(result);

            return SuccessExitCode;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"[ARGUMENTOS] {exception.Message}");
            PrintUsage();

            return InvalidArgumentsExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[ERROR] {exception}");
            return FailureExitCode;
        }
    }

    /// <summary>
    /// Ejecuta el pipeline completo sobre un documento PDF real.
    /// </summary>
    private static async Task<GeometryIntegrationTestResult> ExecuteAsync(
        IntegrationTestArguments arguments)
    {
        Console.WriteLine("[INICIO] GeometryRenderPipeline");
        Console.WriteLine($"PDF: {arguments.PdfPath}");
        Console.WriteLine($"Página UI: {arguments.PageNumber}");
        Console.WriteLine($"Zoom: {arguments.ZoomFactor:F4}x");

        using var pipeline =
            new GeometryRenderPipeline(arguments.PdfPath);

        int pageIndex = checked(arguments.PageNumber - 1);

        Assert(
            pageIndex < pipeline.PageCount,
            $"El documento contiene {pipeline.PageCount} página(s), " +
            $"pero se solicitó la página {arguments.PageNumber}.");

        GeometryRenderResult result =
            await pipeline.RenderGeometryAsync(
                pageIndex,
                arguments.ZoomFactor);

        ValidatePipelineResult(result);

        Directory.CreateDirectory(arguments.OutputDirectory);

        string originalPath =
            Path.Combine(
                arguments.OutputDirectory,
                "geometry-original.ppm");

        WritePpm(
            originalPath,
            result.Original.Image.PixelData,
            result.Original.Image.PixelWidth,
            result.Original.Image.PixelHeight,
            result.Original.Image.Stride);

        string? cropPath = null;

        if (result.CropResult is not null)
        {
            cropPath =
                Path.Combine(
                    arguments.OutputDirectory,
                    "geometry-crop.ppm");

            WritePpm(
                cropPath,
                result.CropResult.ToArray(),
                result.CropResult.PixelWidth,
                result.CropResult.PixelHeight,
                result.CropResult.Stride);
        }

        return new GeometryIntegrationTestResult(
            result,
            originalPath,
            cropPath);
    }

    /// <summary>
    /// Verifica las invariantes principales del resultado integrado.
    /// </summary>
    private static void ValidatePipelineResult(
        GeometryRenderResult result)
    {
        PdfiumRenderResult original =
            result.Original.Image;

        Assert(
            original.PixelWidth > 0 &&
            original.PixelHeight > 0,
            "El render original tiene dimensiones inválidas.");

        Assert(
            original.Stride >= original.PixelWidth * 4,
            "El stride del render original es inválido.");

        Assert(
            original.PixelData.Length ==
            original.Stride * original.PixelHeight,
            "El tamaño del búfer original no coincide con sus dimensiones.");

        Assert(
            result.Mask.Width == original.PixelWidth &&
            result.Mask.Height == original.PixelHeight,
            "La máscara no coincide con las dimensiones del render.");

        Assert(
            result.Mask.GeometryPixelCount ==
            result.Analysis.MatchingPixelCount,
            "La máscara y el analizador clasificaron cantidades diferentes.");

        Assert(
            result.HasGeometry,
            "El pipeline no detectó geometría en la página.");

        Assert(
            result.CropResult is not null,
            "No se produjo el recorte geométrico.");

        BoardGeometryCropResult crop =
            result.CropResult!;

        BoardGeometryBounds bounds =
            result.Analysis.Bounds;

        Assert(
            crop.PixelWidth == bounds.Width &&
            crop.PixelHeight == bounds.Height,
            "El recorte no coincide con los límites detectados.");

        Assert(
            crop.SourceBounds == bounds,
            "El recorte perdió su posición dentro de la página original.");

        Assert(
            bounds.Left >= 0 &&
            bounds.Top >= 0 &&
            bounds.Right <= original.PixelWidth &&
            bounds.Bottom <= original.PixelHeight,
            "Los límites detectados exceden la página renderizada.");

        Assert(
            crop.SizeInBytes ==
            crop.Stride * crop.PixelHeight,
            "El tamaño del búfer recortado es inválido.");
    }

    /// <summary>
    /// Guarda una imagen BGRA32 como PPM binario RGB.
    /// </summary>
    /// <remarks>
    /// El formato PPM permite inspeccionar el resultado sin agregar
    /// bibliotecas gráficas al proyecto de pruebas.
    /// </remarks>
    private static void WritePpm(
        string path,
        byte[] pixelData,
        int width,
        int height,
        int stride)
    {
        ArgumentNullException.ThrowIfNull(pixelData);

        using FileStream stream =
            new(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

        using var writer =
            new BinaryWriter(
                stream,
                System.Text.Encoding.ASCII,
                leaveOpen: true);

        writer.Write(
            System.Text.Encoding.ASCII.GetBytes(
                $"P6\n{width} {height}\n255\n"));

        byte[] rgbRow =
            new byte[checked(width * 3)];

        for (int y = 0; y < height; y++)
        {
            int sourceRowOffset =
                checked(y * stride);

            for (int x = 0; x < width; x++)
            {
                int sourceOffset =
                    checked(
                        sourceRowOffset +
                        (x * 4));

                int destinationOffset =
                    checked(x * 3);

                rgbRow[destinationOffset] =
                    pixelData[sourceOffset + 2];

                rgbRow[destinationOffset + 1] =
                    pixelData[sourceOffset + 1];

                rgbRow[destinationOffset + 2] =
                    pixelData[sourceOffset];
            }

            writer.Write(rgbRow);
        }
    }

    /// <summary>
    /// Muestra los datos necesarios para validar manualmente el pipeline.
    /// </summary>
    private static void PrintResult(
        GeometryIntegrationTestResult testResult)
    {
        GeometryRenderResult result =
            testResult.Result;

        PdfiumRenderResult original =
            result.Original.Image;

        BoardGeometryBounds bounds =
            result.Analysis.Bounds;

        Console.WriteLine();
        Console.WriteLine("[OK] Prueba de integración completada.");
        Console.WriteLine(
            $"Página PDF: " +
            $"{result.Original.PageSize.Width:F2} × " +
            $"{result.Original.PageSize.Height:F2} pt");

        Console.WriteLine(
            $"Render: {original.PixelWidth} × " +
            $"{original.PixelHeight} px");

        Console.WriteLine(
            $"Píxeles geométricos: " +
            $"{result.Analysis.MatchingPixelCount:N0}");

        Console.WriteLine(
            $"Límites: X={bounds.Left}, Y={bounds.Top}, " +
            $"W={bounds.Width}, H={bounds.Height}, " +
            $"R={bounds.Right}, B={bounds.Bottom}");

        Console.WriteLine(
            $"Imagen original: {testResult.OriginalOutputPath}");

        if (testResult.CropOutputPath is not null)
        {
            Console.WriteLine(
                $"Imagen recortada: {testResult.CropOutputPath}");
        }
    }

    private static void Assert(
        bool condition,
        string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            "Uso:");

        Console.Error.WriteLine(
            "dotnet run --project " +
            "tests/BoardView.Rendering.IntegrationTests -- " +
            "\"C:\\ruta\\archivo.pdf\" [pagina] [zoom] [directorio-salida]");
    }
}

/// <summary>
/// Argumentos normalizados de la prueba ejecutable.
/// </summary>
internal sealed record IntegrationTestArguments(
    string PdfPath,
    int PageNumber,
    double ZoomFactor,
    string OutputDirectory)
{
    public static IntegrationTestArguments Parse(
        string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException(
                "Debe indicar la ruta de un documento PDF.");
        }

        string pdfPath =
            Path.GetFullPath(args[0]);

        if (!File.Exists(pdfPath))
        {
            throw new ArgumentException(
                $"No se encontró el documento: {pdfPath}");
        }

        int pageNumber =
            args.Length >= 2
                ? ParsePageNumber(args[1])
                : 1;

        double zoomFactor =
            args.Length >= 3
                ? ParseZoomFactor(args[2])
                : 1D;

        string outputDirectory =
            args.Length >= 4
                ? Path.GetFullPath(args[3])
                : Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "artifacts",
                    "geometry-integration");

        return new IntegrationTestArguments(
            pdfPath,
            pageNumber,
            zoomFactor,
            outputDirectory);
    }

    private static int ParsePageNumber(
        string value)
    {
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int pageNumber) ||
            pageNumber <= 0)
        {
            throw new ArgumentException(
                "La página debe ser un entero mayor que cero.");
        }

        return pageNumber;
    }

    private static double ParseZoomFactor(
        string value)
    {
        string normalized =
            value.Replace(',', '.');

        if (!double.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double zoomFactor) ||
            !double.IsFinite(zoomFactor) ||
            zoomFactor <= 0D)
        {
            throw new ArgumentException(
                "El zoom debe ser un número finito mayor que cero.");
        }

        return zoomFactor;
    }
}

/// <summary>
/// Resultado final de la prueba ejecutable.
/// </summary>
internal sealed record GeometryIntegrationTestResult(
    GeometryRenderResult Result,
    string OriginalOutputPath,
    string? CropOutputPath);