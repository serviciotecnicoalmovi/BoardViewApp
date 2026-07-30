# Módulo B — PDF Engine, fase 2

## Objetivo

Traducir los caminos vectoriales públicos de PdfPig al modelo técnico común de BoardView sin sustituir todavía el visor PDF integrado.

## Objetos generados

- Segmentos rectos: `LineGraphic`.
- Caminos de varios segmentos: `PolylineGraphic`.
- Rectángulos alineados con los ejes: `RectangleGraphic`.
- Curvas cúbicas: `BezierGraphic`.
- Palabras posicionadas: `TextGraphic`.

Todas las coordenadas y grosores quedan normalizados a milímetros. Los colores de trazo y relleno, junto con los indicadores de pintado, se conservan en los metadatos de cada objeto.

## Compatibilidad

PdfPig todavía publica versiones anteriores a 1.0. El extractor encapsula el acceso a los comandos internos mediante reflexión controlada. La propiedad pública `Page.Paths` sigue siendo el punto de entrada, mientras que el resto del programa permanece aislado de cambios menores en las clases de comandos.

## Limitaciones actuales

- Los caminos de recorte no se convierten en objetos visibles.
- Los degradados, patrones y transparencias complejas se registrarán en fases posteriores.
- El visor WebView2 continúa mostrando la representación visual original mientras se valida la fidelidad del modelo extraído.
