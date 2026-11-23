---
title: Contributing
description: Prepare focused changes that are easy to discuss, test, and review.
---

Read the repository's current `CONTRIBUTING.md` and license before beginning work. Those files are authoritative when they differ from this overview.

## Recommended workflow

1. Check existing issues and queued work.
2. Discuss significant features or behavioral changes in an issue before implementation.
3. Start a dedicated branch from the latest `master`.
4. Keep the pull request focused on one approved problem.
5. Avoid unrelated refactors or formatting changes.
6. Add or update tests for the changed behavior.
7. Build and run the relevant tests locally.
8. Explain user impact, implementation choices, and verification in the pull request.

## Documentation changes

Update the relevant page under `docs-site/src/content/docs/` in the same pull request as a user-visible behavior change.

When a new screenshot is needed, add a `Screenshot` component reference and record it in `docs-site/IMAGE_CHECKLIST.md`. Screenshots should avoid private usernames, device serials, unrelated windows, and notification content.

## Distribution and branding

Follow the repository license and branding requirements for modified distributions. Do not present a fork as the official IterVC build.
