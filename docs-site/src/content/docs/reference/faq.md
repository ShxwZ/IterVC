---
title: Frequently asked questions
description: Answers to common IterVC setup and usage questions.
---

## Does IterVC mute the selected application?

No. It captures a copy of the application's output. The application continues playing through its normal speakers or headphones.

## Do I need VB-Cable?

Not for every workflow. IterVC can send its final mix directly to speakers, headphones, or another playback device. You do need a virtual cable when another application must receive that mix as a microphone or recording input. [VB-Audio VB-CABLE](https://vb-audio.com/Cable/) is the recommended and primarily tested option, but compatible alternatives may work.

## Which cable endpoint goes where?

When using VB-CABLE, IterVC sends to **CABLE Input**. Discord, OBS, VRChat, or another receiver records from **CABLE Output**. For direct playback instead, select your speakers or headphones as the IterVC destination.

## Can I route applications without a microphone?

Yes. Disable microphone capture and keep application routing active.

## Can I route only the microphone?

Yes, provided a playback destination is selected and routing is active. Leave applications unselected or mute the application bus.

## Does IterVC install updates automatically?

No. It can check GitHub Releases and open the release page, but it does not automatically download or install an update.

## Will routing continue after I close the window?

It depends on the selected close behavior. **Minimize to tray** keeps the process running; **Exit** stops it.

## Why is there a small delay?

Software capture and mixing require buffering, conversion, processing, and rendering. A small delay is normal and is most noticeable when monitoring your own microphone.

## Is Linux supported?

Not by the current master audio engine. Avalonia is cross-platform, but process capture, device handling, startup integration, tray behavior, and global hotkeys currently contain Windows-specific implementations.
