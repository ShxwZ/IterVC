---
title: Arquitectura
description: Conozca los proyectos, servicios, modelos de vista y límites específicos de plataforma de IterVC.
---

## IterVC.Core

Contiene los modelos compartidos, interfaces, ajustes, contratos de localización, utilidades de resultados, instantáneas de nivel de audio y cálculos de audio. Debe evitar la interfaz de usuario y la integración directa con la plataforma.

## IterVC.Audio

Contiene el motor de audio de Windows y la implementación de ajustes persistentes, entre otros componentes:

- Enumeración de dispositivos y sesiones.
- Captura WASAPI `process-loopback`.
- Detección del formato de reproducción de los procesos.
- Gestión del audio de las aplicaciones.
- Captura y coordinación del micrófono.
- Proveedores de muestras para la puerta de ruido y los medidores de nivel.
- Búferes en tiempo real, política de latencia, mezcla descendente y protección de salida.
- Enrutamiento final y monitorización.
- Envío por OSC.

## IterVC.Desktop

Contiene la aplicación Avalonia, la inyección de dependencias, los servicios de interfaz, los modelos de vista, las vistas, la comprobación de actualizaciones, el comportamiento de la bandeja, el registro de inicio con Windows, los atajos globales, el comportamiento de instancia única, los diagnósticos y el proceso en segundo plano de OSC.

## Composición de modelos de vista

`MainViewModel` compone modelos de vista especializados para:

- Enrutamiento de audio.
- Aplicaciones.
- Micrófono.
- Puerta de ruido.
- Chatbox OSC.
- Ajustes, idioma, atajos, actualizaciones, bandeja, inicio y diagnósticos.

## Flujo de datos

Los cambios de la interfaz actualizan los modelos de vista, que llaman a interfaces implementadas por los servicios de audio o de escritorio. Las decisiones persistentes del usuario se escriben mediante `ISettingsService`. Los servicios de audio publican el estado de nivel que consumen los medidores compactos de la interfaz.
