---
title: Windows startup
description: Launch IterVC automatically with Windows and optionally keep it hidden.
---

Open **Settings** and enable the Windows startup registration option to launch IterVC when you sign in.

## Start hidden

When startup registration is enabled, **Start hidden on Windows startup** can keep the main window in the system tray after initialization. This is enabled by default in the persisted settings model.

A manual launch should still show or restore the main window. IterVC also uses a single-instance coordinator, so launching it again should notify the existing primary process instead of starting a second audio engine.

## Portable-build warning

Startup registration points Windows to the current executable location. If you use a portable release:

1. Move IterVC to its permanent folder first.
2. Enable startup registration afterward.
3. Disable and re-enable the option if you later move the executable.

IterVC records whether the portable startup notice has been acknowledged.
