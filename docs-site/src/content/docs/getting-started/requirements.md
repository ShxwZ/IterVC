---
title: Requirements
description: Supported Windows versions, architecture, and optional virtual-audio requirements.
---

## Operating system

IterVC currently targets:

- Windows 10 build 19041 / version 2004 or later.
- Windows 11.
- 64-bit Windows only.

The minimum Windows version is required by the native WASAPI process-loopback API used for per-application capture. The current native interop layer targets x64.

## Playback destination

IterVC needs a Windows playback endpoint for the final mix. This can be:

- A virtual audio cable.
- Speakers.
- Headphones or a headset.
- Another compatible Windows playback device.

A virtual cable is required only when the mix must appear as an input or microphone in another application. IterVC was developed and tested primarily with **VB-Audio VB-CABLE**, available from the [official VB-Audio download page](https://vb-audio.com/Cable/).

After VB-CABLE is installed, Windows normally exposes two sides:

- **CABLE Input**: the playback endpoint IterVC sends audio to.
- **CABLE Output**: the recording endpoint selected as a microphone in Discord, OBS, VRChat, or another receiving application.

The names can vary slightly with language, driver edition, or additional VB-CABLE products.

## Building from source

Building the project requires the .NET 8 SDK. End users downloading a self-contained release should follow the requirements stated on that release page instead of installing the SDK unnecessarily.
