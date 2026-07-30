# BoardView.SemanticKernel

## Objetivo

`BoardView.SemanticKernel` asigna significado electrónico o documental a las primitivas ya normalizadas por `BoardView.GeometryKernel`. No interpreta operadores PDF y no modifica `BoardDocument`.

## Flujo

```text
GeometryDatabaseSnapshot
        +
GeometryClassificationResult
        +
RecognitionResult
        ↓
SemanticKernelEngine
        ↓
SemanticAnalysisResult
```

## Semánticas iniciales

- Pad
- Via
- Hole
- Copper
- ComponentBody
- Silkscreen
- BoardOutline
- Mechanical
- Text
- Unknown

Las reglas priorizan evidencia explícita y resultados reconocidos. Después utilizan el tipo de capa, la escala relativa respecto al documento, el cierre geométrico y la clase física de la primitiva. Cada resultado conserva la regla y el nivel de confianza que produjeron la decisión.

## Diagnóstico

`Herramientas → Geometry Inspector → Semántica` muestra los contadores y la clasificación detallada. La exportación JSON incluye la instantánea semántica completa para comparar versiones.

## Restricciones de esta versión

La clasificación es conservadora y no reemplaza aún el reconocimiento de componentes. `Unknown` es un resultado válido cuando no existe evidencia suficiente. Las siguientes versiones refinarán reglas por contexto, vecindad, capas y patrones de footprints.
