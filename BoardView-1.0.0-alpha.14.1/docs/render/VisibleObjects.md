# Visible-object retrieval

The rendering layer consumes `BoardDocument` and never format-specific data. Render candidates
are obtained from the document spatial index through `BoardElementQuery`. Layer visibility and
visual order are applied after spatial candidate reduction.

The current viewport fits the complete document, therefore its visible query is the document
bounds. Future zoom and pan work will convert the screen viewport to document coordinates and
use the same query path without changing the renderer contract.
