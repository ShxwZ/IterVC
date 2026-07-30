---
title: Applications missing
description: Make applications appear in the process audio list.
---

## Start playback first

Many Windows applications create an audio session only after they produce sound. Start a song, video, game, or test sound before refreshing the list.

## Refresh manually

Press **Refresh applications** after opening, restarting, or changing the output device of an application.

## Check the Windows output endpoint

An application routed by Windows to another speaker, headset, monitor, or virtual device may not be associated with the reference output you selected in IterVC. Check **Settings > System > Sound > Volume mixer** in Windows and compare endpoints.

## Process name persistence

IterVC remembers selections by process name. If an application launches audio in a separate helper process, select the process that actually shows meter activity. Browser tabs and multi-process applications may share or change their audio process.

## Protected or unusual audio paths

Some protected media, sandboxed applications, exclusive-mode streams, or nonstandard drivers may not expose a compatible process-loopback session. Include logs and the exact application name when reporting a reproducible case.
