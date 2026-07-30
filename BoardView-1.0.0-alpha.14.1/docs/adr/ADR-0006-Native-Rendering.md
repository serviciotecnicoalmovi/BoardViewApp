# ADR-0006: Native rendering from BoardDocument

Status: Accepted in 0.5.3-dev.1.

## Decision

All native drawing is performed from the normalized `BoardDocument` and its spatial index.
Source parsers and WebView2 are not dependencies of `BoardView.Rendering`.

## Consequences

Every supported file format can reuse selection, zoom, pan, layer visibility and future PCB
tools. The integrated PDF viewer remains available as a fidelity reference during migration.
