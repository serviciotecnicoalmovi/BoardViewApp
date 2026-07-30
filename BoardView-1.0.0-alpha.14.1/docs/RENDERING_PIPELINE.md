# Canal de renderizado

`BoardView.Rendering` recibe únicamente un `BoardDocument`. No abre archivos ni conoce PDF, Gerber, KiCad u otros formatos.

## Flujo

1. El parser convierte el archivo original al modelo interno.
2. `BoardDocument.Bounds` entrega los límites globales.
3. `BoardViewport` transforma milímetros a píxeles e invierte el eje Y.
4. Los elementos se ordenan por `BoardLayer.Order`.
5. Se representan únicamente capas y elementos visibles.

## Alcance 0.2.5

- Ajuste automático a pantalla.
- Pistas con extremos redondeados.
- Pads circulares, rectangulares, ovalados y redondeados.
- Vías con taladro visible.
- Polígonos rellenos o delineados.
- Colores diferenciados para cobre superior, inferior y contorno.
