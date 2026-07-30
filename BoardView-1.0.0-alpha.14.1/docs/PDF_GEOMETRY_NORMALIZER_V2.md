# PDF Geometry Normalizer v2

## Objetivo

Normalizar la geometría durante la lectura del camino PDF, antes de crear el
`TechnicalDocument`. Esta etapa no depende del detector de pads.

## Corrección principal

Una subruta de PdfPig puede contener varios comandos `MoveTo`. Cada `MoveTo`
inicia un contorno independiente. La implementación anterior acumulaba todos
los puntos en una única colección, lo que convertía varios rectángulos reales
en una polilínea grande e imposible de clasificar.

El extractor ahora:

1. inicia un contorno con cada `MoveTo`;
2. conserva líneas y Bézier del contorno actual;
3. cierra el contorno cuando recibe `ClosePath`;
4. normaliza y publica cada contorno por separado;
5. sustituye una polilínea rectangular por `RectangleGraphic`;
6. conserva como polígono cualquier contorno cerrado que no sea rectangular;
7. conserva rutas abiertas como líneas o polilíneas.

## Validación visual

Después de abrir el PDF, usar `Herramientas → Geometry Inspector`. La corrección
se considera efectiva cuando el número de rectángulos aumenta y el de
polilíneas disminuye sin perder el total geométrico del documento.

El botón `Exportar JSON` permite guardar una instantánea reproducible de la base
geométrica y del diagnóstico de reconocimiento.
