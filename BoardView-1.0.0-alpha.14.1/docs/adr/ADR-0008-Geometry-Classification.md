# ADR-0008 — Clasificación geométrica previa al reconocimiento electrónico

## Estado

Aceptada.

## Contexto

La detección directa de pads sobre rectángulos y elipses genéricos produjo cero resultados en PDFs donde las formas estaban representadas mediante polilíneas cerradas o contornos sin relleno.

## Decisión

Introducir un Geometry Classification Engine independiente entre `BoardDocument` y los motores de reconocimiento. El clasificador utiliza forma, relleno, repetición y alineación, pero no asigna semántica de componente o net.

## Consecuencias

- Los detectores consumen primitivas homogéneas.
- Se conservan los datos originales sin mutación.
- Los formatos estructurados pueden aportar pads explícitos con confianza total.
- Los PDFs pueden aportar candidatos mediante patrones geométricos verificables.
- La clasificación puede probarse y evolucionar sin afectar al renderizador.
