# Footprint Template Engine

El motor compara métricas normalizadas de cada cluster contra archivos JSON desplegados en `Footprints/`.

## Factores

- cantidad de pads;
- filas y columnas;
- pitch;
- ocupación de matriz;
- simetría;
- relación de aspecto;
- restricciones topológicas.

El resultado conserva todos los factores, el score total y el umbral de aceptación. Una plantilla nueva se incorpora copiando un JSON válido sin recompilar `BoardView.Recognition`.
