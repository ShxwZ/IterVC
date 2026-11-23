---
title: What is IterVC?
description: Understand the purpose, supported use cases, and main limitations of IterVC.
---

IterVC is a Windows desktop application built with .NET 8 and Avalonia UI. It is designed to make a common audio-routing workflow simpler:

1. Choose one or more running applications.
2. Optionally add a physical microphone.
3. Adjust application and microphone levels.
4. Send the mixed result to a selected Windows playback destination.
5. When another application must receive that mix as a microphone, use a virtual cable and select its recording endpoint in the receiving application.

## Typical uses

- Play music or sound effects through a voice-chat microphone input by using a virtual cable.
- Mix selected game, browser, or media-player audio with your voice.
- Feed a controlled application mix into Discord, OBS Studio, TeamSpeak, or VRChat.
- Send the final mix directly to speakers or headphones when a microphone-style endpoint is not needed.
- Show media information in the VRChat Chatbox using OSC.

## What IterVC does not do

IterVC is not a full digital audio workstation and does not replace every feature in advanced mixers such as VoiceMeeter. The current application focuses on selected-process capture, microphone mixing, basic gain controls, noise gating, monitoring, and routing to a Windows playback device.

IterVC does not install an audio driver. A virtual cable is only required for workflows where another application must see the IterVC mix as an input device. The recommended and primarily tested option is [VB-Audio VB-CABLE](https://vb-audio.com/Cable/).

## Non-destructive capture

Selected applications still play through their existing Windows output. IterVC captures a copy of their process output and mixes that copy independently. This means enabling an application in IterVC should not redirect or silence the application's normal playback.
