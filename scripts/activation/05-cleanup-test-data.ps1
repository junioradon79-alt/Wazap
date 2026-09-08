#Requires -Version 5.1
<#
.SYNOPSIS
    Nettoie les donnees de test en production.
.EXAMPLE
    .\05-cleanup-test-data.ps1
    .\05-cleanup-test-data.ps1 -Confirm
#>
param(
    [string]$ProjectRoot = "C:\Dev\Wazap\WazapSln",
    [switch]$Confirm
)
$ErrorActionPreference = "Stop"
Write-Host "=== Nettoyage des donnees de test ===" -ForegroundColor Cyan
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
    dotnet run --project tools/CleanupTestVendors 2>&1 | Out-Null
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
