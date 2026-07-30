# Módulo A — Core documental

## Objetivo

Definir el modelo común que recibirán todos los parsers y consumirán los motores de búsqueda y renderizado.
El módulo no depende de WPF, WebView2, PdfPig ni de ningún formato de archivo concreto.

## Modelo

- `TechnicalDocument`: raíz de un documento técnico.
- `DocumentPage`: página o superficie lógica.
- `GraphicObject`: base de las primitivas importadas.
- `DocumentMetadata`: extensión de datos sin modificar contratos públicos.
- `Matrix2D` y `UnitConverter`: transformaciones y normalización de unidades.

## Compatibilidad

El modelo PCB existente (`BoardDocument`) se conserva intacto. En una fase posterior se adaptará al modelo común
mediante conversores, evitando romper el viewport actual.

## Verificación

`tests/BoardView.Core.Tests` es un ejecutable sin paquetes externos. Compilarlo y ejecutarlo valida límites,
unidades, transformaciones y reglas básicas del documento.
