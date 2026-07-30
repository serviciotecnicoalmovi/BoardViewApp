# Pad Detection Engine

## Propósito

La versión 0.6.2 separa la detección geométrica de bajo nivel del futuro reconocimiento de componentes. El motor no interpreta referencias de texto y nunca crea componentes.

## Flujo

```text
BoardDocument
    -> candidatos geométricos
    -> pads aceptados
    -> vías y agujeros
    -> agrupación de pads
    -> footprints candidatos
```

## Reglas

- Un footprint contiene al menos dos pads.
- Sus límites se calculan exclusivamente desde los pads asociados.
- Las vías se clasifican entre los candidatos circulares más pequeños.
- Los agujeros explícitos conservan su propiedad metalizada.
- El documento normalizado no se modifica durante el análisis.
- La detección conserva el identificador del elemento de origen para diagnóstico y cross-probe futuro.

## Diagnóstico visual

El menú `Ver > Diagnóstico` permite activar por separado pads, vías, agujeros y footprints. Esta representación es diagnóstica y no altera el modelo interno.

## Responsabilidad futura

La asociación de referencias y la creación de componentes se realizará en una etapa posterior, únicamente sobre footprints validados.
