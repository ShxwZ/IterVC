---
title: Update checks
description: Control automatic and manual checks against GitHub Releases.
---

IterVC can query the latest published release from the official GitHub repository.

## Consent

On first use, IterVC asks whether it may perform automatic update checks. The preference is stored and can be changed later in **Settings**.

## Behavior

- A successful automatic result is cached for 24 hours.
- Manual checks bypass the cache.
- Offline or GitHub API failures do not prevent the application from starting.
- IterVC only notifies and opens the release page; it does not automatically download or install an update.
- Dismissing one release suppresses that specific notification, but a later release can notify again.

## Install an update

Open the release page, exit IterVC completely from the tray, and follow the release packaging instructions. User settings under `%AppData%\\IterVC` are separate from the application files.
