using BoardView.GeometryKernel.Graph;

namespace BoardView.GeometryKernel;

/// <summary>Contrato común para reconstruir primitivas a partir de segmentos lineales.</summary>
public interface IGeometryKernel
{
    /// <summary>Construye topología y reconoce primitivas geométricas.</summary>
    GeometryKernelResult Build(IEnumerable<GeometrySegment> segments, CancellationToken cancellationToken = default);
}
