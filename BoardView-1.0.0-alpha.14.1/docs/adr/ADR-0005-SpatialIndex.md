# ADR-0005 — Shared spatial index

## Status

Accepted in version 0.5.2-dev.1.

## Decision

Each `BoardDocument` owns one `ISpatialIndex<BoardElement>`. Rendering, selection, spatial
search and analysis use this service instead of constructing private indexes or scanning the
full element collection.

The first implementation is a thread-safe uniform grid because PCB objects are numerous,
mostly compact and distributed over a bounded two-dimensional workspace. The public contract
is independent of that implementation, allowing a future R-tree or hierarchical grid without
changing consumers.

## Consequences

- Element additions, removals and bounds changes must update the index transactionally.
- Query results expose diagnostics and the observed index version.
- Format parsers remain unaware of index internals; they only populate `BoardDocument`.
