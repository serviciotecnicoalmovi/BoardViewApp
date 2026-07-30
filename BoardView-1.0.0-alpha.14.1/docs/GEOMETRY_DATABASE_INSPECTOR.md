# Geometry Database & Inspector

## Objetivo

La base de datos geométrica materializa **todos** los elementos de `BoardDocument` antes de aplicar clasificación electrónica, filtros de tamaño o heurísticas de pads. Su función es separar claramente tres cantidades:

1. elementos normalizados recibidos;
2. formas que el clasificador puede interpretar;
3. candidatos evaluados por el detector de pads.

## Flujo

```text
BoardDocument
    -> GeometryDatabaseBuilder
    -> GeometryDatabaseSnapshot
    -> GeometryClassificationEngine
    -> PadDetectionEngine
```

`GeometryDatabaseSnapshot` conserva identificador, capa, tipo físico, clase de origen, límites en milímetros y banderas de cierre/relleno.

## Geometry Inspector

Disponible en `Herramientas -> Geometry Inspector...`.

El inspector muestra:

- distribución completa por tipo físico;
- todos los registros geométricos;
- primitivas que llegaron al detector;
- decisiones de aceptación y descarte;
- cantidades de pads y footprints.

La cuadrícula usa virtualización para admitir documentos con miles de registros sin duplicar la geometría en memoria.

## Regla arquitectónica

La base de datos no asigna significado electrónico. No elimina entidades y no depende del viewport visible. Clasificación, detección y renderizado consumen instantáneas posteriores con responsabilidades independientes.
