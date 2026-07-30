# ADR-0010 — Semantic Kernel independiente

## Estado

Aceptado para v0.7.1-dev.1.

## Decisión

La interpretación electrónica se implementa en un proyecto independiente que consume únicamente modelos normalizados del Core. Los importadores no asignan semántica y el Geometry Kernel no conoce pads, vías ni componentes.

## Consecuencias

- Todos los formatos comparten las mismas reglas semánticas.
- La geometría original permanece inmutable.
- Cada decisión conserva confianza y regla de origen.
- Es posible comparar el resultado del clasificador sin volver a leer el archivo fuente.
