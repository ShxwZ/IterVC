---
title: Contribuir
description: Prepare cambios acotados que sean fáciles de debatir, probar y revisar.
---

Lea el archivo `CONTRIBUTING.md` actual del repositorio y la licencia antes de empezar. Si difieren de esta guía, esos archivos tienen prioridad.

## Flujo de trabajo recomendado

1. Revise las incidencias existentes y el trabajo pendiente.
2. Debata en una incidencia las funciones importantes o los cambios de comportamiento antes de implementarlos.
3. Cree una rama específica a partir del estado más reciente de `master`.
4. Mantenga el pull request centrado en un único problema aprobado.
5. Evite refactorizaciones o cambios de formato no relacionados.
6. Añada o actualice las pruebas del comportamiento modificado.
7. Compile y ejecute localmente las pruebas pertinentes.
8. Explique en el pull request el impacto para las personas usuarias, las decisiones de implementación y la verificación realizada.

## Cambios en la documentación

Actualice la página correspondiente de `docs-site/src/content/docs/` en el mismo pull request que introduzca un cambio visible para las personas usuarias.

Cuando se necesite una captura de pantalla, añada una referencia al componente `Screenshot` y regístrela en `docs-site/IMAGE_CHECKLIST.md`. Las capturas no deben incluir nombres de usuario privados, números de serie de dispositivos, ventanas no relacionadas ni contenido de notificaciones.

## Distribución y marca

Respete los requisitos de licencia y marca del repositorio en las distribuciones modificadas. No presente una bifurcación como la compilación oficial de IterVC.
