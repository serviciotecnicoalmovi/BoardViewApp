# Search architecture

Textual searches use document identity indexes and normalized properties. Coordinate and
proximity searches use the shared spatial index. Domain filters can restrict results by layer,
net, component and element type without traversing complete element collections.

Search results must retain stable element identifiers so they can be consumed by selection,
highlighting and cross-probe services.
