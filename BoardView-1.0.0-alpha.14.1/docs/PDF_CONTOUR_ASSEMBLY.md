# Ensamblado de contornos PDF

## Objetivo

Reconstruir la geometría completa de cada `PdfPath` antes de decidir si debe publicarse como línea, polilínea, rectángulo o polígono.

## Pipeline

1. Se leen todas las subrutas del camino PDF.
2. Se conservan intactos los contornos cerrados y las curvas Bézier.
3. Las subrutas lineales abiertas se conectan únicamente cuando sus extremos coinciden dentro de una tolerancia física y el nodo tiene grado dos.
4. Un circuito cerrado se entrega completo al `PdfGeometryNormalizer`.
5. Solo las rutas que permanecen abiertas se publican como líneas o polilíneas.

## Seguridad geométrica

No se conectan nodos con más de dos extremos. Esta restricción evita convertir bifurcaciones, pistas o diagramas complejos en polígonos inexistentes.

## Validación

El proyecto de pruebas reconstruye un rectángulo a partir de cuatro segmentos independientes y confirma que el resultado final es un `RectangleGraphic` de las dimensiones esperadas.
