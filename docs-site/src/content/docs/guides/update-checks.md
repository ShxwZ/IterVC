---
title: Update checks
description: Check and install verified portable releases from inside IterVC.
---

IterVC can query the latest published release from the official GitHub repository.

## Consent and control

On first use, IterVC asks whether it may perform automatic update checks. The preference is stored and can be changed in **Settings**. **Installation is never automatic**: the user must press **Install update** after a newer release is offered.

## Behavior

- Successful automatic checks are cached for 24 hours; manual checks bypass the cache.
- Offline and GitHub API failures do not prevent IterVC from starting.
- **View release** remains available for downloading an update manually.
- Dismissing one release suppresses that notification without blocking newer releases.
- Download and installation run only when the user explicitly chooses **Install update**.

## Portable installation

The updater downloads the official `IterVC-Windows.zip` and checks its SHA-256 against the hash published for that exact asset by GitHub Releases. The ZIP must contain only `IterVC.exe`. Once the new EXE has been verified and staged alongside the running portable EXE, a separate updater process waits until IterVC exits, replaces the EXE and restarts IterVC.

The previous executable is kept as `IterVC.previous-*.exe` in the same folder for recovery. User settings in `%AppData%\\IterVC` are preserved.

This operation needs **write access to the folder where IterVC.exe lives** and a filesystem that supports atomic file replacement. Renamed executables and protected installation directories are not supported. On failure, use **View release** to install manually; the updater never silently installs a different build. Errors occurring after IterVC exits are written to `%AppData%\\IterVC\\last-update-error.txt`.

The update mechanism does not run for unsigned or unverified custom ZIPs; the official GitHub API must include a SHA-256 digest for the portable release asset.
