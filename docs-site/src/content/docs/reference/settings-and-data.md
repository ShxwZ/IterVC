---
title: Settings and data
description: Understand what IterVC stores and where it is located.
---

IterVC stores its user settings as formatted JSON in `%AppData%\\IterVC\\settings.json`.

The current settings schema includes:

- Reference output, routing destination, and microphone device identifiers.
- Microphone enabled state, boost, monitoring, and noise-gate values.
- Shared application gain.
- Included process names.
- Interface language.
- OSC template and enabled state.
- Update-check consent, cache, and dismissed release.
- Global hotkey assignments.
- Close and minimize-to-tray behavior.
- Windows startup preferences.
- First-run notification acknowledgements.

## Safe reset

To reset all preferences:

1. Exit IterVC completely from the tray.
2. Rename `settings.json` to `settings.backup.json`.
3. Start IterVC.

IterVC creates default settings when the file does not exist or cannot be loaded. Keep the backup until you confirm the reset solved the issue.

## Write behavior

Settings updates are serialized through an application lock. IterVC writes a temporary file, replaces the existing settings file, and removes the temporary file. This reduces the chance of leaving partially written JSON.
