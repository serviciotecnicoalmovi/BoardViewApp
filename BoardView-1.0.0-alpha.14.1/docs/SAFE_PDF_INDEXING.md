# Indexación PDF segura

## Propósito

La búsqueda del Repair Workspace utiliza un indexador textual separado del parser geométrico. Su fallo no impide abrir ni visualizar el documento.

## Flujo

1. Se copia el PDF a una ruta temporal.
2. En la copia se renombran, conservando su longitud, claves de anotaciones y destinos que pueden activar fallos de resolución en PdfPig.
3. PdfPig abre exclusivamente la copia temporal.
4. Cada página se indexa dentro de su propio bloque de control de errores.
5. Las páginas defectuosas se conservan como páginas vacías y producen una advertencia.
6. La copia temporal se elimina al finalizar.

## Invariantes

- El PDF original es de solo lectura.
- La visualización mediante WebView2 no depende del índice.
- Una excepción de una página no cancela las demás páginas.
- La búsqueda se habilita solo cuando existe al menos un índice cargado.
