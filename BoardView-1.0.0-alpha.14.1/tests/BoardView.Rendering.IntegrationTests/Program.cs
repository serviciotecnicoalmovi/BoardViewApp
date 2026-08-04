using System.Globalization;
using BoardView.Formats.Pdf;
using BoardView.Rendering.Geometry;
using BoardView.Rendering.Recognition;

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
            Console.Error.WriteLine(
                $"[ARGUMENTOS] {exception.Message}");

            PrintUsage();

            return InvalidArgumentsExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[ERROR] {exception}");

            return FailureExitCode;
        }
    }

    /// <summary>
    /// Ejecuta el pipeline completo sobre un documento PDF real.
    /// </summary>
    private static async Task<GeometryIntegrationTestResult> ExecuteAsync(
        IntegrationTestArguments arguments)
    {
        Console.WriteLine(
            "[INICIO] GeometryRenderPipeline");

        Console.WriteLine(
            $"PDF: {arguments.PdfPath}");

        Console.WriteLine(
            $"Página UI: {arguments.PageNumber}");

        Console.WriteLine(
            $"Zoom: {arguments.ZoomFactor:F4}x");

        using var pipeline =
            new GeometryRenderPipeline(
                arguments.PdfPath);

        int pageIndex =
            checked(
                arguments.PageNumber - 1);

        Assert(
            pageIndex < pipeline.PageCount,
            $"El documento contiene {pipeline.PageCount} página(s), " +
            $"pero se solicitó la página {arguments.PageNumber}.");

        GeometryRenderResult result =
            await pipeline.RenderGeometryAsync(
                pageIndex,
                arguments.ZoomFactor);

        ValidatePipelineResult(result);

        Directory.CreateDirectory(
            arguments.OutputDirectory);

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
    /// Verifica las invariantes del pipeline geométrico actual.
    /// </summary>
    /// <remarks>
    /// El análisis ya no representa todos los píxeles de la máscara.
    /// Representa únicamente los componentes aceptados por
    /// BoardGeometryComponentSelector.
    /// </remarks>
    private static void ValidatePipelineResult(
        GeometryRenderResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        PdfiumRenderResult original =
            result.Original.Image;

        Assert(
            original.PixelWidth > 0 &&
            original.PixelHeight > 0,
            "El render original tiene dimensiones inválidas.");

        Assert(
            original.Stride >=
            checked(original.PixelWidth * 4),
            "El stride del render original es inválido.");

        Assert(
            original.PixelData.Length ==
            checked(
                original.Stride *
                original.PixelHeight),
            "El tamaño del búfer original no coincide con sus dimensiones.");

        Assert(
            result.Mask.Width ==
            original.PixelWidth &&
            result.Mask.Height ==
            original.PixelHeight,
            "La máscara no coincide con las dimensiones del render.");

        Assert(
            result.Mask.GeometryPixelCount > 0L,
            "La máscara geométrica está vacía.");

        Assert(
            result.Components.ComponentCount > 0,
            "No se detectaron componentes conectados.");

        Assert(
            result.Components.GeometryPixelCount ==
            result.Mask.GeometryPixelCount,
            "El análisis de componentes no conserva el total de píxeles de la máscara.");

        Assert(
            result.GeometryIndex.Count ==
            result.Classification.ClassificationCount,
            "El índice geométrico no contiene todas las clasificaciones.");

        Assert(
            result.GeometryIndex.PageWidth ==
            original.PixelWidth &&
            result.GeometryIndex.PageHeight ==
            original.PixelHeight,
            "El índice geométrico no coincide con las dimensiones del render.");

        Assert(
            result.GeometryIndex.Statistics.ComponentCount ==
            result.Components.ComponentCount,
            "Las estadísticas del índice no coinciden con los componentes detectados.");

        Assert(
            ReferenceEquals(
                result.SpatialSearch.Index,
                result.GeometryIndex),
            "El motor espacial no utiliza el índice contenido en el resultado.");

        Assert(
            result.ReferenceAssociation.Statistics.CandidateCount ==
            result.ReferenceAssociation.Candidates.Count,
            "Las estadísticas de asociación no coinciden con los candidatos.");

        Assert(
            result.ReferenceAssociation.Statistics.CandidateCount ==
            result.ReferenceCandidates.Count,
            "El resultado de asociación no contiene todos los candidatos detectados.");

        Assert(
            result.TextObservations.All(observation =>
                observation.PageIndex == result.Original.PageIndex),
            "Las observaciones textuales pertenecen a una página diferente.");

        Assert(
            result.ReferenceCandidates.All(candidate =>
                candidate.PageIndex == result.Original.PageIndex),
            "Los candidatos de referencia pertenecen a una página diferente.");

        Assert(
            result.ReferenceAssociation.Statistics.AssociationCount ==
            result.ReferenceAssociation.Associations.Count,
            "Las estadísticas de asociación no coinciden con las asociaciones.");

        Assert(
            result.ReferenceIndex.Statistics.EntryCount ==
            result.ReferenceAssociation.Associations.Count,
            "El índice no contiene todas las asociaciones.");

        Assert(
            result.ReferenceIndex.Statistics.UniqueReferenceCount ==
            result.ReferenceIndex.Count,
            "Las estadísticas del índice no coinciden con sus referencias únicas.");

        foreach (BoardReferenceEntry entry in result.ReferenceIndex.Entries)
        {
            Assert(
                result.ReferenceIndex.TryGetByReference(
                    entry.Reference,
                    out BoardReferenceEntry? byReference) &&
                byReference is not null,
                $"No se pudo recuperar la referencia {entry.Reference}.");

            Assert(
                result.ReferenceIndex.TryGetByComponentId(
                    entry.ComponentId,
                    out BoardReferenceEntry? byComponent) &&
                byComponent is not null,
                $"No se pudo recuperar el componente {entry.ComponentId}.");
        }

        Assert(
            result.Selection.HasSelection,
            "El selector no produjo una región geométrica válida.");

        Assert(
            result.Selection.SelectedComponentCount > 0,
            "El selector no aceptó ningún componente.");

        Assert(
            result.Selection.SelectedPixelCount > 0L,
            "La selección no contiene píxeles geométricos.");

        Assert(
            result.Selection.SelectedPixelCount <=
            result.Mask.GeometryPixelCount,
            "La selección contiene más píxeles que la máscara completa.");

        Assert(
            result.Analysis.HasGeometry,
            "El análisis final no contiene geometría.");

        Assert(
            result.Analysis.MatchingPixelCount ==
            result.Selection.SelectedPixelCount,
            "El análisis final no coincide con los píxeles seleccionados.");

        Assert(
            result.Analysis.Bounds ==
            result.Selection.Bounds,
            "Los límites del análisis no coinciden con la selección.");

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
            bounds.Width > 0 &&
            bounds.Height > 0,
            "Los límites seleccionados tienen dimensiones inválidas.");

        Assert(
            bounds.Left >= 0 &&
            bounds.Top >= 0 &&
            bounds.Right <=
            original.PixelWidth &&
            bounds.Bottom <=
            original.PixelHeight,
            "Los límites seleccionados exceden la página renderizada.");

        Assert(
            crop.PixelWidth ==
            bounds.Width &&
            crop.PixelHeight ==
            bounds.Height,
            "El recorte no coincide con los límites seleccionados.");

        Assert(
            crop.SourceBounds ==
            bounds,
            "El recorte perdió su posición dentro de la página original.");

        Assert(
            crop.Stride >=
            checked(crop.PixelWidth * 4),
            "El stride del recorte es inválido.");

        Assert(
            crop.SizeInBytes ==
            checked(
                crop.Stride *
                crop.PixelHeight),
            "El tamaño del búfer recortado es inválido.");

        Assert(
            result.Selection.SelectedComponents.All(
                component =>
                    component.Bounds.Left >= 0 &&
                    component.Bounds.Top >= 0 &&
                    component.Bounds.Right <=
                    original.PixelWidth &&
                    component.Bounds.Bottom <=
                    original.PixelHeight),
            "Uno o más componentes seleccionados exceden la página.");
    }

    /// <summary>
    /// Guarda una imagen BGRA32 como PPM binario RGB.
    /// </summary>
    /// <remarks>
    /// El formato PPM permite inspeccionar el resultado sin agregar
    /// bibliotecas gráficas al proyecto de integración.
    /// </remarks>
    private static void WritePpm(
        string path,
        byte[] pixelData,
        int width,
        int height,
        int stride)
    {
        ArgumentNullException.ThrowIfNull(pixelData);

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height));
        }

        if (stride <
            checked(width * 4))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stride));
        }

        long requiredLength =
            checked(
                (long)stride *
                height);

        if (pixelData.LongLength <
            requiredLength)
        {
            throw new ArgumentException(
                "El búfer no contiene suficientes píxeles.",
                nameof(pixelData));
        }

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
            new byte[
                checked(width * 3)];

        for (int y = 0;
             y < height;
             y++)
        {
            int sourceRowOffset =
                checked(
                    y *
                    stride);

            for (int x = 0;
                 x < width;
                 x++)
            {
                int sourceOffset =
                    checked(
                        sourceRowOffset +
                        (x * 4));

                int destinationOffset =
                    checked(
                        x *
                        3);

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
    /// Muestra el resultado completo del pipeline.
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
        Console.WriteLine(
            "[OK] Prueba de integración completada.");

        Console.WriteLine(
            $"Página PDF: " +
            $"{result.Original.PageSize.Width:F2} × " +
            $"{result.Original.PageSize.Height:F2} pt");

        Console.WriteLine(
            $"Render: " +
            $"{original.PixelWidth} × " +
            $"{original.PixelHeight} px");

        Console.WriteLine(
            $"Píxeles de máscara: " +
            $"{result.Mask.GeometryPixelCount:N0}");

        Console.WriteLine(
            $"Componentes encontrados: " +
            $"{result.Components.ComponentCount:N0}");

        Console.WriteLine(
            $"Componentes indexados: " +
            $"{result.GeometryIndex.Count:N0}");

        Console.WriteLine(
            $"Celdas espaciales ocupadas: " +
            $"{result.GeometryIndex.Statistics.OccupiedCellCount:N0}");

        Console.WriteLine(
            $"Confianza media: " +
            $"{result.GeometryIndex.Statistics.AverageConfidence:P2}");

        Console.WriteLine(
            "Motor espacial: listo");

        Console.WriteLine(
            $"Observaciones textuales: " +
            $"{result.TextObservations.Count:N0}");

        Console.WriteLine(
            $"Candidatos de referencia: " +
            $"{result.ReferenceCandidates.Count:N0}");

        Console.WriteLine(
            $"Asociaciones de referencia: " +
            $"{result.ReferenceAssociation.Statistics.AssociationCount:N0}");

        Console.WriteLine(
            $"Cobertura de referencias: " +
            $"{result.ReferenceAssociation.Statistics.CandidateCoverage:P2}");

        Console.WriteLine(
            $"Entradas del ReferenceIndex: " +
            $"{result.ReferenceIndex.Statistics.EntryCount:N0}");

        Console.WriteLine(
            $"Referencias únicas: " +
            $"{result.ReferenceIndex.Statistics.UniqueReferenceCount:N0}");

        Console.WriteLine(
            $"Componentes indexados por referencia: " +
            $"{result.ReferenceIndex.Statistics.IndexedComponentCount:N0}");

        if (result.ReferenceAssociation.HasAssociations)
        {
            Console.WriteLine(
                "Primeras asociaciones:");

            foreach (BoardReferenceAssociation association
                     in result.ReferenceAssociation.Associations.Take(20))
            {
                Console.WriteLine(
                    $"  {association.Reference,-10} → " +
                    $"ID {association.ComponentId,-6} " +
                    $"{association.Component.Type,-14} " +
                    $"Score {association.Score:P1} " +
                    $"Dist. {association.DistancePixels:N1}px " +
                    $"Regla {association.Rule}");
            }

            BoardReferenceEntry firstEntry =
                result.ReferenceIndex.Entries[0];

            Assert(
                result.TryGetReference(
                    firstEntry.Reference,
                    out BoardReferenceEntry? resolvedEntry) &&
                resolvedEntry is not null &&
                resolvedEntry.ComponentId == firstEntry.ComponentId,
                "La búsqueda exacta de referencia falló.");

            BoardReferenceLookupResult searchResult =
                result.SearchReferences(
                    firstEntry.Prefix,
                    maximumResults: 10);

            Assert(
                searchResult.HasMatches,
                "La búsqueda por prefijo no devolvió resultados.");

            Console.WriteLine(
                $"Prueba de búsqueda: {firstEntry.Reference} → " +
                $"ID {resolvedEntry!.ComponentId}");

            Console.WriteLine(
                $"Búsqueda por prefijo '{firstEntry.Prefix}': " +
                $"{searchResult.Matches.Count:N0} resultado(s)");
        }

        Console.WriteLine(
            "Clasificación:");

        foreach (BoardGeometryComponentType type
                 in Enum.GetValues<BoardGeometryComponentType>())
        {
            Console.WriteLine(
                $"  {type,-16}: " +
                $"{result.Classification.GetCount(type):N0}");
        }

        Console.WriteLine(
            $"Componentes seleccionados: " +
            $"{result.Selection.SelectedComponentCount:N0}");

        Console.WriteLine(
            $"Píxeles seleccionados: " +
            $"{result.Selection.SelectedPixelCount:N0}");

        Console.WriteLine(
            $"Fallback utilizado: " +
            $"{(result.Selection.UsedFallback ? "Sí" : "No")}");

        Console.WriteLine(
            $"Cobertura seleccionada: " +
            $"{result.Selection.BoundsCoverage:P2}");

        Console.WriteLine(
            $"Límites: " +
            $"X={bounds.Left}, " +
            $"Y={bounds.Top}, " +
            $"W={bounds.Width}, " +
            $"H={bounds.Height}, " +
            $"R={bounds.Right}, " +
            $"B={bounds.Bottom}");

        Console.WriteLine(
            $"Imagen original: " +
            $"{testResult.OriginalOutputPath}");

        if (testResult.CropOutputPath is not null)
        {
            Console.WriteLine(
                $"Imagen recortada: " +
                $"{testResult.CropOutputPath}");
        }
    }

    /// <summary>
    /// Lanza una excepción cuando una invariante no se cumple.
    /// </summary>
    private static void Assert(
        bool condition,
        string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }

    /// <summary>
    /// Muestra la sintaxis admitida por la prueba.
    /// </summary>
    private static void PrintUsage()
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            "Uso:");

        Console.Error.WriteLine(
            "dotnet run --project " +
            "tests/BoardView.Rendering.IntegrationTests -- " +
            "\"C:\\ruta\\archivo.pdf\" " +
            "[pagina] [zoom] [directorio-salida]");
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
    /// <summary>
    /// Analiza y valida los argumentos de la consola.
    /// </summary>
    public static IntegrationTestArguments Parse(
        string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            throw new ArgumentException(
                "Debe indicar la ruta de un documento PDF.");
        }

        string pdfPath =
            Path.GetFullPath(
                args[0]);

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

    /// <summary>
    /// Convierte la página humana basada en uno.
    /// </summary>
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

    /// <summary>
    /// Convierte el factor de zoom admitiendo punto o coma decimal.
    /// </summary>
    private static double ParseZoomFactor(
        string value)
    {
        string normalized =
            value.Replace(
                ',',
                '.');

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
