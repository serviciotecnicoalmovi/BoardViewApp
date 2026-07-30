# Compilación y validación

## Entorno admitido

BoardView es una aplicación WPF x64 dirigida a .NET 10. La compilación oficial se realiza en Windows mediante Visual Studio o `dotnet`/MSBuild.

## Procedimiento obligatorio

1. Restaurar la solución.
2. Recompilar `BoardView.sln` en `Debug|x64`.
3. Ejecutar todas las pruebas.
4. Iniciar `BoardView.App`.
5. Abrir el PDF vectorial de referencia.
6. Verificar visualización, búsqueda y contadores de texto, vectores y objetos.
7. Registrar errores completos antes de modificar el código.

## Comandos estándar

```cmd
dotnet restore BoardView.sln
dotnet build BoardView.sln --configuration Debug --property:Platform=x64
dotnet test BoardView.sln --configuration Debug --property:Platform=x64 --no-build
```

No se mantienen scripts de compilación propios. Esta decisión evita capas innecesarias sobre las herramientas oficiales.
