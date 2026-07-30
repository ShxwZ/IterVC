---
title: No audio
description: Diagnose silence using IterVC's application, microphone, and routed-output meters.
---

Use the level meters to determine where audio stops.

## No application meter activity

- Confirm the application is actively producing sound.
- Press **Refresh applications**.
- Confirm the application is selected in the left panel.
- Confirm all application audio is not muted.
- Check that the application is playing through the selected reference output device.
- Restart the application if its Windows audio session is stale.

## Application meters move, routed output does not

- Start routing from the header status control.
- Select a valid playback destination: **CABLE Input**, speakers, headphones, or another Windows playback endpoint.
- Ensure application gain is above 0%.
- Try stopping and starting routing after changing devices.

## Routed output moves, but speakers or headphones are silent

- Confirm the expected hardware playback device is selected as the destination.
- Check Windows volume, device mute, and the hardware volume control.
- Confirm another application has not taken exclusive control of the endpoint.
- Avoid routing the microphone to nearby speakers because this can create feedback.

## Routed output moves, but a receiving application hears nothing through VB-CABLE

- Select the recording side of the cable, normally **CABLE Output**, as the microphone in the receiving application.
- Confirm the receiving application has permission to use microphones in Windows privacy settings.
- Check its own input gain, mute, voice-activation threshold, and selected device.
- Temporarily disable exclusive-mode or enhancement features on the virtual cable endpoints if another application is locking them.
- Reinstall VB-CABLE only from the [official VB-Audio page](https://vb-audio.com/Cable/) if the endpoints are missing or damaged.

## Microphone is missing from the mix

- Select the correct physical microphone.
- Enable microphone capture from the header.
- Set microphone boost above 0.
- Temporarily disable the noise gate or lower its threshold.
- Check the microphone output meter while speaking.
