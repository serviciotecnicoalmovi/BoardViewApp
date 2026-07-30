# v1.0.0-alpha.14.1 — Corrección de compatibilidad WebView2

- Corrige CS1061 causado por el uso de `CoreWebView2Controller`, miembro que no está expuesto por el control WPF `WebView2`.
- Mantiene la detección de cambios de DPI sin depender de APIs internas o específicas de WinForms.
- No modifica la interfaz, la búsqueda ni la carga de documentos.

## [1.0.0-alpha.14] - 2026-07-27

### Añadido
- Sincronización de la escala de rasterización de WebView2 con el DPI físico del monitor.
- Compatibilidad dinámica con `RasterizationScale` y `ShouldDetectMonitorScaleChanges`.

### Cambiado
- El visor PDF usa redondeo de diseño y ajuste a píxeles físicos.
- El zoom general de WebView2 permanece en 100 % para evitar una doble ampliación sobre el zoom interno del PDF.

### Conservado
- No se modifica la interfaz, la búsqueda, la indexación ni la sincronización Board ↔ Schematic.

## [1.0.0-alpha.13] - 2026-07-27

### Añadido
- PDFium x64 actualizado mediante `bblanchon.PDFium.Win32` para la extracción textual.
- Interop nativo mínimo y aislado para cargar documentos, páginas, caracteres y coordenadas.
- Reconstrucción de palabras desde glifos PDFium.
- Reconstrucción de referencias divididas en varios fragmentos contiguos.
- Prueba determinista para `L` + `305` + `_E`.

### Cambiado
- `SafePdfDocumentIndexer` ya no usa PdfPig ni modifica copias temporales del PDF.
- WebView2 permanece como visor y PdfPig permanece como parser geométrico.
- La indexación PDFium se serializa para proteger el estado global de la biblioteca nativa.

## [1.0.0-alpha.10] - 2026-07-27

### Añadido
- Servicio puro `PdfReferenceSearchService` para búsquedas exactas de referencias.
- Equivalencia entre referencias con y sin sufijo técnico `_E`.
- Resultados agrupados de forma independiente para placa y esquemático.
- Navegación cruzada: seleccionar una coincidencia centra el documento elegido y la primera coincidencia del documento asociado.
- Resaltado simultáneo mediante el término de búsqueda enviado a ambos visores PDF.
- Pestaña **Componente** con referencia activa, páginas, cantidades y estado de localización.
- Prueba determinista para referencias exactas, sufijos y falsos positivos.

### Cambiado
- La búsqueda inicial navega automáticamente ambos documentos cuando la referencia existe en los dos.
- La reapertura de un proyecto restaura y vuelve a ejecutar la última búsqueda.
- La versión visible de MainShell se actualiza a alpha.10.

### Conservado
- Indexación segura de alpha.9.
- Geometry Kernel, Semantic Kernel, Recognition Engine y plantillas sin modificaciones.

## [1.0.0-alpha.9] - 2026-07-27

### Añadido

- Indexador textual seguro `ISafePdfDocumentIndexer`.
- Indexación asíncrona e independiente para placa y esquemático.
- Procesamiento página por página con advertencias no fatales.
- Copia temporal protegida que neutraliza anotaciones y destinos PDF incompatibles.
- Resultados de búsqueda por documento, página y cantidad de coincidencias.
- Navegación directa al seleccionar una coincidencia.

### Conservado

- El archivo PDF original nunca se modifica.
- Los visores continúan operativos aunque el índice textual falle parcial o totalmente.
- Geometry Kernel, Semantic Kernel y Recognition Engine permanecen sin cambios.

## [1.0.0-alpha.8] - 2026-07-27

### Corregido

- Se restaura la referencia de espacio de nombres `BoardView.Core.Pdf` requerida por `RepairWorkspaceViewModel`.
- Se corrigen los cuatro errores `CS0246` relacionados con `PdfDocumentIndex` y `PdfPage`.
- Se mantiene deshabilitada la invocación de PdfPig durante la apertura del Repair Workspace; no se reintroduce el error `/DotRet`.
- No se añaden funciones nuevas.

## [1.0.0-alpha.7] - 2026-07-27

### Corregido

- El Repair Workspace ya no invoca PdfPig automáticamente al abrir una placa o un esquemático.
- Se elimina de forma definitiva la excepción de primera oportunidad causada por destinos PDF no conformes como `/DotRet`.
- Los documentos continúan abriéndose en el visor integrado; la indexación textual interna queda desactivada hasta disponer del indexador seguro independiente.

# Changelog

## [1.0.0-alpha.6] - 2026-07-27

### Corregido

- Añadida una inspección binaria previa para detectar el destino PDF no conforme `/DotRet`.
- Los documentos afectados ya no se entregan a PdfPig, evitando `PdfDocumentFormatException`.
- El PDF continúa disponible en el visor integrado.
- El análisis técnico y la búsqueda textual se omiten únicamente para el documento incompatible.
- El Repair Workspace recibe un índice de páginas seguro y no cierra la aplicación.
- No se añadieron funciones visuales nuevas.

## [1.0.0-alpha.5] - 2026-07-27

### Corregido

- Añadida una configuración PDF tolerante y común para Inspector, Indexer y parser técnico.
- Activado el análisis permisivo y la omisión de fuentes dañadas.
- Omitidas las anotaciones PDF cuando la versión instalada de PdfPig expone esa opción.
- Evitada la resolución del destino no conforme `/DotRet`, que provocaba `PdfDocumentFormatException`.
- No se añadieron funciones nuevas ni cambios visuales.

## [1.0.0-alpha.4] - 2026-07-27

### Corregido

- Corregida la prueba `VerifyFootprintTemplateEngine` para usar `System.IO.Path` de forma explícita.
- Eliminados los dos errores `CS0103` de compilación en `BoardView.Core.Tests/Program.cs`.
- No se añadieron cambios funcionales.

## [1.0.0-alpha.4] - 2026-07-27

### Changed
- Reemplazado completamente el contenido de MainWindow por la MainShell definitiva.
- Eliminada cualquier dependencia visual del antiguo Repair Workspace.
- Integrados visores Board y Schematic reutilizables en una sola ventana maximizada.
- Añadidos encabezados visibles, estados vacíos, barra de herramientas, explorador, resultados, notas y barra de estado.
- Forzado el inicio maximizado para impedir que configuraciones antiguas oculten la barra superior.

### Compatibility
- Geometry Kernel, Semantic Kernel, Recognition Engine y Footprint Template Engine permanecen sin cambios.

## [1.0.0-alpha.2] - 2026-07-27

### Changed
- Reemplazada la integración provisional por una MainShell única y funcional.
- Integrados permanentemente los visores Board y Schematic dentro de la ventana principal.
- Añadidos estados visuales vacío, carga y error en cada visor PDF.
- Añadidos paneles redimensionables para explorador, ambos documentos e inspector.
- Integradas búsqueda, navegación y anotaciones sin ventanas secundarias.
- Conservados los motores técnico, geométrico, semántico y de reconocimiento.

## [1.0.0-alpha.1] - 2026-07-27

### Added
- Workspace unificado dentro de la ventana principal.
- Paneles permanentes y redimensionables para placa, esquemático e inspector.
- Apertura, búsqueda, anotaciones y persistencia de proyectos de reparación sin ventanas secundarias.
- Pestaña de modelo técnico que conserva el render nativo, PDF y superposición existentes.
- Barra de estado conjunta para el motor técnico y la sesión de reparación.

### Changed
- Repair Workspace deja de abrirse como ventana independiente.
- La navegación principal se organiza alrededor del flujo real de diagnóstico de placas.

### Compatibility
- Geometry Kernel, Semantic Kernel, Recognition Engine y Footprint Template Engine permanecen sin cambios.

# 0.9.0-dev.1

- Añadido Footprint Template Engine extensible mediante JSON.
- Añadida biblioteca externa de CHIP, SOIC, TSSOP, QFN, QFP, BGA, FFC, conectores y arrays.
- Añadido cálculo de score por pads, ejes, pitch, ocupación, simetría, proporción y topología.
- Cada footprint conserva plantilla, score, factores y estado auditable.
- Geometry Inspector muestra plantilla, score y estado.
- Añadida biblioteca integrada de respaldo y prueba de carga.

# 0.8.0-dev.1

- Nuevo proyecto BoardView.Recognition.
- Clustering espacial adaptativo de pads.
- Solver de footprints con pitch, filas, columnas, simetría y rotación.
- Construcción de componentes y asociación de referencias.
- Nueva pestaña Componentes en Geometry Inspector.
- Prueba ejecutable del Recognition Engine.

# Changelog

## [0.7.1-dev.1] - 2026-07-27

### Added

- Nuevo proyecto `BoardView.SemanticKernel`.
- Contrato `ISemanticKernel` y motor `SemanticKernelEngine`.
- Semánticas Pad, Via, Hole, Copper, ComponentBody, Silkscreen, BoardOutline, Mechanical, Text y Unknown.
- Resultado inmutable con contadores, confianza y regla de clasificación.
- Pestaña **Semántica** en Geometry Inspector.
- Exportación JSON de clasificación semántica.
- Verificación ejecutable del Semantic Kernel.
- Documentación técnica y ADR-0010.

### Changed

- La composición de la aplicación ejecuta el análisis semántico después del reconocimiento geométrico.
- La barra PDF informa también la cantidad de entidades semánticas desconocidas.

## [0.7.0-dev.1] - 2026-07-27

### Added
- Proyecto independiente `BoardView.GeometryKernel`.
- Grafo topológico de nodos y aristas con ajuste por tolerancia.
- Reconocimiento de ciclos rectangulares de cuatro aristas a nivel de página completa.
- Diagnóstico de segmentos, nodos, ciclos, rectángulos aceptados y segmentos restantes.
- Prueba determinista del núcleo geométrico.

### Changed
- El importador PDF acumula los segmentos lineales de toda la página antes de publicar geometría.
- Los segmentos consumidos por un rectángulo no se duplican como líneas.
- El Recognition Engine y las heurísticas de pads permanecen sin cambios.

## 0.6.6-dev.2 - 2026-07-27

### Corregido

- La clasificación se ejecuta ahora después de reconstruir el contorno completo del `PdfPath`.
- Las subrutas lineales abiertas del mismo camino PDF se ensamblan por sus extremos antes de emitirse como líneas.
- Cuatro segmentos independientes que forman un circuito cerrado pueden convertirse en un único `RectangleGraphic`.
- Los nodos con bifurcaciones no se ensamblan, evitando inventar contornos en pistas o diagramas complejos.
- Las rutas que permanecen abiertas continúan publicándose como líneas o polilíneas.

### Pruebas

- Añadida una prueba determinista que reconstruye cuatro segmentos desordenados, verifica el cierre y confirma su normalización como rectángulo.

### Sin cambios

- No se modificaron el detector de pads, footprints ni el Recognition Engine.

## 0.6.6-dev.1 - 2026-07-27

### Añadido

- Normalización de contornos PDF dentro de `PdfVectorPathExtractor`, antes de publicar `TechnicalDocument`.
- Separación de múltiples contornos descritos mediante varios comandos `MoveTo` dentro de una misma subruta PDF.
- Conversión inmediata de contornos rectangulares cerrados a `RectangleGraphic`.
- Clasificación de contornos cerrados no rectangulares como polígonos normalizados.
- Exportación JSON completa desde Geometry Inspector.
- Iconos por tipo geométrico y columnas ordenables en el inspector.

### Corregido

- Las coordenadas de distintos contornos ya no se mezclan en una única polilínea.
- Un rectángulo normalizado sustituye a la polilínea original en lugar de duplicarla.
- El reconocimiento de círculos Bézier se ejecuta por contorno independiente.

### Sin cambios

- No se modificaron las heurísticas del detector de pads ni del Recognition Engine.

## 0.6.5-dev.2 - 2026-07-27

### Correcciones

- Restaurados los auxiliares de lectura reflexiva eliminados accidentalmente de `PdfVectorPathExtractor`.
- Restaurada la lectura de números, colecciones, puntos, segmentos, curvas Bézier y círculos.
- Corregidos los errores CS0103 observados en `BoardView.Formats`.
- Sin cambios funcionales adicionales respecto de v0.6.5-dev.1.

## [0.6.5-dev.2] - PDF Geometry Normalizer

### Añadido
- Normalizador PDF previo al modelo interno para convertir polilíneas rectangulares en `RectangleGraphic`.
- Eliminación segura de vértices duplicados y puntos colineales intermedios.
- Pruebas de normalización para rectángulos subdivididos y rellenos.
- Documento técnico `docs/PDF_GEOMETRY_NORMALIZER.md`.

### Corregido
- Reconocimiento de rectángulos PDF con más de cuatro puntos y cierre repetido.
- Conservación del estado de relleno y metadatos durante la normalización.
- Contraste del `Geometry Inspector`: encabezados, filas, cifras y advertencias usan colores explícitos del tema oscuro.


## [0.6.4-dev.1] - Geometry Database & Inspector

### Added
- Base de datos geométrica completa previa a cualquier heurística electrónica.
- Inventario tipado de líneas, polilíneas, Bézier, rectángulos, elipses, polígonos, textos, imágenes, pads, vías, agujeros, pistas y arcos.
- Geometry Inspector accesible desde el menú Herramientas.
- Vistas de resumen, registros geométricos y decisiones de candidatos.
- Métricas visibles para comparar elementos recibidos, clasificados y aceptados.
- Pruebas del constructor geométrico y de conservación integral de elementos.

### Changed
- `RecognitionResult` conserva la instantánea geométrica utilizada por la ejecución.
- `PadDetectionEngine` construye explícitamente la base geométrica antes de clasificar.
- La barra de análisis distingue registros geométricos de primitivas clasificadas.


## [0.6.3-dev.2] - 2026-07-27

### Añadido
- Diagnóstico cuantitativo completo del flujo geometría → candidato → pad.
- Motivos de descarte trazables: tamaño, proporción, geometría, contorno, confianza y duplicado.
- Contadores visibles de primitivas clasificadas, candidatos, pads y footprints.
- Límites físicos explícitos en milímetros para impedir discrepancias de escala.

### Corregido
- Los umbrales de tamaño combinan escala documental y límites físicos normalizados.
- Los contornos repetidos y alineados admiten un umbral de confianza apropiado para PDF técnicos sin relleno.
- El registro de aplicación informa exactamente en qué etapa se descarta cada forma.

## [0.6.3-dev.1] - 2026-07-27

### Añadido

- Geometry Classification Engine independiente del detector de pads.
- Clasificación de rectángulos, elipses, donuts, ranuras y polígonos.
- Reconocimiento de rectángulos construidos mediante polilíneas cerradas.
- Métricas de repetición por tamaño y alineación espacial.
- Resultado de clasificación disponible desde `RecognitionResult`.
- Pruebas para pads delineados repetidos y alineados.
- Documentación técnica y ADR-0008.

### Cambiado

- `PadDetectionEngine` consume primitivas clasificadas en lugar de interpretar directamente cada elemento.
- Las formas sin relleno requieren evidencia de repetición y alineación antes de aceptarse como pads.
- El tamaño máximo conservador de pad se amplía al 2,5 % de la dimensión de referencia para documentos PDF escalados.
- Versión actualizada a `0.6.3-dev.1`.

## [0.6.2-dev.1] - 2026-07-27

### Added
- Pad Detection Engine independiente del reconocimiento de referencias y componentes.
- Detección conservadora de pads circulares y rectangulares con eliminación de duplicados.
- Clasificación de vías por distribución de tamaños circulares.
- Detección de agujeros explícitos y candidatos circulares no rellenos.
- Agrupación de footprints basada exclusivamente en pads compatibles.
- Diagnóstico visual independiente para pads, vías, agujeros y footprints.
- Pruebas que impiden la creación de footprints con menos de dos pads.

### Changed
- Eliminada la creación prematura de componentes y referencias inferidas.
- Los límites de cada footprint se calculan únicamente desde sus pads asociados.
- La barra de análisis muestra pads y footprints en lugar de componentes sin validar.
- Versión actualizada a `0.6.2-dev.1`.


## [0.6.1-dev.1]

- Añadido Component Recognition Engine independiente del formato.
- Detección geométrica de pads, vías, referencias, footprints y componentes.
- Añadidas capas de diagnóstico visual activables desde el menú Ver.
- Añadidas pruebas deterministas del reconocimiento y documentación técnica.

## [0.6.0-dev.1] - 2026-07-27

### Added
- Independent `ViewportCamera` with reversible world/screen transformations.
- Stateless `NativeBoardRenderer` and immutable per-frame visibility model.
- True circular-arc rendering instead of drawing complete circles for arc entities.
- Adaptive world-coordinate grid and deterministic layer/entity ordering.
- Camera tests for inverse transformations, pointer-anchored zoom and pan.
- ADR-0007 documenting separation of camera, visibility, interaction and drawing.

### Changed
- `BoardViewport` now orchestrates dedicated camera and rendering services instead of containing the complete drawing engine.
- Model mode remains fully independent from WebView2 and all source-format renderers.
- Core tests target Windows so the native camera can be verified with the rest of the solution.
- Application version advanced to `0.6.0-dev.1`.

## [0.5.3-dev.1] - 2026-07-26

### Added
- Native PDF model presentation with PDF, Model and Overlay modes.
- Complete drawing support for normalized PDF vector and text elements.
- Pointer-centered zoom, pan, fit-to-document and indexed element selection.
- Spatially culled rendering through the shared `BoardDocument` index.
- Native rendering documentation and ADR-0006.

### Changed
- PDF analysis now publishes the converted `BoardDocument` to the native viewport.
- Application version advanced to `0.5.3-dev.1`.

## [0.5.2-dev.1] - 2026-07-26

### Added
- Stable `ISpatialIndex<T>` contract and advanced spatial query/result models.
- Thread-safe uniform-grid index with versioning and operational statistics.
- Rectangular, point and circular proximity queries.
- Board-domain filtering by visibility, layer, net, component and element type.
- Incremental document index updates for element insertion, movement and removal.
- Spatial-index architecture, rendering and search documentation.
- Core tests for filtering, proximity ordering, incremental mutations and statistics.

### Changed
- `BoardHitTester` and `BoardViewport` now consume the shared document index.
- Version advanced to `0.5.2-dev.1`.

## [0.5.1-dev.1] - 2026-07-26

### Added

- Contrato `IBoardDocumentConverter` para separar extracción documental y normalización interna.
- Conversor `PdfBoardDocumentConverter` con validación obligatoria del modelo resultante.
- Cargador `PdfBoardDocumentLoader` para ejecutar el flujo completo PDF → `TechnicalDocument` → `BoardDocument`.
- Páginas normalizadas mediante `BoardDocumentPage`, con desplazamiento global y capas asociadas.
- Capas independientes de texto, vectores e imágenes para cada página PDF.
- Elementos internos documentales para líneas, polilíneas, Bézier, elipses, rectángulos e imágenes raster.
- Prueba de integración que comprueba páginas, capas, metadatos, geometría, desplazamiento e índice espacial.
- Documento técnico `docs/PDF_CORE_INTEGRATION.md`.

### Changed

- `BoardDocument` conserva ahora la estructura multipágina del archivo de origen.
- El validador comprueba también límites y referencias de capas de cada página.
- La versión central del producto pasa a `0.5.1-dev.1`.

### Compatibility

- No se modifica la interfaz ni el flujo visual existente.
- Los parsers y renderizadores anteriores continúan disponibles sin cambios de contrato.

## [0.5.0-dev.1] - 2026-07-26

### Added
- Core Engine normalizado con coordenadas, metadatos y propiedades extensibles.
- Índices por identificador para capas, redes, componentes y elementos.
- Índice espacial con alta, baja, actualización, limpieza y consultas.
- Entidades de arco, texto y taladro.
- Validación integral mediante `BoardDocumentValidator`.
- Documentación técnica `docs/CORE_ENGINE.md`.

### Changed
- `BoardDocument` mantiene compatibilidad con parsers existentes y agrega consultas espaciales.
- Capas, nets, componentes y elementos admiten metadatos propios.


## 0.4.0-dev.1 - Infraestructura candidata a validación

### Añadido
- Proyectos independientes `BoardView.Contracts`, `BoardView.Configuration`, `BoardView.Application` y `BoardView.Plugin.Abstractions`.
- Contratos para rutas de aplicación, reloj del sistema y resultados de operaciones.
- Coordinador de arranque para preparar directorios y descubrir plugins.
- Contrato público de plugins con metadatos y contexto de inicialización.
- Pruebas de infraestructura sin frameworks auxiliares adicionales.
- Documentación de arquitectura, compilación, estándares y decisiones ADR.

### Cambiado
- La composición de la aplicación utiliza el proveedor centralizado de rutas y el coordinador de arranque.
- La solución queda separada por responsabilidades sin modificar la interfaz existente.
- La identidad de desarrollo se actualiza a `0.4.0-dev.1`.

### Retirado
- Scripts personalizados de restauración, compilación, pruebas y verificación.
- Dependencia de comandos auxiliares para abrir o validar la solución.

### Compatibilidad
- Se conservan el visor PDF, la extracción geométrica, los parsers PCB existentes y el motor de renderizado de BoardView 0.3.0.

### Validación pendiente
- Compilación completa en Windows con .NET SDK 10 y WPF.
- Ejecución de las pruebas incluidas.
- Prueba funcional del PDF vectorial utilizado en BoardView 0.3.0.

## 0.3.0 - Extracción geométrica PDF

- Corrige la enumeración de `PdfPath` y sus subrutas en PdfPig.
- Reconstruye segmentos lineales usando sus extremos reales.
- Conserva líneas, polilíneas, rectángulos y curvas Bézier.
- Detecta contornos circulares construidos por cuatro curvas Bézier.
- Publica métricas geométricas por página sin modificar la interfaz.

## 0.2.10.1 - Estabilización del PDF Engine

- Añadida inspección preliminar de documentos PDF.
- Clasificación de PDF estándar, técnico, rasterizado, AcroForm, XFA, protegido y dañado.
- Los documentos XFA dejan de mostrarse como la página incorrecta `Please wait`.
- Panel informativo para documentos incompatibles.
- Acción para abrir el archivo con la aplicación predeterminada del sistema.
- Se conserva el visor y el análisis técnico de PDF compatibles.
- Añadida verificación automática del comportamiento de documentos XFA.

# Registro de cambios

## 0.2.10 - Módulo B: PDF Engine, fase 2

- Se añadió extracción de caminos vectoriales desde `Page.Paths` de PdfPig.
- Los segmentos rectos se convierten a `LineGraphic`.
- Los caminos compuestos se convierten a `PolylineGraphic`.
- Los rectángulos alineados se reconocen como `RectangleGraphic`.
- Se añadió `BezierGraphic` para conservar curvas cúbicas y sus puntos de control.
- Se conservan grosor, indicadores de trazo/relleno y colores en metadatos.
- El índice informa por separado textos, vectores y objetos totales.
- Se añadió una verificación del nuevo objeto Bézier al proyecto de pruebas.
- La interfaz visual y el visor PDF integrado permanecen sin cambios.

## 0.2.10 - Módulo B: PDF Engine, fase 2

- Se añadió extracción de caminos vectoriales desde `Page.Paths` de PdfPig.
- Los segmentos rectos se convierten a `LineGraphic`.
- Los caminos compuestos se convierten a `PolylineGraphic`.
- Los rectángulos alineados se reconocen como `RectangleGraphic`.
- Se añadió `BezierGraphic` para conservar curvas cúbicas y sus puntos de control.
- Se conservan grosor, indicadores de trazo/relleno y colores en metadatos.
- El índice informa por separado textos, vectores y objetos totales.
- Se añadió una verificación del nuevo objeto Bézier al proyecto de pruebas.
- La interfaz visual y el visor PDF integrado permanecen sin cambios.

# Changelog

## 0.2.9 - Módulo B: PDF Engine, fase 1

- Se añadió `PdfTechnicalDocumentParser`.
- Las páginas PDF se convierten a `TechnicalDocument`.
- Las palabras posicionadas se convierten a `TextGraphic`.
- Todas las coordenadas PDF se normalizan a milímetros.
- Se registran metadatos de página, operaciones y objetos.
- El análisis PDF se ejecuta en paralelo con el índice de búsqueda.
- La interfaz informa la cantidad de objetos técnicos normalizados.
- Se actualizó la versión visible y el registro de inicio a 0.2.9.

## 0.2.8 - Módulo A: Core documental

- Modelo común `TechnicalDocument` y `DocumentPage`.
- Primitivas gráficas independientes de WPF.
- Transformaciones afines y conversión de unidades.
- Metadatos extensibles.
- Contratos para parsers, renderizadores y búsquedas.
- Proyecto de verificaciones del núcleo sin dependencias externas.
- La interfaz y el visor PDF permanecen sin cambios.

## 0.2.7

- Se adopta `BoardView-0.2.6(1).zip` como base oficial.
- Se incorpora `PdfPig 0.1.14` en `BoardView.Formats`.
- Se añade `IPdfDocumentIndexer`.
- Se añaden los modelos `PdfDocumentIndex`, `PdfPage` y `PdfWord`.
- El PDF se analiza en segundo plano para evitar bloquear la interfaz.
- Se muestran página y cantidad de palabras indexadas.
- Se añade búsqueda textual sobre el documento técnico.
- Se conserva WebView2 como representación visual fiel durante esta etapa.
- Se eliminan de la entrega `.vs`, `bin`, `obj` y datos temporales de WebView2.

## 0.3.0

- Consolidación del Core documental y de coordenadas.
- Índice espacial común para selección y análisis.
- Motor de búsqueda por referencia, valor, red, capa, elemento y coordenadas.
- Estado de viewport y hit testing para el renderizador propio.
- Parsers funcionales para Gerber, Excellon, KiCad PCB, Eagle XML, IPC-2581, ODB++ ZIP y PCB legado.
- Herramientas de medición, capas, seguimiento de nets y anotaciones.

## [0.9.1-dev.1] - 2026-07-27

### Added
- Repair Workspace con visualización simultánea de placa y esquemático PDF.
- Búsqueda de referencias en ambos documentos y navegación a la página encontrada.
- Notas persistentes con estado de reparación, referencia y páginas asociadas.
- Estados: pendiente, revisar, sospechoso, comprobado, reemplazado y resuelto.
- Guardado y reapertura de proyectos `.bvrepair` sin modificar los PDFs originales.
- Historial interno de búsquedas, navegación, apertura de documentos y anotaciones.
- Navegación del visor PDF a una página específica mediante el workspace.

### Compatibility
- PDF Engine, Geometry Kernel, Semantic Kernel y Recognition Engine permanecen sin cambios.
