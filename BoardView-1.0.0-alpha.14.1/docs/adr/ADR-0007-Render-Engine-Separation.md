# ADR-0007: Separate camera, visibility and drawing responsibilities

Status: Accepted in 0.6.0-dev.1.

## Context

The initial native viewport combined camera math, spatial queries, entity drawing and pointer
interaction in one WPF control. That made the visual path difficult to test and encouraged
format-specific behavior to enter the viewport.

## Decision

The render engine is split into four responsibilities:

1. `ViewportCamera` owns view state.
2. `ViewportTransform` owns coordinate conversion.
3. `NativeBoardRenderer` owns drawing only.
4. `BoardViewport` owns WPF input and selection orchestration.

All visible entities are obtained from the `BoardDocument` spatial index before drawing.

## Consequences

The model view can render any supported source format without source-specific branches. Camera
math can be tested without opening a window. Future GPU and retained-mode backends can implement
the same frame preparation rules without changing parsers or the internal model.
