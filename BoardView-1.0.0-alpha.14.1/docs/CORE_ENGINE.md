# Core Engine — v0.5.0-dev.1

## Objetivo

El Core define el modelo interno único de BoardView. Ningún parser ni renderizador debe crear
un modelo alternativo. Los lectores convierten el archivo fuente a `BoardDocument`; búsqueda,
renderizado y herramientas consumen ese documento.

## Agregados principales

- `BoardDocument`: raíz y propietario de capas, nets, componentes y elementos.
- `CoordinateSpace`: normalización de unidades y orientación.
- `BoardLayer`: capa lógica, visibilidad, bloqueo y opacidad.
- `BoardNet`: red eléctrica y vínculos con elementos.
- `BoardComponent`: referencia, huella, posición y elementos asociados.
- `BoardElement`: base geométrica extensible con capa, net, componente y propiedades.
- `SpatialIndex<T>`: consultas por punto y área, actualización y eliminación.
- `BoardDocumentValidator`: validación explícita de integridad.

## Reglas

1. La unidad mundial del Core es el milímetro.
2. Los identificadores son únicos y sensibles a mayúsculas.
3. Un elemento solo puede agregarse si existen sus referencias de capa, net y componente.
4. El índice espacial se reconstruye automáticamente al cambiar el conjunto de elementos.
5. El Core no referencia WPF, PDF, Gerber ni bibliotecas de interfaz.
