# Compatibilidad PDF — v0.3.0

Antes de visualizar o indexar un archivo, `PdfDocumentInspector` realiza una inspección estructural y lo clasifica como PDF estándar, técnico, rasterizado, AcroForm, XFA, protegido o dañado.

## Flujo

1. Verificación de cabecera PDF.
2. Detección de cifrado, AcroForm y XFA.
3. Apertura controlada con PdfPig cuando el documento es compatible.
4. Conteo preliminar de páginas, palabras y caminos vectoriales.
5. Selección del visor o del panel de incompatibilidad.

Los documentos XFA no se presentan como una página válida con el mensaje `Please wait`. BoardView muestra una explicación y permite abrir el archivo con la aplicación predeterminada del sistema.

Los PDF estándar y técnicos conservan el comportamiento de la v0.2.10: WebView2 ofrece la representación visual y PdfPig construye el índice y el modelo técnico.
