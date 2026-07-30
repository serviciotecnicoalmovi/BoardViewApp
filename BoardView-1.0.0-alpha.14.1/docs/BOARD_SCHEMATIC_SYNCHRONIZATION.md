# Sincronización Board ↔ Schematic

## Objetivo

La versión 1.0.0-alpha.10 mantiene índices textuales independientes para la placa y el esquemático, pero presenta una navegación coordinada por referencia.

## Flujo

1. Cada documento se indexa de forma independiente mediante `ISafePdfDocumentIndexer`.
2. `PdfReferenceSearchService` busca referencias exactas y acepta el sufijo técnico `_E`.
3. Los resultados se agrupan por documento.
4. La búsqueda navega la placa y el esquema a su primera coincidencia.
5. Seleccionar una página concreta conserva esa página y sincroniza el documento opuesto.
6. Ambos visores reciben la referencia activa para solicitar el resaltado al visor PDF de Edge.

## Aislamiento

Este módulo no modifica el parser geométrico, Geometry Kernel, Semantic Kernel ni Recognition Engine.
