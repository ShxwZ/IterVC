---
title: Build from source
description: Restore, build, test, and run the IterVC solution locally.
---

## Requirements

- Windows 10 build 19041 or later, or Windows 11.
- x64 environment.
- .NET 8 SDK.
- Git.
- Audio hardware is useful for manual testing but should not be required by deterministic unit tests.

## Commands

From the repository root:

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project IterVC.Desktop
```

The desktop project targets `net8.0-windows10.0.19041.0`, uses x64, and builds the `IterVC` Windows executable.

## Main dependencies

- Avalonia UI 11.1.3.
- CommunityToolkit.Mvvm 8.3.2.
- Microsoft.Extensions.Hosting and logging 8.x.
- NAudio 2.2.1.
- VRCOscLib 1.6.0.

Use the versions in the project files as the source of truth when this page becomes outdated.

## Local version label

Local builds use the `local-development` informational version unless the build provides `ReleaseVersion`. Release workflows can inject the published version.
