# ADR-0002: Contrato estable de plugins

- Estado: aceptado
- Versión: 0.4.0

## Decisión

Los plugins implementan `IBoardViewPlugin` y exponen `PluginMetadata`. La inicialización devuelve `OperationResult` para errores esperados.

## Motivo

Evita que los plugins conozcan la aplicación WPF y permite validar compatibilidad antes de integrarlos al proceso principal.
