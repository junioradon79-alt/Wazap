<#
Script de déploiement MANUEL vers SmarterASP.NET via FTP.

⚠️ Ce script est un outil de secours. Le déploiement nominal passe par
`.github/workflows/deploy.yml` + `scripts/cd-deploy.sh` (manifeste SHA-256 différentiel,
exclusions, health check). Préférez-les.

Prérequis : définir les variables d'environnement suivantes
  $env:SMARTERASP_FTP_HOST        ex: ftp://votre-domaine.com
  $env:SMARTERASP_FTP_USER
  $env:SMARTERASP_FTP_PASSWORD
  $env:SMARTERASP_FTP_REMOTE_DIR  ex: /wazap2 (défaut: /wazap2)

Usage :
  .\scripts\deploy.ps1
#>

$ErrorActionPreference = "Stop"

$FtpHost = $env:SMARTERASP_FTP_HOST
$FtpUser = $env:SMARTERASP_FTP_USER
$FtpPassword = $env:SMARTERASP_FTP_PASSWORD
$RemoteDir = $env:SMARTERASP_FTP_REMOTE_DIR

# Le répertoire servi par SmarterASP est /wazap2 (cf. deploy.yml, cd-deploy.sh,
# DEPLOYMENT.md) : /wwwroot n'est pas servi, le déploiement y était donc sans effet.
if (-not $RemoteDir) { $RemoteDir = "/wazap2" }

if (-not $FtpHost -or -not $FtpUser -or -not $FtpPassword) {
    Write-Error "Définissez SMARTERASP_FTP_HOST, SMARTERASP_FTP_USER et SMARTERASP_FTP_PASSWORD."
    exit 1
}

$root = Split-Path $PSScriptRoot -Parent
$publishDir = Join-Path $root "artifacts\publish"

Write-Host "==> Publication de l'API (Release) ..."
dotnet publish (Join-Path $root "src\Wazap.API\Wazap.API.csproj") -c Release -o $publishDir --no-restore
if ($LASTEXITCODE -ne 0) { throw "Publication échouée." }

# Fichiers qui ne doivent JAMAIS être écrasés sur le serveur :
#   • web.config        → porte TOUTES les variables d'environnement de production
#                         (chaîne de connexion, Jwt__Key, clés GeniusPay, clé de
#                         chiffrement des scans d'identité). L'écraser casse la prod
#                         et rend les scans de CNI illisibles.
#   • app_offline.htm   → sert à couper le site pendant une maintenance.
#   • appsettings.Development.json → config de développement, sans objet en production.
$excludedNames = @('web.config', 'app_offline.htm', 'appsettings.Development.json')

Write-Host "==> Upload FTP vers $FtpHost$RemoteDir ..."
$publishRoot = (Resolve-Path $publishDir).Path
$files = Get-ChildItem -Path $publishDir -Recurse -File |
    Where-Object { $excludedNames -notcontains $_.Name }

foreach ($file in $files) {
    $relative = $file.FullName.Substring($publishRoot.Length + 1).Replace('\', '/')
    $remoteUrl = "$FtpHost$RemoteDir/$relative"
    Write-Host "  -> $relative"

    curl.exe --silent --show-error --fail --user "${FtpUser}:${FtpPassword}" --ftp-create-dirs -T $file.FullName $remoteUrl
    if ($LASTEXITCODE -ne 0) { throw "Échec d'upload pour $relative" }
}

Write-Host "==> Déploiement terminé avec succès."
Write-Host "    Vérifiez /health et /health/details, puis la page /app (front à jour)."
