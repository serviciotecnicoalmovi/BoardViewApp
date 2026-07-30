# Spatial Index

## Purpose

`SpatialIndex<T>` is the shared two-dimensional query service for rendering, selection,
measurement, search, net tracing and cross-probe. It prevents modules from scanning every
object in a `BoardDocument` when only a bounded region is relevant.

## Data structure

The implementation uses a uniform grid. Every object is registered in each cell intersected
by its axis-aligned bounds. Query candidates are deduplicated before exact bounds and domain
filters are evaluated.

The implementation is thread-safe for concurrent readers and serialized mutations. Every
successful mutation increments `Version`; query results record the version they observed.

## Operations

- Single and batch insertion.
- Incremental bounds update.
- Removal and clear.
- Rectangle and point queries.
- Circular proximity queries ordered by distance.
- Optional predicate and result limit.
- Operational statistics.

## Board-domain filters

`BoardElementQuery` adds filters for visibility, layers, nets, components and element types.
`BoardDocument` owns the single index instance and updates it incrementally when elements are
added, moved or removed.

## Coordinate requirements

All indexed coordinates are finite values in the normalized document coordinate system.
Current `BoardDocument` instances use millimeters.
