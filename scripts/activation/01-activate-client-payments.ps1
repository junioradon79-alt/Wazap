#Requires -Version 5.1
<#
.SYNOPSIS
    Active le paiement client Mobile Money en production (SmarterASP).

.DESCRIPTION
    Modifie le web.config distant pour activer ClientPayments__Enabled=true.
    Prérequis : accès FTP SmarterASP (voir DEPLOYMENT.md).

.EXAMPLE
    .\01-activate-client-payments.ps1
    .\01-activate-client-payments.ps1 -FtpHost "ftp://WIN6054.site4now.net" -FtpUser "junioradon79gm-001" -FtpPass "<mot-de-passe>"
#>

param(
    [string]$FtpHost = "ftp://WIN6054.site4now.net",
    [string]$FtpUser = "junioradon79gm-001",
    [string]$FtpPass = "",
    [string]$RemotePath = "/wazap2",
    [string]$WebConfig = "web.config",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# Identifiants FTP : jamais en dur dans le dépôt (voir DEPLOYMENT.md).
if ([string]::IsNullOrWhiteSpace($FtpPass)) { $FtpPass = $env:WAZAP_FTP_PASS }
if ([string]::IsNullOrWhiteSpace($FtpPass)) { $FtpPass = Read-Host "Mot de passe FTP SmarterASP (voir DEPLOYMENT.md)" }

Write-Host "=== Activation Paiement Client Mobile Money ===" -ForegroundColor Cyan
Write-Host ""

# 1. Télécharger le web.config distant
$localConfig = Join-Path $PSScriptRoot "web.config.remote"
Write-Host "[1/4] Téléchargement du web.config distant..." -ForegroundColor Yellow

if (-not $DryRun) {
    $webClient = New-Object System.Net.WebClient
    $webClient.Credentials = New-Object System.Net.NetworkCredential($FtpUser, $FtpPass)
    $webClient.DownloadFile("$FtpHost$RemotePath/$WebConfig", $localConfig)
    Write-Host "      -> Téléchargé : $localConfig" -ForegroundColor Green
} else {
    Write-Host "      [DRY-RUN] Téléchargement simulé" -ForegroundColor DarkYellow
}

# 2. Vérifier que les variables ne sont pas déjà présentes
Write-Host "[2/4] Vérification de la configuration existante..." -ForegroundColor Yellow

$configContent = Get-Content $localConfig -Raw -ErrorAction Stop

$requiredVars = @(
    "ClientPayments__Enabled",
    "ClientPayments__CommissionPercent",
    "ClientPayments__RequirePaymentBeforeDispatch"
)

$missingVars = @()
foreach ($var in $requiredVars) {
    if ($configContent -notlike "*$var*") {
        $missingVars += $var
    }
}

if ($missingVars.Count -eq 0) {
    Write-Host "      -> Toutes les variables sont déjà présentes !" -ForegroundColor Green
    Write-Host "      -> Aucune modification nécessaire." -ForegroundColor Green
    exit 0
}

Write-Host "      -> Variables manquantes : $($missingVars -join ', ')" -ForegroundColor DarkYellow

# 3. Ajouter les variables d'environnement
Write-Host "[3/4] Ajout des variables ClientPayments..." -ForegroundColor Yellow

$envVars = @"
    <environmentVariable name="ClientPayments__Enabled" value="true" />
    <environmentVariable name="ClientPayments__CommissionPercent" value="2.0" />
    <environmentVariable name="ClientPayments__RequirePaymentBeforeDispatch" value="false" />
"@

# Insérer avant la fermeture de </environmentVariables>
$insertMarker = "</environmentVariables>"
if ($configContent -notlike "*$insertMarker*") {
    Write-Error "Impossible de trouver </environmentVariables> dans le web.config"
    exit 1
}

# Vérifier si ClientPayments__Enabled existe déjà
if ($configContent -like "*ClientPayments__Enabled*") {
    Write-Host "      -> ClientPayments__Enabled déjà présent, mise à jour..." -ForegroundColor DarkYellow
    $configContent = $configContent -replace '<environmentVariable name="ClientPayments__Enabled" value="[^"]*" />', '<environmentVariable name="ClientPayments__Enabled" value="true" />'
} else {
    # Ajouter les nouvelles variables
    $newEnvVars = $envVars.Trim()
    $configContent = $configContent.Replace($insertMarker, "$newEnvVars`n    $insertMarker")
}

# Sauvegarder localement
$configContent | Set-Content $localConfig -Encoding UTF8
Write-Host "      -> web.config modifié localement" -ForegroundColor Green

# 4. Uploader le web.config modifié
Write-Host "[4/4] Upload du web.config modifié..." -ForegroundColor Yellow

if (-not $DryRun) {
    $webClient = New-Object System.Net.WebClient
    $webClient.Credentials = New-Object System.Net.NetworkCredential($FtpUser, $FtpPass)
    $webClient.UploadFile("$FtpHost$RemotePath/$WebConfig", $localConfig)
    Write-Host "      -> Upload terminé !" -ForegroundColor Green
} else {
    Write-Host "      [DRY-RUN] Upload simulé" -ForegroundColor DarkYellow
}

# 5. Vérification
Write-Host ""
Write-Host "=== Vérification ===" -ForegroundColor Cyan
Write-Host "Tester l'endpoint :"
Write-Host "  POST https://junioradon79gm-001-site1.jtempurl.com/api/client/orders/{orderId}/pay" -ForegroundColor White
Write-Host ""
Write-Host "Réponse attendue (200) :" -ForegroundColor Green
Write-Host '  { "status": "Pending", "amount": 3500, "paymentLink": "https://geniuspay.ci/checkout/..." }' -ForegroundColor Gray
Write-Host ""
Write-Host "=== Activation terminée ===" -ForegroundColor Cyan
