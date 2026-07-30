# BoardView 1.0.0-alpha.13

Motor gráfico nativo en validación para la aplicación de escritorio BoardView.

Esta entrega parte de BoardView 0.7.1-dev.1, compilada por el usuario. Conserva la interfaz y el visor PDF, pero el modo **Modelo** utiliza exclusivamente `BoardDocument`, el índice espacial y el renderizador WPF propio.

## Indexación textual con PDFium

La versión `1.0.0-alpha.13` sustituye PdfPig por PDFium únicamente en el índice textual del Repair Workspace. PDFium extrae caracteres y coordenadas por página; WebView2 continúa mostrando los PDF y PdfPig continúa procesando la geometría técnica. La búsqueda reconstruye referencias fragmentadas como `L` + `305` + `_E`.

## Requisitos

- Windows 10 o Windows 11 de 64 bits.
- Visual Studio 2026 o una versión compatible con .NET 10 y WPF.
- Carga de trabajo **Desarrollo de escritorio con .NET**.
- .NET SDK 10.

## Compilación en Visual Studio

1. Abra `BoardView.sln`.
2. Seleccione la configuración `Debug` y la plataforma `x64`.
3. Establezca `BoardView.App` como proyecto de inicio.
4. Ejecute **Compilar > Recompilar solución**.
5. Ejecute `BoardView.App`.
6. Abra un PDF y confirme que el índice conserva palabras, vectores y objetos.
7. Pulse **Modelo** y confirme que el PDF desaparece y se muestra el render nativo.
8. Verifique zoom con la rueda, paneo y selección de entidades.
9. Ejecute los proyectos de pruebas incluidos en la solución.

## Compilación desde una terminal de desarrollador

```cmd
dotnet restore BoardView.sln
dotnet build BoardView.sln --configuration Debug --property:Platform=x64
dotnet test BoardView.sln --configuration Debug --property:Platform=x64 --no-build
```

No se incluyen scripts personalizados. La restauración, compilación y prueba utilizan directamente Visual Studio, MSBuild y `dotnet`.

## Organización

- `src/BoardView.Contracts`: contratos transversales estables.
- `src/BoardView.Configuration`: rutas y servicios de plataforma independientes de la UI.
- `src/BoardView.Application`: coordinación de casos de uso y arranque.
- `src/BoardView.Plugin.Abstractions`: API pública mínima para plugins.
- `src/BoardView.Core`: modelo interno, geometría, búsqueda y herramientas.
- `src/BoardView.Infrastructure`: configuración persistente, logging e inyección de dependencias.
- `src/BoardView.Formats`: lectores PDF y PCB existentes.
- `src/BoardView.Plugins`: descubrimiento de plugins.
- `src/BoardView.Rendering`: viewport, selección y representación visual.
- `src/BoardView.GeometryKernel`: reconstrucción topológica y primitivas.
- `src/BoardView.SemanticKernel`: significado electrónico de primitivas.
- `src/BoardView.Recognition`: clustering, footprints y componentes.
- `src/BoardView.App`: aplicación WPF y raíz de composición.
- `tests`: verificaciones del núcleo y de la infraestructura.
- `docs`: arquitectura, decisiones y normas de desarrollo.

## Estado

`1.0.0-alpha.4` no se considera estable hasta superar la compilación y las pruebas en Windows. El resultado debe registrarse antes de promover la versión a RC.


## Recognition Engine

La versión 0.8.0-dev.1 incorpora `BoardView.Recognition`. El motor agrupa pads con un índice espacial, calcula filas, columnas, pitch, simetría y rotación, clasifica familias iniciales de footprints y asocia referencias cercanas. El Geometry Inspector incluye una pestaña **Componentes** para validar los resultados.


## Footprint Template Engine

Las plantillas JSON se despliegan en `Footprints/` junto al ejecutable. Se pueden añadir o ajustar plantillas sin recompilar el motor. Cada resultado conserva score, factores y estado de aceptación.


## Workspace integrado

La ventana principal contiene de forma permanente el explorador, el visor de placa, el visor de esquemático y el inspector de reparación. Los separadores permiten redimensionar cada panel. Los comandos de abrir, buscar, anotar y guardar proyecto ya no requieren una ventana secundaria.

## Compatibilidad PDF en Repair Workspace

La versión 1.0.0-alpha.8 abre la placa y el esquemático directamente en el visor integrado sin ejecutar PdfPig durante la apertura. Esto evita excepciones con PDFs que Chromium visualiza correctamente pero contienen estructuras internas no conformes. La búsqueda textual interna permanece temporalmente deshabilitada para esos documentos.


## Búsqueda segura por referencia

La versión `1.0.0-alpha.10` sincroniza la navegación entre la placa y el esquemático. Mantiene índices independientes, agrupa los resultados por documento, navega ambos visores a la misma referencia cuando está disponible y muestra una ficha de localización cruzada. El indexador seguro de alpha.9 se conserva sin cambios.

Flujo de validación:

1. Abra la placa y el esquemático.
2. Espere a que la barra de estado indique la cantidad de palabras y páginas indexadas.
3. Escriba una referencia, por ejemplo `R605`, y pulse **Buscar**.
4. Seleccione una coincidencia para navegar a la página correspondiente.
