# Contributing to IterVC

Thank you for helping improve IterVC. Keep contributions focused, tested, and easy to review. An approved issue allows implementation to begin, but does not guarantee that the resulting pull request will be merged.

## Contribution workflow

1. **Discuss the change first.** Open a GitHub issue for a feature or significant change. Describe the problem, expected behavior, and user impact, then wait for approval before implementing it. Bug reports should include reproduction steps, expected behavior, actual behavior, and relevant environment details.
2. **Start from the latest `master`.** Create a dedicated branch; never work directly on `master`.
3. **Implement one focused change.** Keep unrelated cleanup out of the pull request.
4. **Add or update tests.** Every feature and bug fix must include appropriate automated coverage.
5. **Build and test locally.** All tests must pass before submission.
6. **Self-review the complete diff.** Check behavior, readability, tests, UI consistency, localization, and accidental files before opening the pull request.
7. **Open a pull request.** Link the issue and explain what changed, why, and how it was verified.
8. **Wait for review.** Another person must review the pull request before it can be merged. Address feedback without force-pushing or hiding relevant changes.

## Branch names

Use lowercase kebab-case with the appropriate prefix:

| Change | Prefix | Example |
| --- | --- | --- |
| New functionality | `feature/` | `feature/microphone-noise-gate` |
| Bug fix | `fix/` | `fix/audio-preview-freeze` |
| Refactoring | `refactor/` | `refactor/settings-panels` |
| Documentation | `docs/` | `docs/contribution-guide` |
| Maintenance | `chore/` | `chore/update-dependencies` |

## Build and test

From the repository root:

```bash
dotnet restore
dotnet build IterVC.sln
dotnet test IterVC.sln
```

- Do not submit a pull request with failing tests or compilation errors.
- Add tests for new behavior and regression tests for bug fixes.
- Do not weaken, remove, or rewrite existing tests merely to make a change pass.
- When manual verification is relevant, include clear reproduction and validation steps in the pull request.

## Code and architecture

- Keep the application compatible with Windows 10 Build 19041 or later and x64.
- Keep shared models, settings, and interfaces in `IterVC.Core`.
- Keep capture, mixing, routing, and other audio logic in `IterVC.Audio`.
- Keep Avalonia UI code in `IterVC.Desktop`; ViewModels must not contain UI-specific code.
- Use English for code comments.
- Avoid public API changes unless they have been discussed and approved.
- Do not include secrets, local settings, build output, test results, IDE files, or unrelated generated artifacts.

## UI contributions

UI changes must feel like part of the existing application:

- Reuse the established colors, spacing, typography, controls, and interaction patterns.
- Keep layouts usable at the supported window sizes; avoid designs that require unnecessarily enlarging the window.
- Add every user-facing string through the existing localization system and update all supported languages.
- Verify focus, selection, disabled, hover, and scrolling behavior where applicable.
- Include screenshots or a short recording for visible changes when this helps reviewers compare the result.

## Pull requests and reviewability

Prefer small pull requests that deliver one coherent outcome. Large or cross-cutting changes require more validation and will usually take longer to review and approve. Split them into independently useful, reviewable changes whenever possible, while keeping each change's implementation, tests, and documentation together.

A good pull request description includes:

- the linked issue;
- the problem and chosen solution;
- notable technical or product decisions;
- exact build and test results;
- manual verification steps, when applicable;
- screenshots or recordings for UI changes;
- known limitations or intentionally deferred work.

## Use of AI tools

AI-assisted development is allowed, but it must remain under active human supervision. The contributor is fully accountable for every submitted line and must understand, verify, test, and self-review generated changes. Do not submit AI output that you cannot explain or maintain.

## Pull request checklist

- [ ] The issue was approved before implementation, when required.
- [ ] The branch follows the naming convention and is based on the latest `master`.
- [ ] The pull request contains one focused, reviewable change.
- [ ] I reviewed the complete diff before submission.
- [ ] The solution builds successfully.
- [ ] All tests pass, and new behavior has appropriate tests.
- [ ] UI changes match the existing design and remain usable at supported sizes.
- [ ] User-facing text is localized in every supported language.
- [ ] Windows 10 compatibility and architecture boundaries are preserved.
- [ ] No secrets, local configuration, build output, or unrelated generated files are included.
- [ ] The pull request links its issue and explains verification clearly.
- [ ] Another person will review the pull request before merge.
