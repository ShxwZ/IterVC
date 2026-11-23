---
title: Logs and diagnostics
description: Find settings and rolling log files before reporting a bug.
---

IterVC writes persistent data below:

```text
%AppData%\IterVC\
```

## Open logs from the application

Open **Settings** and select **Open logs folder** under Diagnostics.

The log directory is:

```text
%AppData%\IterVC\Logs\
```

IterVC registers global exception handlers and flushes critical errors to the rolling file logger when possible.

## Settings file

The persisted settings file is:

```text
%AppData%\IterVC\settings.json
```

Do not publish this file without reviewing it. It can contain device identifiers, process names, shortcut choices, update state, and UI preferences.

## Useful bug-report information

Include:

- IterVC version shown in the footer or release filename.
- Windows version and build.
- Audio device names and driver software.
- Virtual cable product and version.
- The application being captured.
- Exact reproduction steps.
- The relevant log file from immediately after reproducing the problem.

Avoid editing the log before attaching it unless you are removing private information.
