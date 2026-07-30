# Native Render Engine

Version: 0.6.0-dev.1

## Purpose

`BoardView.Rendering` is now a source-format-independent rendering subsystem. It receives a
normalized `BoardDocument`, asks its spatial index for visible entities and draws those entities
on a dedicated WPF surface. The native model path has no dependency on PDF, WebView2, PdfPig,
Gerber, KiCad or any other parser.

## Components

- `ViewportCamera` owns zoom and pan state.
- `ViewportTransform` performs reversible world/screen transformations.
- `NativeBoardRenderer` renders normalized entities and has no interaction state.
- `NativeRenderFrame` contains the visible elements and layer snapshot for one frame.
- `BoardViewport` connects input, selection, camera, spatial index and renderer.

## Rendering pipeline

```text
BoardDocument
    -> SpatialIndex query using visible world bounds
    -> NativeRenderFrame
    -> NativeBoardRenderer
    -> WPF DrawingContext
```

The renderer never enumerates the complete document when a viewport area is available.

## PDF presentation modes

- **PDF** displays the original document with the integrated WebView2 PDF viewer.
- **Model** hides the PDF surface and displays only the native `BoardDocument` render.
- **Overlay** keeps the PDF as a reference and requests a transparent native composition layer.

WebView2 uses a native child surface. Exact interactive overlay registration is outside this
version and requires a shared PDF/native camera service or a captured PDF backing surface.
This limitation does not affect **Model**, which is fully independent from the PDF viewer.

## Interaction

- Mouse wheel: zoom around the pointer.
- Right-button drag: pan.
- Ctrl + left-button drag or Space + left-button drag: pan.
- Left click: select the closest visible indexed entity.
- Double left click: fit the complete document.

## Supported normalized entities

Lines, polylines, cubic Bézier curves, rectangles, circles, ellipses, text, raster placeholders,
tracks, pads, vias, polygons, circular arcs and drill holes.
