<#
Script de déploiement vers SmarterASP.NET via FTP.

Prérequis : définir les variables d'environnement suivantes
  $env:SMARTERASP_FTP_HOST        ex: ftp://votre-domaine.com
  $env:SMARTERASP_FTP_USER
  $env:SMARTERASP_FTP_PASSWORD
  $env:SMARTERASP_FTP_REMOTE_DIR  ex: /wwwroot (défaut: /wwwroot)

Usage :
  .\scripts\deploy.ps1
#>

$ErrorActionPreference = "Stop"

$FtpHost = $env:SMARTERASP_FTP_HOST
$FtpUser = $env:SMARTERASP_FTP_USER
$FtpPassword = $env:SMARTERASP_FTP_PASSWORD
$RemoteDir = $env:SMARTERASP_FTP_REMOTE_DIR
if (-not $RemoteDir) { $RemoteDir = "/wwwroot" }

if (-not $FtpHost -or -not $FtpUser -or -not $FtpPassword) {
    Write-Error "Définissez SMARTERASP_FTP_HOST, SMARTERASP_FTP_USER et SMARTERASP_FTP_PASSWORD."
    exit 1
}

$root = Split-Path $PSScriptRoot -Parent
$publishDir = Join-Path $root "artifacts\publish"

Write-Host "==> Publication de l'API (Release) ..."
dotnet publish (Join-Path $root "src\Wazap.API\Wazap.API.csproj") -c Release -o $publishDir --no-restore
if ($LASTEXITCODE -ne 0) { throw "Publication échouée." }

Write-Host "==> Upload FTP vers $FtpHost$RemoteDir ..."
$publishRoot = (Resolve-Path $publishDir).Path
$files = Get-ChildItem -Path $publishDir -Recurse -File

foreach ($file in $files) {
    $relative = $file.FullName.Substring($publishRoot.Length + 1).Replace('\', '/')
    $remoteUrl = "$FtpHost$RemoteDir/$relative"
    Write-Host "  -> $relative"

    curl.exe --silent --show-error --fail --user "${FtpUser}:${FtpPassword}" --ftp-create-dirs -T $file.FullName $remoteUrl
    if ($LASTEXITCODE -ne 0) { throw "Échec d'upload pour $relative" }
}

Write-Host "==> Déploiement terminé avec succès."
