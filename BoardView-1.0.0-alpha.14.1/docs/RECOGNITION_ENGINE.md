# Recognition Engine

`BoardView.Recognition` transforma pads reconocidos en clusters, footprints y componentes.

## Pipeline

1. Índice espacial uniforme de pads.
2. Clustering conectado con radio adaptativo.
3. Cálculo de filas, columnas, pitch, simetría y rotación.
4. Clasificación inicial de CHIP-2, conectores, SOIC, TSSOP, QFN, QFP, BGA y arrays.
5. Asociación de referencias textuales cercanas.

El motor no modifica `BoardDocument` ni los resultados del Geometry/Semantic Kernel.
