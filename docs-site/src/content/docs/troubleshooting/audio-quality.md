---
title: Audio quality
description: Fix echo, surround artifacts, clipping, low volume, delay, and audio interruptions.
---

## Echo, reverb, or hollow sound

Process-loopback capture can receive audio after Windows spatial processing or vendor effects. On the selected playback endpoint, try disabling:

- Spatial sound.
- Surround virtualization.
- Dolby, DTS, Sonic, or headset-specific effects.
- Audio enhancements.

Then restart playback and refresh the application list.

## Low volume

1. Increase the source application's own volume.
2. Check the Windows session volume for that application.
3. Increase IterVC application gain gradually.
4. Check the receiving application's input gain.

Application gain supports up to 300%, but large boosts can amplify noise and cause clipping.

## Distortion or clipping

Reduce application gain, microphone boost, or the source volume. Multiple loud applications can sum above full scale even when each one is individually clean. IterVC includes output-protection and diagnostic stages, but avoiding excessive gain produces the best result.

## Delay

A small amount of latency is expected because IterVC captures, buffers, converts, mixes, and renders audio in real time. Monitoring a microphone through software makes this delay more noticeable.

- Disable microphone monitoring when it is not needed.
- Avoid running multiple unnecessary audio-enhancement layers.
- Use stable, current audio drivers.
- Check logs for buffer underruns, overruns, or processing delays.

## Crackles or interruptions

- Close applications that heavily load the CPU or audio stack.
- Keep all relevant endpoints at a stable common sample rate when possible.
- Disable exclusive mode if another application repeatedly takes control of a device.
- Reconnect or restart USB audio devices.
- Reproduce the problem, then attach the logs when reporting it.
