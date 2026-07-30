# ADR-0001: Arquitectura modular por proyectos

- Estado: aceptado
- Versión: 0.4.0

## Decisión

Separar contratos, configuración, aplicación, infraestructura, plugins, formatos, renderizado y UI en ensamblados independientes.

## Motivo

Permite sustituir implementaciones, probar módulos sin WPF y agregar lectores sin modificar el núcleo gráfico.

## Consecuencias

Las referencias circulares están prohibidas. Todo intercambio entre módulos debe utilizar contratos públicos documentados.
