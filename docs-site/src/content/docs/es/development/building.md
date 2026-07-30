---
title: Compilar desde el código fuente
description: Restaure, compile, pruebe y ejecute la solución IterVC localmente.
---

## Requisitos

- Windows 10 compilación 19041 o posterior, o Windows 11.
- Entorno x64.
- SDK de .NET 8.
- Git.
- El hardware de audio resulta útil para las pruebas manuales, pero las pruebas unitarias deterministas no deberían depender de él.

## Comandos

Desde la raíz del repositorio:

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project IterVC.Desktop
```

El proyecto de escritorio tiene como destino `net8.0-windows10.0.19041.0`, usa x64 y compila el ejecutable de Windows `IterVC`.

## Dependencias principales

- Avalonia UI 11.1.3.
- CommunityToolkit.Mvvm 8.3.2.
- Microsoft.Extensions.Hosting y logging 8.x.
- NAudio 2.2.1.
- VRCOscLib 1.6.0.

Cuando esta página quede desactualizada, use las versiones de los archivos de proyecto como fuente de referencia.

## Etiqueta de versión local

Las compilaciones locales usan la versión informativa `local-development`, salvo que la compilación proporcione `ReleaseVersion`. Los flujos de publicación pueden inyectar la versión publicada.
