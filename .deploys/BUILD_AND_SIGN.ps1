# ============================================================
# BUILD_AND_SIGN.ps1 (IterVC)
# ============================================================

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Resolve-Path (Join-Path $ScriptDir "..\IterVC.Desktop\IterVC.Desktop.csproj") -ErrorAction SilentlyContinue

# Fallback si se ejecuta desde la raíz directamente
if (-not $ProjectFile) {
    $ProjectFile = Resolve-Path ".\IterVC.Desktop\IterVC.Desktop.csproj" -ErrorAction SilentlyContinue
}

if (-not $ProjectFile) {
    throw "No se pudo encontrar el archivo de proyecto IterVC.Desktop.csproj."
}

$Configuration = "Release"
$Runtime = "win-x64"

# === GESTIÓN SEGURA DE CONTRASEÑA ===
$CertPassword = $env:MY_GITHUB_SECRET_PASSWORD

if (-not $CertPassword) {
    Write-Host "⚠️ Ejecución local detectada (No estás en GitHub)." -ForegroundColor Yellow
    # Pide la contraseña interactivamente en tu PC para no guardarla en texto plano
    $CertPassword = Read-Host -Prompt "Introduce una contraseña para el certificado de firma"
    if (-not $CertPassword) {
        throw "La contraseña no puede estar vacía para firmar el ejecutable."
    }
}

$CertName = "Gabriel Garcia"
$SingleFile = $true
$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host " ITERVC BUILD + SIGN"
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

$ProjectDir = Split-Path $ProjectFile -Parent
$PublishDir = Join-Path $ProjectDir "publish_output"

# === GENERAR CERTIFICADO ===
Write-Host "Buscando certificado local..." -ForegroundColor Yellow
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq "CN=$CertName" } | Select-Object -First 1

if (-not $cert) {
    Write-Host "Creando certificado de firma autofirmado..." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject "CN=$CertName" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -HashAlgorithm SHA256 `
        -KeyAlgorithm RSA `
        -KeyLength 4096 `
        -NotAfter (Get-Date).AddYears(5)
}

# === EXPORTAR PFX ===
$CertFolder = Join-Path $env:USERPROFILE "CodeSigning"
New-Item -ItemType Directory -Force -Path $CertFolder | Out-Null
$PfxFile = Join-Path $CertFolder "CodeSigningCert.pfx"
$password = ConvertTo-SecureString $CertPassword -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $PfxFile -Password $password | Out-Null

# === LIMPIAR Y COMPILAR ===
if (Test-Path $PublishDir) { 
    Remove-Item $PublishDir -Recurse -Force 
}

Write-Host "Compilando y publicando aplicación con .NET..." -ForegroundColor Cyan
$PublishArgs = @("publish", $ProjectFile.Path, "-c", $Configuration, "-r", $Runtime, "--self-contained", "true", "-o", $PublishDir)
if ($SingleFile) {
    $PublishArgs += "-p:PublishSingleFile=true"
    $PublishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
}
& dotnet $PublishArgs

# === BUSCAR SIGNTOOL EN EL SISTEMA ===
$PossiblePaths = @(
    "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe",
    "C:\Program Files (x86)\Microsoft SDKs\ClickOnce\SignTool\signtool.exe",
    "C:\Program Files\Windows Kits\10\bin\*\x64\signtool.exe"
)
$signtoolPath = $null
foreach ($pattern in $PossiblePaths) {
    $resolved = Resolve-Path $pattern -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($resolved) { 
        $signtoolPath = $resolved.Path
        break 
    }
}
if (-not $signtoolPath) { 
    throw "No se pudo encontrar SignTool.exe en el equipo. Asegúrate de tener instalado el Windows SDK." 
}

# === FIRMAR EXECUTABLES ===
$exeFiles = Get-ChildItem $PublishDir -Filter *.exe -File
foreach ($exe in $exeFiles) {
    Write-Host "Firmando ejecutable: $($exe.Name)..." -ForegroundColor Cyan
    & $signtoolPath sign /f $PfxFile /p $CertPassword /fd SHA256 $exe.FullName
}

# === VERIFICAR FIRMAS ===
Write-Host "Verificando validez de las firmas..." -ForegroundColor Yellow
foreach ($exe in $exeFiles) {
    & $signtoolPath verify /pa $exe.FullName
}

Write-Host ""
Write-Host "======================================" -ForegroundColor Green
Write-Host " ¡COMPILACIÓN Y FIRMA COMPLETADAS! "
Write-Host "======================================" -ForegroundColor Green
Write-Host ""