---
title: OSC problems
description: Diagnose missing, stale, truncated, or malformed VRChat Chatbox messages.
---

## Nothing appears in VRChat

- Enable OSC inside VRChat.
- Enable the OSC Chatbox option in IterVC.
- Start media playback in an application that exposes Windows media-session metadata.
- Confirm the media title is visible to Windows media controls.
- Check that local firewall or security software is not blocking loopback OSC traffic.

IterVC currently sends only when it receives a non-empty media title.

## Title or time is wrong

IterVC depends on metadata provided by the active Windows media session. The media application controls the title, artist formatting, and timing accuracy.

## Multiline templates do not display correctly

IterVC converts CRLF and LF line breaks to the vertical-tab separator used by its VRChat OSC Chatbox library. Keep the template small and test with the exact current VRChat client.

## Message is truncated or wraps badly

Shorten fixed text and decorations. Leave room for long values produced by `{title}` and `{time}`.

## OSC worker stops

Open the log folder and search the latest file for `OSC chatbox worker stopped unexpectedly` or `Error enviando mensaje OSC al chatbox`.
