# BoardView.GeometryKernel

## Responsabilidad

`BoardView.GeometryKernel` reconstruye topología geométrica independientemente del formato de origen. Recibe segmentos expresados en milímetros y devuelve primitivas normalizadas junto con los segmentos que no pudieron clasificarse.

## Pipeline

```text
Importador de formato
  -> segmentos y curvas
  -> Geometry Kernel
  -> grafo de nodos y aristas
  -> detección de ciclos
  -> primitivas normalizadas
  -> TechnicalDocument / BoardDocument
```

## Primera implementación

La versión 0.7.0-dev.1 reconoce rectángulos formados por cuatro aristas, incluso cuando las aristas pertenecen a rutas PDF distintas. La agrupación se restringe por estilo para evitar conectar geometrías incompatibles.

## Diagnóstico

El importador conserva en los metadatos de página: segmentos de entrada, nodos, aristas, ciclos evaluados, rectángulos aceptados, ciclos rechazados y segmentos restantes.

## Extensiones previstas

- Ciclos poligonales generales.
- Círculos y elipses a partir de curvas.
- Arcos.
- Slots y rectángulos redondeados.
- Donuts y regiones compuestas.
