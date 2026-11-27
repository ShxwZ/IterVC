---
title: OSC problems
description: Diagnose missing, stale, truncated, or malformed VRChat Chatbox messages.
---

## Nothing appears in VRChat

- Enable OSC inside VRChat.
- Enable the OSC Chatbox option in IterVC.
- Start media playback in AIMP or an application that exposes Windows media-session metadata.
- For players other than AIMP, confirm the media title is visible to Windows media controls. AIMP can instead provide metadata through its built-in Remote API.
- Check that local firewall or security software is not blocking loopback OSC traffic.

When no supported player provides a track, IterVC displays the stopped indicator. A paused track follows the Chatbox pause-visibility setting.

## Title or time is wrong

IterVC uses metadata from Windows media sessions or AIMP's Remote API. If AIMP is not detected, check that it is running in the same Windows user session and at the same privilege level as IterVC. Close other playing media apps to check the fallback. The media application controls the title, artist formatting, and timing accuracy.

## Multiline templates do not display correctly

IterVC converts CRLF and LF line breaks to the vertical-tab separator used by its VRChat OSC Chatbox library. Keep the template small and test with the exact current VRChat client.

## Message is truncated or wraps badly

Shorten fixed text and decorations. Leave room for long values produced by `{title}` and `{time}`.

## OSC worker stops

Open the log folder and search the latest file for `OSC chatbox worker stopped unexpectedly` or `Error enviando mensaje OSC al chatbox`.
