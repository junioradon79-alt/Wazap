#Requires -Version 5.1
<#
.SYNOPSIS
    Nettoie les donnees de test (base de DEV/TEST uniquement).
.DESCRIPTION
    ATTENTION : l'outil PurgeTestData execute des DELETE SANS clause WHERE
    (Orders, DeliveryBatches, CreditTransactions, OutboxMessages...) et remet a zero les
    credits de TOUS les vendeurs. Lance sur la base de PRODUCTION, il efface l'activite
    reelle. Un garde-fou refuse donc toute base dont le nom ne contient ni « test » ni
    « dev », sauf -Force explicite (meme regle que 09-purge-test-accounts.ps1).
.EXAMPLE
    .\05-cleanup-test-data.ps1
    .\05-cleanup-test-data.ps1 -Confirm
    .\05-cleanup-test-data.ps1 -Confirm -Force   # base dont le nom ne contient pas test/dev
#>
param(
    [string]$ProjectRoot,
    [switch]$Confirm,
    [switch]$Force
)
$ErrorActionPreference = "Stop"

# Chemins deduits du script (aucun chemin absolu propre a un poste) :
#   ...\WazapSln\scripts\activation\05-cleanup-test-data.ps1
#   -> $PSScriptRoot\..\..    = ...\WazapSln  (solution)
#   -> $PSScriptRoot\..\..\.. = ...\Wazap     (espace de travail : secrets, backups)
$solutionRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$workspaceRoot = Split-Path $solutionRoot -Parent
if (-not $ProjectRoot) { $ProjectRoot = $solutionRoot }

# L'outil PurgeTestData ecrit sa sauvegarde JSON dans WAZAP_BACKUP_DIR (sinon « backups » sous
# le dossier courant) : on fixe ce dossier a cote des autres sauvegardes du projet.
if (-not $env:WAZAP_BACKUP_DIR) { $env:WAZAP_BACKUP_DIR = Join-Path $workspaceRoot "backups" }

function Get-DatabaseName([string]$connectionString) {
    if ([string]::IsNullOrWhiteSpace($connectionString)) { return $null }
    $match = [regex]::Match($connectionString, '(?i)(?:^|;)\s*(?:Database|Initial Catalog)\s*=\s*([^;]+)')
    if ($match.Success) { return $match.Groups[1].Value.Trim() }
    return $null
}

$databaseName = Get-DatabaseName $env:WAZAP_CONNECTION_STRING
if (-not $Force -and ($null -eq $databaseName -or $databaseName -notmatch '(?i)(test|dev)')) {
    Write-Host "REFUS : la base visee ('$databaseName') n'est pas identifiable comme base de test/dev." -ForegroundColor Red
    Write-Host "Cet outil supprime TOUTES les commandes, lots et transactions, et remet les credits" -ForegroundColor Red
    Write-Host "de tous les vendeurs a zero. Definissez WAZAP_CONNECTION_STRING sur une base de test," -ForegroundColor Red
    Write-Host "ou passez -Force en connaissance de cause." -ForegroundColor Red
    exit 1
}

Write-Host "=== Nettoyage des donnees de test (base : $databaseName) ===" -ForegroundColor Cyan
Write-Host ""
if (-not $Confirm) {
    Write-Host "ATTENTION : Cette commande va supprimer toutes les donnees de test !" -ForegroundColor Red
    $c = Read-Host "Confirmer ? (O/N)"
    if ($c -ne "O") { Write-Host "Annule." -ForegroundColor DarkYellow; exit 0 }
}
Write-Host "[1] Purge des commandes/offres/lots..." -ForegroundColor Yellow
try {
    Push-Location $ProjectRoot
    dotnet run --project tools/PurgeTestData -- --confirm 2>&1 | Out-Null
    Write-Host "      OK" -ForegroundColor Green
}
catch {
    Write-Host "      Erreur : $_" -ForegroundColor Red
}
finally {
    Pop-Location
}
Write-Host "[2] Suppression comptes test_..." -ForegroundColor Yellow
try {
    Push-Location $ProjectRoot
    dotnet run --project tools/CleanupTestVendors -- --confirm 2>&1 | Out-Null
    Write-Host "      OK" -ForegroundColor Green
}
catch {
    Write-Host "      Erreur : $_" -ForegroundColor Red
}
finally {
    Pop-Location
}
Write-Host ""
Write-Host "=== Nettoyage termine ===" -ForegroundColor Cyan
