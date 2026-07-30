# Arquitectura de BoardView 0.4.0

BoardView utiliza una arquitectura modular con dependencias dirigidas hacia contratos estables.

```text
BoardView.App
  ├── BoardView.Application
  ├── BoardView.Configuration
  ├── BoardView.Infrastructure
  ├── BoardView.Formats
  ├── BoardView.Plugins
  └── BoardView.Rendering

BoardView.Application ──> BoardView.Core / BoardView.Contracts
BoardView.Configuration ──> BoardView.Contracts
BoardView.Plugins ──> BoardView.Plugin.Abstractions / BoardView.Core
BoardView.Formats ──> BoardView.Core
BoardView.Rendering ──> BoardView.Core
```

## Reglas

1. La interfaz no contiene parsers ni lógica de persistencia.
2. Los lectores convierten los archivos al modelo interno de `BoardView.Core`.
3. El renderizado consume únicamente el modelo interno.
4. Los plugins dependen de `BoardView.Plugin.Abstractions`, nunca de la aplicación WPF.
5. Las rutas y servicios del sistema se obtienen mediante contratos inyectables.
6. Las versiones se definen centralmente en `Directory.Build.props` y `ApplicationInformation`.
