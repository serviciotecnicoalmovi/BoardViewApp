# Arquitectura de BoardView

## Regla de dependencias

`BoardView.Core` no depende de WPF ni de ningún formato de archivo.

- `BoardView.Infrastructure` implementa configuración, registro y composición.
- `BoardView.Formats` detecta formatos y posteriormente alojará parsers.
- `BoardView.Rendering` representa el modelo interno sin conocer el archivo original.
- `BoardView.Plugins` descubre extensiones sin modificar el núcleo.
- `BoardView.App` compone los módulos y contiene la interfaz WPF.

## Flujo previsto

Archivo -> detector/parser -> modelo interno -> motor de renderizado -> herramientas de interacción.
