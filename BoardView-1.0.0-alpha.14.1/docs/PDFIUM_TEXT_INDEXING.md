# PDFium Text Indexing

## Responsabilidad

PDFium se utiliza únicamente para extraer texto y coordenadas de cada página. No sustituye al visor WebView2 ni al parser geométrico PdfPig.

## Flujo

```text
PDF -> PDFium -> caracteres Unicode + cajas -> palabras -> referencias -> índice por página
```

## Aislamiento

- El acceso se serializa porque PDFium mantiene estado global.
- Cada página y página de texto se libera en bloques `finally`.
- Los errores de una página no cancelan el documento completo.
- Si PDFium no puede abrir el archivo, la aplicación conserva el visor y devuelve un índice vacío con advertencias.

## Arquitectura

- WebView2: visualización.
- PDFium: texto y coordenadas para búsqueda.
- PdfPig: geometría técnica existente.
