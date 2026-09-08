#Requires -Version 5.1
<#
.SYNOPSIS
    Active la securite certification livreurs en production (SmarterASP).
.EXAMPLE
    .\04-activate-rider-security.ps1
#>
param(
    [string]$FtpHost = "ftp://WIN6054.site4now.net",
    [string]$FtpUser = "junioradon79gm-001",
    [string]$FtpPass = "",
    [string]$RemotePath = "/wazap2",
    [switch]$DryRun
)
$ErrorActionPreference = "Stop"

# Identifiants FTP : jamais en dur dans le dépôt (voir DEPLOYMENT.md).
if ([string]::IsNullOrWhiteSpace($FtpPass)) { $FtpPass = $env:WAZAP_FTP_PASS }
if ([string]::IsNullOrWhiteSpace($FtpPass)) { $FtpPass = Read-Host "Mot de passe FTP SmarterASP (voir DEPLOYMENT.md)" }
Write-Host "=== Activation Securite Certification Livreurs ===" -ForegroundColor Cyan
Write-Host ""
$localConfig = Join-Path $PSScriptRoot "web.config.remote"
Write-Host "[1/3] Telechargement web.config..." -ForegroundColor Yellow
if (-not $DryRun) {
    $wc = New-Object System.Net.WebClient
    $wc.Credentials = New-Object System.Net.NetworkCredential($FtpUser, $FtpPass)
    $wc.DownloadFile("$FtpHost$RemotePath/web.config", $localConfig)
    Write-Host "      OK" -ForegroundColor Green
} else { Write-Host "      [DRY-RUN]" -ForegroundColor DarkYellow }
Write-Host "[2/3] Verification configuration..." -ForegroundColor Yellow
$content = Get-Content $localConfig -Raw
if ($content -like "*RiderSecurity__RequireCertifiedRiders*") {
    $content = $content -replace '<environmentVariable name="RiderSecurity__RequireCertifiedRiders" value="[^"]*" />', '<environmentVariable name="RiderSecurity__RequireCertifiedRiders" value="true" />'
    Write-Host "      Mise a jour effectuee" -ForegroundColor Green
} else {
    $marker = "</environmentVariables>"
    $newVar = '<environmentVariable name="RiderSecurity__RequireCertifiedRiders" value="true" />'
    $content = $content.Replace($marker, "    $newVar`n    $marker")
    Write-Host "      Variable ajoutee" -ForegroundColor Green
}
$content | Set-Content $localConfig -Encoding UTF8
Write-Host "[3/3] Upload web.config..." -ForegroundColor Yellow
if (-not $DryRun) {
    $wc = New-Object System.Net.WebClient
    $wc.Credentials = New-Object System.Net.NetworkCredential($FtpUser, $FtpPass)
    $wc.UploadFile("$FtpHost$RemotePath/web.config", $localConfig)
    Write-Host "      OK" -ForegroundColor Green
} else { Write-Host "      [DRY-RUN]" -ForegroundColor DarkYellow }
Write-Host ""
Write-Host "=== Activation terminee ===" -ForegroundColor Cyan
Write-Host "Verifier : GET https://junioradon79gm-001-site1.jtempurl.com/health/details" -ForegroundColor White
