# ============================================================
# BUILD_AND_SIGN.ps1 (Detección Dinámica + Auto-Confianza)
# ============================================================

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
# El directorio raíz de tu repositorio está un nivel por encima de .deploys
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host " DETECTANDO PROYECTO DENTRO DE $RepoRoot"
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Buscamos todos los proyectos .csproj de forma recursiva en el repositorio
$AllProjects = Get-ChildItem -Path $RepoRoot -Filter "*.csproj" -Recurse

if ($AllProjects.Count -eq 0) {
    throw "No se encontró ningún archivo de proyecto (.csproj) en el repositorio."
}

# Intentamos localizar el proyecto principal (suele contener Desktop)
$ProjectFile = $AllProjects | Where-Object { $_.Name -like "*Desktop.csproj" } | Select-Object -First 1

# Si no hay ninguno con 'Desktop', buscamos el principal que no sea Core ni Audio
if (-not $ProjectFile) {
    $ProjectFile = $AllProjects | Where-Object { $_.Name -notlike "*Core.csproj" -and $_.Name -notlike "*Audio.csproj" } | Select-Object -First 1
}

# Si seguimos sin encontrar nada específico, usamos el primero del repositorio
if (-not $ProjectFile) {
    $ProjectFile = $AllProjects[0]
}

Write-Host "Proyecto localizado con éxito: $($ProjectFile.FullName)" -ForegroundColor Green

$Configuration = "Release"
$Runtime = "win-x64"

# === GESTIÓN SEGURA DE CONTRASEÑA ===
$CertPassword = $env:MY_GITHUB_SECRET_PASSWORD
if (-not $CertPassword) {
    Write-Host "⚠️ Ejecución local detectada." -ForegroundColor Yellow
    $CertPassword = Read-Host -Prompt "Introduce la contraseña para el certificado local"
    if (-not $CertPassword) {
        throw "La contraseña no puede estar vacía."
    }
}

$CertName = "Gabriel Garcia"
$SingleFile = $true
$ErrorActionPreference = "Stop"

$ProjectDir = Split-Path $ProjectFile.FullName -Parent
$PublishDir = Join-Path $ProjectDir "publish_output"

# === GENERAR CERTIFICADO ===
Write-Host "Buscando certificado..." -ForegroundColor Yellow
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq "CN=$CertName" } | Select-Object -First 1

if (-not $cert) {
    Write-Host "Creando certificado autofirmado..." -ForegroundColor Yellow
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

# === CONFIA EN EL CERTIFICADO EN LA MÁQUINA DE COMPILACIÓN ===
# Importamos el certificado en el almacén de "Entidades de certificación raíz de confianza"
# Esto evita que 'signtool verify' falle por falta de confianza en la máquina virtual.
Write-Host "Instalando certificado en el almacén de confianza local..." -ForegroundColor Yellow
Import-PfxCertificate -FilePath $PfxFile -CertStoreLocation Cert:\CurrentUser\Root -Password $password | Out-Null

# === LIMPIAR Y COMPILAR ===
if (Test-Path $PublishDir) { 
    Remove-Item $PublishDir -Recurse -Force 
}

Write-Host "Publicando aplicación..." -ForegroundColor Cyan
$PublishArgs = @("publish", $ProjectFile.FullName, "-c", $Configuration, "-r", $Runtime, "--self-contained", "true", "-o", $PublishDir)
if ($SingleFile) {
    $PublishArgs += "-p:PublishSingleFile=true"
    $PublishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
}
& dotnet $PublishArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish ha fallado."
}

# === BUSCAR SIGNTOOL ===
$PossiblePaths = @(
    "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe",
    "C:\Program Files (x86)\Microsoft SDKs\ClickOnce\SignTool\signtool.exe",
    "C:\Program Files\Windows Kits\10\bin\*\x64\signtool.exe"
)
$signtoolPath = $null
foreach ($pattern in $PossiblePaths) {
    $resolved = Resolve-Path $pattern -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($resolved) { $signtoolPath = $resolved.Path; break }
}
if (-not $signtoolPath) { throw "No se pudo encontrar SignTool.exe" }

# === FIRMAR EXES ===
$exeFiles = Get-ChildItem $PublishDir -Filter *.exe -File
foreach ($exe in $exeFiles) {
    Write-Host "Firmando $($exe.Name)..." -ForegroundColor Cyan
    & $signtoolPath sign /f $PfxFile /p $CertPassword /fd SHA256 $exe.FullName
}

# === VERIFICAR ===
Write-Host "Verificando validez de las firmas..." -ForegroundColor Yellow
foreach ($exe in $exeFiles) {
    & $signtoolPath verify /pa $exe.FullName
}

# === EMPAQUETAR EN ZIP ===
Write-Host "Comprimiendo salida..." -ForegroundColor Yellow
$BaseProjectName = $ProjectFile.BaseName -replace "\.Desktop$", ""
if (-not $BaseProjectName) { $BaseProjectName = "IterVC" }

$ZipFile = Join-Path $RepoRoot "$BaseProjectName-Windows.zip"
if (Test-Path $ZipFile) { Remove-Item $ZipFile -Force }

Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipFile

Write-Host ""
Write-Host "======================================" -ForegroundColor Green
Write-Host " PROCESO COMPLETADO CON ÉXITO"
Write-Host " Archivo generado: $ZipFile"
Write-Host "======================================" -ForegroundColor Green
Write-Host ""

exit 0 