# Geometry Classification Engine

## Propósito

El Geometry Classification Engine (GCE) convierte geometría documental sin semántica en primitivas normalizadas antes de ejecutar la detección de pads, vías, agujeros o footprints.

El motor no crea componentes ni modifica `BoardDocument`. Su salida es un resultado independiente y reproducible.

## Flujo

```text
BoardDocument
    -> extracción de formas candidatas
    -> clasificación por forma y relleno
    -> agrupación por tamaño equivalente
    -> análisis de repetición y alineación
    -> GeometryClassificationResult
    -> PadDetectionEngine
```

## Primitivas reconocidas

- Rectángulo relleno.
- Rectángulo delineado.
- Elipse rellena.
- Elipse delineada.
- Donut concéntrico.
- Ranura.
- Polígono relleno o delineado.
- Pad explícito procedente de un formato estructurado.
- Agujero explícito.

Las polilíneas cerradas de cuatro esquinas se normalizan como rectángulos. Esto es necesario para PDFs donde un pad aparece como un contorno compuesto por segmentos, no como un operador rectangular nativo.

## Evidencia geométrica

Cada primitiva incluye:

- forma;
- límites;
- centro;
- estado de relleno;
- cantidad de repeticiones de tamaño equivalente;
- cantidad de vecinos alineados;
- confianza;
- identificador del elemento de origen.

Una forma delineada solo se admite como candidata conductiva cuando existe evidencia repetitiva y alineada. De esta manera se reducen falsos positivos procedentes de cajas de texto, marcos y contornos mecánicos.

## Compatibilidad

El GCE opera exclusivamente sobre el modelo común. Los lectores PDF, Gerber, KiCad, Eagle y futuros plugins pueden utilizarlo sin introducir dependencias hacia la interfaz o el renderizador.
