# Modelo interno de BoardView

BoardView utiliza un modelo propio e independiente del formato de entrada.

## Flujo

```text
Archivo -> Parser -> BoardDocument -> Renderizador / Herramientas
```

Un parser nunca dibuja. Un renderizador nunca interpreta PDF, Gerber, KiCad ni otro formato.

## Convención geométrica

- Todas las coordenadas y dimensiones internas se almacenan en milímetros.
- `Point2D`, `Vector2D` y `Bounds2D` no dependen de WPF ni de SkiaSharp.
- La conversión desde unidades del archivo original se realiza dentro del parser correspondiente.

## Agregados principales

- `BoardDocument`: raíz del documento.
- `BoardLayer`: capa lógica y orden de presentación.
- `BoardNet`: red eléctrica.
- `BoardComponent`: componente colocado.
- `BoardElement`: base de la geometría representable.
- `TrackElement`, `ViaElement`, `PadElement` y `PolygonElement`: primeras primitivas de placa.

## Integridad

`BoardDocument` impide identificadores duplicados y rechaza elementos que referencien capas o redes inexistentes.
