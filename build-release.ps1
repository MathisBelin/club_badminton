<#
    Construit une nouvelle version : exe autonome + installeur setup.exe.

    Utilisation :
        powershell -ExecutionPolicy Bypass -File build-release.ps1              # rebuild avec la version actuelle
        powershell -ExecutionPolicy Bypass -File build-release.ps1 -Version 1.1.0   # bump de version + rebuild

    Résultat : dist\ClubBadminton-Setup-<version>.exe
    (l'installeur met à jour une installation existante sans toucher aux données utilisateur)
#>
param([string]$Version)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$csproj = Join-Path $root "BadmintonClub.csproj"
$iss    = Join-Path $root "installer\ClubBadminton.iss"

# 1. Mise à jour éventuelle du numéro de version
if ($Version) {
    # Lire ET écrire en UTF-8 explicite : sinon PowerShell 5.1 relit ces fichiers
    # en ANSI et corrompt les accents (« Créer » -> « CrÃ©er »).
    (Get-Content $csproj -Raw -Encoding utf8) -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>" |
        Set-Content $csproj -Encoding utf8
    (Get-Content $iss -Raw -Encoding utf8) -replace '#define MyAppVersion "[^"]+"', "#define MyAppVersion `"$Version`"" |
        Set-Content $iss -Encoding utf8
    Write-Host "Version -> $Version" -ForegroundColor Cyan
}

# 2. Publication de l'exe autonome
Write-Host "Publication de l'exe autonome..." -ForegroundColor Cyan
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true --nologo
if ($LASTEXITCODE -ne 0) { throw "Echec de la publication." }

# 3. Compilation de l'installeur (Inno Setup)
$iscc = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host "Inno Setup introuvable. Exe publié dans bin\Release\...\publish\" -ForegroundColor Yellow
    Write-Host "Installez Inno Setup 6 (https://jrsoftware.org/isdl.php) puis relancez." -ForegroundColor Yellow
    return
}

Write-Host "Compilation de l'installeur..." -ForegroundColor Cyan
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "Echec de la compilation de l'installeur." }

Write-Host "Terminé. Installeur dans le dossier dist\." -ForegroundColor Green
