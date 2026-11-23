---
title: Architecture
description: Understand IterVC projects, services, view models, and platform-specific boundaries.
---

## IterVC.Core

Contains shared models, interfaces, settings, localization contracts, result helpers, audio level snapshots, and audio math. It should avoid UI and direct platform integration.

## IterVC.Audio

Contains the Windows audio engine and persistent settings implementation, including:

- Device and session enumeration.
- WASAPI process-loopback capture.
- Process render-format detection.
- Application audio management.
- Microphone capture and coordination.
- Noise gate and level-meter sample providers.
- Real-time buffers, latency policy, downmixing, and output protection.
- Final routing and monitoring.
- OSC sending.

## IterVC.Desktop

Contains the Avalonia application, dependency injection, UI services, view models, views, update checks, tray behavior, Windows startup registration, global hotkeys, single-instance behavior, diagnostics, and the OSC background worker.

## View-model composition

`MainViewModel` composes focused view models for:

- Audio routing.
- Applications.
- Microphone.
- Noise gate.
- OSC Chatbox.
- Settings, language, hotkeys, updates, tray, startup, and diagnostics.

## Data flow

UI changes update view models, which call interfaces implemented by the audio or desktop services. Persistent user choices are written through `ISettingsService`. Audio services publish level state consumed by compact meters in the UI.
