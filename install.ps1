<#
    Installe (ou désinstalle) « Club de Badminton » pour l'utilisateur courant.
    - Copie l'exe autonome dans %LOCALAPPDATA%\Programs\ClubBadminton
    - Crée les raccourcis Menu Démarrer + Bureau (avec l'icône et le nom)
    Aucun droit administrateur requis.

    Utilisation :
        powershell -ExecutionPolicy Bypass -File install.ps1            # installer
        powershell -ExecutionPolicy Bypass -File install.ps1 -Uninstall # désinstaller
#>
param([switch]$Uninstall)

$AppName    = "Club de Badminton"
$InstallDir = Join-Path $env:LOCALAPPDATA "Programs\ClubBadminton"
$Exe        = Join-Path $InstallDir "BadmintonClub.exe"
$StartMenu  = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$AppName.lnk"
$Desktop    = Join-Path ([Environment]::GetFolderPath('Desktop')) "$AppName.lnk"

if ($Uninstall) {
    Get-Process BadmintonClub -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item $StartMenu, $Desktop -ErrorAction SilentlyContinue
    Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Club de Badminton a été désinstallé." -ForegroundColor Green
    return
}

$PublishDir = Join-Path $PSScriptRoot "bin\Release\net8.0-windows\win-x64\publish"
$SrcExe = Join-Path $PublishDir "BadmintonClub.exe"
if (-not (Test-Path $SrcExe)) {
    Write-Host "Exe introuvable. Lancez d'abord :" -ForegroundColor Red
    Write-Host '  dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true'
    return
}

Get-Process BadmintonClub -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item $SrcExe $InstallDir -Force

# client_secret.json (si présent à côté de l'exe publié) pour la synchro Google
$cs = Join-Path $PublishDir "client_secret.json"
if (Test-Path $cs) { Copy-Item $cs $InstallDir -Force }

$ws = New-Object -ComObject WScript.Shell
foreach ($lnk in @($StartMenu, $Desktop)) {
    $s = $ws.CreateShortcut($lnk)
    $s.TargetPath       = $Exe
    $s.WorkingDirectory = $InstallDir
    $s.IconLocation     = "$Exe,0"
    $s.Description       = $AppName
    $s.Save()
}

Write-Host "Club de Badminton est installé." -ForegroundColor Green
Write-Host "  Dossier    : $InstallDir"
Write-Host "  Menu Démarrer + Bureau : raccourci « $AppName »"
Write-Host "Vous pouvez le lancer depuis le menu Windows (et l'épingler à la barre des tâches)."
