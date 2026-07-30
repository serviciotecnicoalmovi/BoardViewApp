using BoardView.Core.Documents;

namespace BoardView.Core.GeometryDatabase;

/// <summary>Construye una base de datos geométrica completa desde el modelo normalizado.</summary>
public interface IGeometryDatabaseBuilder
{
    /// <summary>Enumera todos los elementos del documento sin aplicar heurísticas electrónicas.</summary>
    GeometryDatabaseSnapshot Build(BoardDocument document);
}
