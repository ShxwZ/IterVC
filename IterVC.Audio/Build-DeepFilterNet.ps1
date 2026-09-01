[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$manifest = Join-Path $projectRoot 'native\iter_vc_deep_filter\Cargo.toml'
$target = 'x86_64-pc-windows-msvc'
$nativeName = 'iter_vc_deep_filter.dll'
$nativeProject = Join-Path $projectRoot 'native\iter_vc_deep_filter'
$nativeBinary = Join-Path $nativeProject "target\$target\release\$nativeName"

if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    throw @"
Rust/Cargo is required to build the DeepFilterNet3 native backend.
Install Rust from https://rustup.rs/ and run the build again.
"@
}

if (-not (Get-Command rustup -ErrorAction SilentlyContinue)) {
    throw 'rustup is required so the pinned Windows MSVC target can be installed.'
}

rustup target add $target

if (-not (Test-Path $nativeBinary)) {
    Write-Host "Building DeepFilterNet3 native backend..."
    cargo build --manifest-path $manifest --release --target $target
}

if (-not (Test-Path $nativeBinary)) {
    throw "DeepFilterNet3 native backend was not produced: $nativeBinary"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Copy-Item -Force $nativeBinary (Join-Path $OutputDirectory $nativeName)
Write-Host "Copied $nativeName to $OutputDirectory"
