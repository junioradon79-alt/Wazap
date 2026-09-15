#Requires -Version 5.1
<#
.SYNOPSIS
    Audit puis purge des comptes de test en base (prod ou dev).
.DESCRIPTION
    1) Inventaire + sauvegarde JSON (toujours) : tools/PurgeTestData en dry-run
       (lit WAZAP_CONNECTION_STRING, ecrit backups/purge_backup_*.json).
    2) -Confirm : supprime les comptes de test via tools/CleanupTestVendors --confirm
       (usernames 'test_%' / 'vendeur_test%' + remise a 0 des credits de la Pizzeria).
    3) -FullPurge -Confirm : purge transactionnelle complete (tools/PurgeTestData --confirm) :
       supprime TOUTES les commandes/offres/lots/transactions/outbox, remet TOUS les credits
       a 0 et invalide TOUTES les sessions (RefreshTokens). A reserver aux bases ne
       contenant que des donnees de test (garde-fou : -Force si le nom de base ne
       contient ni 'test' ni 'dev').
.EXAMPLE
    .\09-purge-test-accounts.ps1
    # Audit seul : aucune suppression ; liste des comptes de test + sauvegarde JSON.
.EXAMPLE
    .\09-purge-test-accounts.ps1 -Confirm
    # Liste puis supprime les comptes de test.
.EXAMPLE
    .\09-purge-test-accounts.ps1 -Confirm -FullPurge -Force
    # Purge complete (donnees transactionnelles incluses).
.NOTES
    Chaine de connexion resolue dans cet ordre : -ConnectionString, $env:WAZAP_CONNECTION_STRING,
    puis le web.config distant sauvegarde (secrets/web.config.server.xml).
    ATTENTION : la purge invalide les jetons -> re-connexion admin obligatoire ensuite.
#>
param(
    [string]$ProjectRoot,
    [string]$ConnectionString = "",
    [string]$RemoteConfig,
    [switch]$Confirm,
    [switch]$FullPurge,
    [switch]$Force
)
$ErrorActionPreference = "Stop"

# Chemins deduits du script (aucun chemin absolu propre a un poste) :
#   ...\WazapSln\scripts\activation\09-...ps1  ->  ..\.. = WazapSln, ..\..\.. = espace de travail
$solutionRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$workspaceRoot = Split-Path $solutionRoot -Parent
if (-not $ProjectRoot) { $ProjectRoot = $solutionRoot }
if (-not $RemoteConfig) { $RemoteConfig = Join-Path $workspaceRoot "secrets\web.config.server.xml" }

function Resolve-ConnectionString {
    if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) { return $ConnectionString }
    if (-not [string]::IsNullOrWhiteSpace($env:WAZAP_CONNECTION_STRING)) { return $env:WAZAP_CONNECTION_STRING }
    if (Test-Path $RemoteConfig) {
        $xml = Get-Content $RemoteConfig -Raw
        $m = [regex]::Match($xml, 'name="ConnectionStrings__DefaultConnection"\s*value="([^"]+)"')
        if (-not $m.Success) {
            $m = [regex]::Match($xml, 'value="([^"]+)"\s*name="ConnectionStrings__DefaultConnection"')
        }
        if ($m.Success) { return $m.Groups[1].Value }
    }
    throw "Chaine de connexion introuvable : utilisez -ConnectionString, WAZAP_CONNECTION_STRING ou verifiez $RemoteConfig."
}

function Get-TargetInfo {
    param([Parameter(Mandatory = $true)][string]$Cs)
    $hostName = ([regex]::Match($Cs, 'Host=([^;]+)')).Groups[1].Value
    $port = ([regex]::Match($Cs, 'Port=([^;]+)')).Groups[1].Value
    $db = ([regex]::Match($Cs, 'Database=([^;]+)')).Groups[1].Value
    return "$hostName`:$port / $db"
}

function Invoke-Tool {
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [string[]]$ToolArgs = @()
    )
    Push-Location $ProjectRoot
    try {
        if ($ToolArgs.Count -gt 0) {
            & dotnet run --project $Project -- @ToolArgs
        }
        else {
            & dotnet run --project $Project
        }
        if ($LASTEXITCODE -ne 0) {
            throw "L'outil $Project a retourne le code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

# ---- 1. Cible + perimetre ----
$cs = Resolve-ConnectionString
$env:WAZAP_CONNECTION_STRING = $cs

Write-Host "=== Purge / audit des comptes de test ===" -ForegroundColor Cyan
Write-Host "Cible     : $(Get-TargetInfo -Cs $cs)" -ForegroundColor Yellow
if ($Confirm) {
    Write-Host "Mode      : SUPPRESSION" -ForegroundColor Yellow
}
else {
    Write-Host "Mode      : AUDIT (dry-run, aucune suppression)" -ForegroundColor Yellow
}
if ($FullPurge) {
    Write-Host "Perimetre : FULL PURGE (commandes/offres/lots/transactions/credits/sessions)" -ForegroundColor Red
}
else {
    Write-Host "Perimetre : comptes de test uniquement (Username 'test%' / '%_test%', insensible a la casse)" -ForegroundColor Yellow
}
Write-Host ""

if (-not $Confirm) {
    Write-Host "  -Confirm            : supprime les comptes de test" -ForegroundColor DarkGray
    Write-Host "  -Confirm -FullPurge : purge transactionnelle complete (-Force si la base ne contient ni 'test' ni 'dev')" -ForegroundColor DarkGray
    Write-Host ""
}

# ---- 2. Inventaire + sauvegarde JSON (toujours) ----
Write-Host "[1/3] Inventaire + sauvegarde JSON (tools/PurgeTestData, dry-run)..." -ForegroundColor Yellow
Invoke-Tool -Project "tools/PurgeTestData"

# ---- 3. Comptes de test ----
Write-Host ""
if ($Confirm) {
    Write-Host "[2/3] Suppression des comptes de test (tools/CleanupTestVendors --confirm)..." -ForegroundColor Yellow
    Invoke-Tool -Project "tools/CleanupTestVendors" -ToolArgs @("--confirm")
}
else {
    Write-Host "[2/3] Comptes de test : inspection seule (dry-run)..." -ForegroundColor DarkGray
    Invoke-Tool -Project "tools/CleanupTestVendors"
}

# ---- 4. Purge transactionnelle (optionnelle) ----
Write-Host ""
if (-not $FullPurge) {
    Write-Host "[3/3] Purge transactionnelle : non demandee (ajouter -FullPurge)." -ForegroundColor DarkGray
}
else {
    if (-not $Confirm) {
        throw "-FullPurge exige aussi -Confirm (aucune suppression en mode audit)."
    }

    $dbName = ([regex]::Match($cs, 'Database=([^;]+)')).Groups[1].Value
    if (-not $Force -and $dbName -notmatch '(test|dev)') {
        throw "Garde-fou : la base '$dbName' ne ressemble pas a une base de test/dev. Ajoutez -Force pour confirmer la purge transactionnelle complete."
    }

    Write-Host "[3/3] PURGE COMPLETE de la base '$dbName' (tools/PurgeTestData --confirm)..." -ForegroundColor Red
    Invoke-Tool -Project "tools/PurgeTestData" -ToolArgs @("--confirm")
    Write-Host "Sessions invalidees (RefreshTokens supprimes) : re-connexion de l'admin necessaire." -ForegroundColor DarkYellow
}

Write-Host ""
Write-Host "=== Termine ===" -ForegroundColor Cyan
Write-Host "Sauvegarde JSON : $workspaceRoot\backups\purge_backup_*.json" -ForegroundColor DarkGray