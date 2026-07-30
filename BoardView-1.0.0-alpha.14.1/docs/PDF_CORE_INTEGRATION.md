# Integración PDF → Core — v0.5.1-dev.1

## Objetivo

Esta etapa conecta la extracción PDF existente con el modelo interno único de BoardView sin modificar la interfaz. El flujo queda dividido en dos responsabilidades explícitas:

```text
Archivo PDF
    │
    ▼
PdfTechnicalDocumentParser
    │
    ▼
TechnicalDocument
    │
    ▼
PdfBoardDocumentConverter
    │
    ▼
BoardDocument validado
```

`TechnicalDocument` conserva el contenido extraído fielmente. `BoardDocument` contiene la representación normalizada que consumirán renderizado, búsqueda, selección y herramientas.

## Organización multipágina

Las páginas PDF no comparten el mismo espacio global. Cada página se desplaza verticalmente y se separa por 10 mm para impedir superposiciones. `BoardDocumentPage` conserva:

- número de página;
- ancho y alto en milímetros;
- desplazamiento dentro del documento;
- límites globales;
- capas asociadas.

## Capas generadas

Por cada página se crean tres capas independientes:

- vectores;
- texto;
- imágenes.

La separación permite ocultar, seleccionar y analizar cada clase de contenido sin volver a interpretar el PDF.

## Conversión geométrica

| Gráfico técnico | Elemento interno |
|---|---|
| `TextGraphic` | `TextElement` |
| `LineGraphic` | `VectorLineElement` |
| `PolylineGraphic` | `VectorPolylineElement` |
| `BezierGraphic` | `VectorBezierElement` |
| `CircleGraphic` | `VectorEllipseElement` |
| `RectangleGraphic` | `VectorRectangleElement` |
| `ImageGraphic` | `RasterImageElement` |

Las primitivas documentales no reciben semántica eléctrica. Una línea PDF no se convierte en pista y un círculo no se convierte automáticamente en vía o pad. Esa inferencia pertenecerá a un módulo de análisis posterior.

## Metadatos

Se conservan:

- metadatos generales del documento;
- metadatos de página con prefijo `page.<n>.`;
- identificador original de cada gráfico;
- formato y número de página de cada elemento;
- recuentos normalizados de páginas, capas y elementos.

## Validación

El conversor ejecuta `BoardDocument.Validate()` antes de devolver el resultado. Los errores estructurales impiden publicar un documento incompleto o inconsistente.

## Compatibilidad

La integración es aditiva. El visor PDF actual y los contratos previos no se eliminan ni cambian. El nuevo cargador podrá conectarse al flujo de aplicación en una entrega posterior, una vez validada esta base en Windows.
