# Diagnóstico geométrico y normalización de unidades

## Objetivo

La versión 0.6.3-dev.2 hace observable cada etapa entre la geometría normalizada y los pads aceptados. El motor no ajusta umbrales silenciosamente: registra el resultado de cada primitiva y el motivo exacto de descarte.

## Unidades

`BoardDocument` trabaja exclusivamente en milímetros. El lector PDF convierte puntos PDF a milímetros antes de crear los elementos del modelo. El detector combina límites relativos al documento con límites físicos de 0,04 mm y 20 mm para evitar que textos, márgenes o páginas múltiples distorsionen la escala.

## Métricas

La barra superior muestra:

- primitivas clasificadas;
- candidatos evaluados;
- pads aceptados después de eliminar duplicados;
- footprints construidos.

El estado y el log incluyen los descartes por tamaño, proporción, contorno sin patrón, geometría no soportada y confianza insuficiente.

## Regla de aceptación

Una forma delineada solo puede aceptarse como pad cuando pertenece a un grupo de tamaño equivalente y tiene al menos un vecino alineado. Las formas rellenas se evalúan con un umbral de confianza independiente. Los pads explícitos de formatos estructurados se conservan sin inferencia.
