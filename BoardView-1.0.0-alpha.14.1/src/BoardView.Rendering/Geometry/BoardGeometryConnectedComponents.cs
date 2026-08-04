using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BoardView.Rendering.Geometry;

/// <summary>
/// Detecta componentes conectados dentro de una máscara geométrica binaria.
/// </summary>
/// <remarks>
/// La implementación utiliza conectividad de ocho vecinos y un recorrido
/// iterativo basado en una pila administrada, evitando recursión y posibles
/// desbordamientos de pila.
///
/// Cada componente conserva:
///
/// <list type="bullet">
/// <item>identificador estable;</item>
/// <item>cantidad de píxeles;</item>
/// <item>rectángulo delimitador;</item>
/// <item>porcentaje ocupado respecto de la máscara completa.</item>
/// </list>
///
/// El resultado permite seleccionar el componente dominante antes de
/// recortar la imagen original.
/// </remarks>
public sealed class BoardGeometryConnectedComponents
{
    private static readonly (int X, int Y)[] EightNeighborOffsets =
    [
        (-1, -1),
        (0, -1),
        (1, -1),
        (-1, 0),
        (1, 0),
        (-1, 1),
        (0, 1),
        (1, 1)
    ];

    /// <summary>
    /// Analiza todos los componentes conectados de una máscara.
    /// </summary>
    /// <param name="mask">
    /// Máscara binaria que contiene píxeles de fondo y geometría.
    /// </param>
    /// <returns>
    /// Colección inmutable de componentes y referencia al componente
    /// dominante.
    /// </returns>
    public BoardGeometryComponentsResult Analyze(
        BoardGeometryMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);

        int width = mask.Width;
        int height = mask.Height;
        int totalPixels = checked(width * height);

        var visited = new bool[totalPixels];
        var components = new List<BoardGeometryComponent>();

        int nextComponentId = 1;

        for (int y = 0; y < height; y++)
        {
            int rowOffset = checked(y * width);

            for (int x = 0; x < width; x++)
            {
                int offset = checked(rowOffset + x);

                if (visited[offset] ||
                    !mask.IsGeometry(x, y))
                {
                    continue;
                }

                BoardGeometryComponent component =
                    TraverseComponent(
                        mask,
                        visited,
                        x,
                        y,
                        nextComponentId);

                components.Add(component);
                nextComponentId++;
            }
        }

        BoardGeometryComponent? largestComponent =
            FindLargestComponent(components);

        return new BoardGeometryComponentsResult(
            new ReadOnlyCollection<BoardGeometryComponent>(
                components),
            largestComponent,
            mask.Width,
            mask.Height,
            mask.GeometryPixelCount);
    }

    /// <summary>
    /// Recorre un componente conectado mediante búsqueda en profundidad
    /// iterativa.
    /// </summary>
    private static BoardGeometryComponent TraverseComponent(
        BoardGeometryMask mask,
        bool[] visited,
        int startX,
        int startY,
        int componentId)
    {
        int width = mask.Width;
        int height = mask.Height;

        var pending = new Stack<int>();
        int startOffset = checked((startY * width) + startX);

        pending.Push(startOffset);
        visited[startOffset] = true;

        int left = startX;
        int top = startY;
        int right = startX;
        int bottom = startY;
        long pixelCount = 0L;

        while (pending.Count > 0)
        {
            int currentOffset = pending.Pop();

            int currentY = currentOffset / width;
            int currentX = currentOffset - (currentY * width);

            pixelCount++;

            if (currentX < left)
            {
                left = currentX;
            }

            if (currentX > right)
            {
                right = currentX;
            }

            if (currentY < top)
            {
                top = currentY;
            }

            if (currentY > bottom)
            {
                bottom = currentY;
            }

            foreach ((int offsetX, int offsetY) in EightNeighborOffsets)
            {
                int neighborX = currentX + offsetX;
                int neighborY = currentY + offsetY;

                if ((uint)neighborX >= (uint)width ||
                    (uint)neighborY >= (uint)height)
                {
                    continue;
                }

                int neighborOffset =
                    checked(
                        (neighborY * width) +
                        neighborX);

                if (visited[neighborOffset])
                {
                    continue;
                }

                visited[neighborOffset] = true;

                if (!mask.IsGeometry(
                        neighborX,
                        neighborY))
                {
                    continue;
                }

                pending.Push(neighborOffset);
            }
        }

        var bounds = new BoardGeometryBounds(
            left,
            top,
            checked(right - left + 1),
            checked(bottom - top + 1));

        return new BoardGeometryComponent(
            componentId,
            pixelCount,
            bounds,
            checked((long)width * height));
    }

    /// <summary>
    /// Selecciona el componente de mayor área de píxeles.
    /// </summary>
    /// <remarks>
    /// En caso de empate se prioriza:
    ///
    /// <list type="number">
    /// <item>el componente con mayor área de rectángulo;</item>
    /// <item>el componente más cercano al origen superior izquierdo;</item>
    /// <item>el identificador menor.</item>
    /// </list>
    /// </remarks>
    private static BoardGeometryComponent? FindLargestComponent(
        IReadOnlyList<BoardGeometryComponent> components)
    {
        if (components.Count == 0)
        {
            return null;
        }

        BoardGeometryComponent largest =
            components[0];

        for (int index = 1;
             index < components.Count;
             index++)
        {
            BoardGeometryComponent candidate =
                components[index];

            if (IsPreferred(
                    candidate,
                    largest))
            {
                largest = candidate;
            }
        }

        return largest;
    }

    /// <summary>
    /// Determina si un candidato debe reemplazar al componente actual.
    /// </summary>
    private static bool IsPreferred(
        BoardGeometryComponent candidate,
        BoardGeometryComponent current)
    {
        if (candidate.PixelCount != current.PixelCount)
        {
            return candidate.PixelCount >
                   current.PixelCount;
        }

        if (candidate.BoundsArea != current.BoundsArea)
        {
            return candidate.BoundsArea >
                   current.BoundsArea;
        }

        if (candidate.Bounds.Top != current.Bounds.Top)
        {
            return candidate.Bounds.Top <
                   current.Bounds.Top;
        }

        if (candidate.Bounds.Left != current.Bounds.Left)
        {
            return candidate.Bounds.Left <
                   current.Bounds.Left;
        }

        return candidate.Id < current.Id;
    }
}

/// <summary>
/// Componente conectado detectado dentro de una máscara.
/// </summary>
public sealed class BoardGeometryComponent
{
    /// <summary>
    /// Inicializa una descripción inmutable de componente.
    /// </summary>
    public BoardGeometryComponent(
        int id,
        long pixelCount,
        BoardGeometryBounds bounds,
        long maskPixelCount)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(id),
                id,
                "El identificador debe ser mayor que cero.");
        }

        if (pixelCount <= 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelCount),
                pixelCount,
                "El componente debe contener al menos un píxel.");
        }

        if (bounds.Width <= 0 ||
            bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "Los límites del componente no son válidos.");
        }

        if (maskPixelCount <= 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maskPixelCount),
                maskPixelCount,
                "La máscara debe contener al menos un píxel.");
        }

        Id = id;
        PixelCount = pixelCount;
        Bounds = bounds;
        MaskPixelCount = maskPixelCount;
    }

    /// <summary>
    /// Obtiene el identificador secuencial del componente.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Obtiene la cantidad de píxeles conectados.
    /// </summary>
    public long PixelCount { get; }

    /// <summary>
    /// Obtiene el rectángulo mínimo que contiene el componente.
    /// </summary>
    public BoardGeometryBounds Bounds { get; }

    /// <summary>
    /// Obtiene la cantidad total de píxeles de la máscara analizada.
    /// </summary>
    public long MaskPixelCount { get; }

    /// <summary>
    /// Obtiene el área del rectángulo delimitador.
    /// </summary>
    public long BoundsArea =>
        checked(
            (long)Bounds.Width *
            Bounds.Height);

    /// <summary>
    /// Obtiene la proporción de píxeles ocupados dentro del rectángulo.
    /// </summary>
    public double Density =>
        BoundsArea == 0L
            ? 0D
            : (double)PixelCount /
              BoundsArea;

    /// <summary>
    /// Obtiene la proporción del componente respecto de toda la máscara.
    /// </summary>
    public double MaskCoverage =>
        (double)PixelCount /
        MaskPixelCount;
}

/// <summary>
/// Resultado inmutable del análisis de componentes conectados.
/// </summary>
public sealed class BoardGeometryComponentsResult
{
    /// <summary>
    /// Inicializa el resultado del análisis.
    /// </summary>
    public BoardGeometryComponentsResult(
        IReadOnlyList<BoardGeometryComponent> components,
        BoardGeometryComponent? largestComponent,
        int maskWidth,
        int maskHeight,
        long geometryPixelCount)
    {
        ArgumentNullException.ThrowIfNull(components);

        if (maskWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maskWidth));
        }

        if (maskHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maskHeight));
        }

        if (geometryPixelCount < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(geometryPixelCount));
        }

        Components = components;
        LargestComponent = largestComponent;
        MaskWidth = maskWidth;
        MaskHeight = maskHeight;
        GeometryPixelCount = geometryPixelCount;
    }

    /// <summary>
    /// Obtiene todos los componentes detectados.
    /// </summary>
    public IReadOnlyList<BoardGeometryComponent> Components { get; }

    /// <summary>
    /// Obtiene el componente de mayor cantidad de píxeles.
    /// </summary>
    public BoardGeometryComponent? LargestComponent { get; }

    /// <summary>
    /// Obtiene el ancho de la máscara analizada.
    /// </summary>
    public int MaskWidth { get; }

    /// <summary>
    /// Obtiene el alto de la máscara analizada.
    /// </summary>
    public int MaskHeight { get; }

    /// <summary>
    /// Obtiene la cantidad total de píxeles geométricos de la máscara.
    /// </summary>
    public long GeometryPixelCount { get; }

    /// <summary>
    /// Obtiene la cantidad de componentes encontrados.
    /// </summary>
    public int ComponentCount => Components.Count;

    /// <summary>
    /// Indica si se encontró al menos un componente.
    /// </summary>
    public bool HasComponents =>
        Components.Count > 0;

    /// <summary>
    /// Indica si existe un componente dominante.
    /// </summary>
    public bool HasLargestComponent =>
        LargestComponent is not null;
}
