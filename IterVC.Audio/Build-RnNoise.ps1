[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$nativeProject = Join-Path $projectRoot 'native\iter_vc_rnnoise'
$manifest = Join-Path $nativeProject 'Cargo.toml'
$target = 'x86_64-pc-windows-msvc'
$nativeBinary = Join-Path $nativeProject "target\$target\release\iter_vc_rnnoise.dll"

if (-not (Get-Command cargo -ErrorAction SilentlyContinue) -or
    -not (Get-Command rustup -ErrorAction SilentlyContinue)) {
    throw 'Rust/Cargo is required to build the RNNoise backend. Install Rust with the MSVC target.'
}

rustup target add $target
if ($LASTEXITCODE -ne 0) { throw 'Could not install Rust Windows MSVC target.' }

cargo build --manifest-path $manifest --release --target $target
if ($LASTEXITCODE -ne 0) { throw 'RNNoise native build failed.' }

if (-not (Test-Path -LiteralPath $nativeBinary -PathType Leaf)) {
    throw "RNNoise native library missing: $nativeBinary"
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Copy-Item -LiteralPath $nativeBinary -Destination (Join-Path $OutputDirectory 'iter_vc_rnnoise.dll') -Force
Write-Host "RNNoise native library available in $OutputDirectory"
