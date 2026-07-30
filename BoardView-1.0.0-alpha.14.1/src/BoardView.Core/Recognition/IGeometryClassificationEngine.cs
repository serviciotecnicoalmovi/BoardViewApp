using BoardView.Core.Documents;
using BoardView.Core.GeometryDatabase;

namespace BoardView.Core.Recognition;

/// <summary>
/// Clasifica geometría documental sin asignar todavía componentes, nets ni footprints.
/// </summary>
public interface IGeometryClassificationEngine
{
    /// <summary>Clasifica las primitivas geométricas contenidas en un documento normalizado.</summary>
    GeometryClassificationResult Analyze(
        BoardDocument document,
        GeometryClassificationOptions? options = null);

    /// <summary>Clasifica una instantánea geométrica ya materializada.</summary>
    GeometryClassificationResult Analyze(
        BoardDocument document,
        GeometryDatabaseSnapshot geometryDatabase,
        GeometryClassificationOptions? options = null);
}
