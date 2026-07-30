# PDF Geometry Normalizer

## Objetivo

La extracción PDF conserva la geometría del archivo, pero muchas aplicaciones CAD describen un rectángulo mediante una polilínea cerrada con vértices duplicados o puntos intermedios colineales. El normalizador convierte esas rutas en primitivas rectangulares antes de crear el `BoardDocument`.

## Flujo

```text
PdfPath
  → Graphics técnicos
  → PdfGeometryNormalizer
  → RectangleGraphic / PolylineGraphic
  → BoardDocument
  → Geometry Database
  → Classification
```

## Reglas conservadoras

Una polilínea solo se convierte en rectángulo cuando:

- está cerrada;
- tiene área positiva;
- tras eliminar duplicados y puntos colineales conserva cuatro esquinas únicas;
- todos sus segmentos son horizontales o verticales;
- las cuatro esquinas coinciden con los límites geométricos.

Las rutas ambiguas permanecen como polilíneas. La normalización conserva identificador, visibilidad, capa, grosor, relleno y metadatos de origen.

## Inspector

El `Geometry Inspector` utiliza colores explícitos para filas, encabezados, cifras y advertencias. Esto evita depender de los colores predeterminados del tema de Windows y mantiene legibilidad en el tema oscuro.
