# IterVC

## Stack

- .NET 8
- Avalonia
- MVVM Toolkit
- NAudio

## Rules

- Never change tests unless explicitly requested.
- Never change public APIs without asking.
- Keep code compatible with Windows 10.
- Comments in English.
- UI strings use localization.

## Architecture

- ViewModels contain no UI code.
- Audio logic belongs in RadioOSC.Audio.
- UI belongs in Avalonia.

## Git workflow

For every new implementation, bug fix, refactor, or independent task:

1. Inspect the current repository state with:
   - `git status`
   - `git branch --show-current`

2. Do not begin implementation directly on `master`.

3. Before creating the task branch:
   - Switch to `master`.
   - Ensure there are no uncommitted changes.
   - Run `git pull --ff-only origin master` to obtain the latest remote changes.

4. Create a new branch from the updated `master`.

5. Use one of these branch prefixes:
   - `feature/` for new functionality.
   - `fix/` for bug fixes.
   - `refactor/` for refactoring.
   - `docs/` for documentation.
   - `chore/` for maintenance.

6. Use lowercase kebab-case branch names.

7. Never:
   - Commit directly to `master`.
   - Force-push.
   - Delete branches.
   - Discard uncommitted user changes.
   - Run `git reset --hard`.
   - Merge into `master` unless explicitly requested.

8. If the working tree is not clean, stop before switching branches and explain which files contain uncommitted changes.

9. At the end of the task:
   - Run the relevant build and tests.
   - Show `git status`.
   - Summarize the changed files.
   - Do not commit or push unless explicitly requested.