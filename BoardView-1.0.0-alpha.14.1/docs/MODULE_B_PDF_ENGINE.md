# Módulo B — PDF Engine (fase 1)

## Objetivo

Convertir el contenido textual posicionado de un PDF al modelo común de
`BoardView.Core`, sin exponer tipos de PdfPig fuera de `BoardView.Formats`.

## Flujo actual

```text
PDF
  -> PdfPig
  -> PdfTechnicalDocumentParser
  -> TechnicalDocument
  -> DocumentPage
  -> TextGraphic
```

## Normalización

- Las dimensiones y coordenadas se convierten de puntos PDF a milímetros.
- Se conserva el origen inferior izquierdo del PDF.
- Cada palabra se transforma en un `TextGraphic` con identificador estable.
- Cada página registra tamaño original, número de operaciones y palabras.
- El documento registra parser, páginas y cantidad total de objetos gráficos.

## Límite de esta fase

Las operaciones vectoriales crudas se contabilizan, pero todavía no se
traducen a líneas, curvas, rectángulos ni polígonos. Esa traducción se añadirá
en la fase 2 del módulo B después de validar esta frontera arquitectónica.
