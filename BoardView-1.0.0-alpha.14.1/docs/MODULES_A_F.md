# BoardView 0.3.0 — módulos A–F

- **A Core:** modelo normalizado, propiedades, coordenadas, índice espacial y contratos.
- **B PDF Engine:** inspección, texto, geometría vectorial, imágenes y metadatos por página.
- **C Rendering:** renderizador WPF propio, estado de viewport y selección espacial.
- **D Search & Analysis:** búsqueda por referencia, valor, net, capa, elemento y coordenada.
- **E PCB Parsers:** Gerber RS-274X, Excellon, KiCad PCB, Eagle XML, IPC-2581 XML, ODB++ ZIP y PCB legado.
- **F Tools:** medición, visibilidad de capas, seguimiento de nets y anotaciones.

Los parsers producen `BoardDocument`; el renderizador, buscador y herramientas nunca interpretan archivos fuente.
