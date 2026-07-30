# Canal de procesamiento PDF

## Etapa actual

```text
Archivo PDF
    ├── WebView2 -> representación visual fiel
    └── PdfDocumentIndexer -> páginas, palabras y coordenadas
```

El índice técnico es independiente de WPF. Esto permite construir búsqueda, selección y análisis sin acoplar el dominio al visor de Microsoft Edge.

## Próximas etapas

1. Extracción de operaciones gráficas y rutas vectoriales.
2. Conversión a primitivas geométricas internas.
3. Selección de texto y geometría.
4. Clasificación de símbolos, referencias y posibles conexiones.
5. Enlace entre la representación visual y el modelo técnico.

Un PDF convencional contiene instrucciones gráficas, no componentes electrónicos ni redes explícitas. La inferencia semántica se implementará después de conservar fielmente la geometría original.
