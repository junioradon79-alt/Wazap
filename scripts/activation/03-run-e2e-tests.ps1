#Requires -Version 5.1
<#
.SYNOPSIS
    Lance les tests E2E reel WAZAP (S1-S4) via WhatsApp.
.EXAMPLE
    .\03-run-e2e-tests.ps1 -VendorPhone "+2250708091011" -RiderPhone "+2250708091012"
#>
param(
    [string]$BaseUrl = "https://junioradon79gm-001-site1.jtempurl.com",
    [string]$VendorPhone = "",
    [string]$RiderPhone = "",
    [string]$ClientPhone = "",
    [string]$AdminToken = "",
    [switch]$DryRun
)
$ErrorActionPreference = "Stop"
Write-Host "=== Tests E2E WAZAP ===" -ForegroundColor Cyan
Write-Host ""

if ([string]::IsNullOrWhiteSpace($AdminToken)) { $AdminToken = Read-Host "Token Admin JWT" }
if ([string]::IsNullOrWhiteSpace($VendorPhone)) { $VendorPhone = Read-Host "Numero vendeur (ex: +2250708091011)" }
if ([string]::IsNullOrWhiteSpace($RiderPhone)) { $RiderPhone = Read-Host "Numero livreur (ex: +2250708091012)" }
if ([string]::IsNullOrWhiteSpace($ClientPhone)) { $ClientPhone = $VendorPhone }

$headers = @{ "Authorization" = "Bearer $AdminToken"; "Content-Type" = "application/json" }

Write-Host "[INFO] Telephones :" -ForegroundColor Cyan
Write-Host "  Vendeur : $VendorPhone" -ForegroundColor Gray
Write-Host "  Livreur : $RiderPhone" -ForegroundColor Gray
Write-Host "  Client  : $ClientPhone" -ForegroundColor Gray
Write-Host ""

# Creer les comptes de test
Write-Host "[1] Creation des comptes de test..." -ForegroundColor Yellow

$vendorBody = @{ username = "test_vendeur_e2e"; password = "Test@1234"; role = "Vendor"; phoneNumber = $VendorPhone } | ConvertTo-Json
$riderBody = @{ username = "test_livreur_e2e"; password = "Test@1234"; role = "Rider"; phoneNumber = $RiderPhone } | ConvertTo-Json

try {
    if (-not $DryRun) {
        $vendorResult = Invoke-RestMethod -Uri "$BaseUrl/api/auth/register" -Headers $headers -Method Post -Body $vendorBody
        $riderResult = Invoke-RestMethod -Uri "$BaseUrl/api/auth/register" -Headers $headers -Method Post -Body $riderBody
        Write-Host "  Vendeur cree : $($vendorResult.username)" -ForegroundColor Green
        Write-Host "  Livreur cree : $($riderResult.username)" -ForegroundColor Green
    } else {
        Write-Host "  [DRY-RUN] Comptes simules" -ForegroundColor DarkYellow
    }
}
catch {
    Write-Error "Erreur creation comptes : $_"
    exit 1
}

Write-Host ""
Write-Host "[2] Etapes de test (a effectuer sur WhatsApp) :" -ForegroundColor Yellow
Write-Host ""

Write-Host "=== S1 - Livraison a la demande ===" -ForegroundColor Cyan
Write-Host "  a) Le vendeur envoie : ZONE Cocody" -ForegroundColor White
Write-Host "  b) Le vendeur envoie : LIVRAISON 2 poulets braises a Marcory, rue Princesse, tel $ClientPhone" -ForegroundColor White
Write-Host "  c) Le livreur envoie : ACCEPTE <code_commande>" -ForegroundColor White
Write-Host "  d) Le livreur envoie : RECU" -ForegroundColor White
Write-Host "  e) Le livreur envoie : LIVRE" -ForegroundColor White
Write-Host "  f) Verifier : statuts RiderAssigned -> InTransit -> Delivered" -ForegroundColor White
Write-Host ""

Write-Host "=== S2 - Tournée multi-clients ===" -ForegroundColor Cyan
Write-Host "  a) Creer 2 commandes client du meme vendeur (API/espace)" -ForegroundColor White
Write-Host "  b) Les confirmer rapidement (fenetre de groupage 2 min)" -ForegroundColor White
Write-Host "  c) Le livreur recoit une liste detaillee" -ForegroundColor White
Write-Host "  d) RECU -> tournee InTransit" -ForegroundColor White
Write-Host "  e) LIVRE sans code -> refuse (multi)" -ForegroundColor White
Write-Host "  f) LIVRE <code1> -> Client A notifie; LIVRE <code2> -> Client B notifie" -ForegroundColor White
Write-Host ""

Write-Host "=== S3 - Parcours acheteur PWA ===" -ForegroundColor Cyan
Write-Host "  a) Creer une commande client avec numero WhatsApp reel" -ForegroundColor White
Write-Host "  b) Le vendeur confirme (bouton WhatsApp ou API)" -ForegroundColor White
Write-Host "  c) Le client recoit le lien de suivi" -ForegroundColor White
Write-Host "  d) Le client ouvre le lien et valide ses coordonnees GPS" -ForegroundColor White
Write-Host "  e) Le vendeur est notifie ; les livreurs sont contactes" -ForegroundColor White
Write-Host "  f) L'acheteur suit jusqu'a 'Livré ✓'" -ForegroundColor White
Write-Host ""

Write-Host "=== S4 - Cas negatifs ===" -ForegroundColor Cyan
Write-Host "  a) Vendeur sans zone envoie LIVRAISON -> message d'erreur" -ForegroundColor White
Write-Host "  b) LIVRAISON sans texte apres la commande -> message format" -ForegroundColor White
Write-Host "  c) Livreur LIVRE avec plusieurs cours et sans code -> refuse" -ForegroundColor White
Write-Host "  d) Vendeur a 0 credit -> acceptation livreur refusee (402)" -ForegroundColor White
Write-Host ""

Write-Host "[3] Verification des statuts (API) :" -ForegroundColor Yellow
Write-Host "  Commandes actives :" -ForegroundColor Gray

if (-not $DryRun) {
    try {
        $orders = Invoke-RestMethod -Uri "$BaseUrl/api/orders" -Headers $headers -Method Get
        $orders | Where-Object { $_.status -ne "Delivered" -and $_.status -ne "Cancelled" } |
            Format-Table -Property id, status, vendorUserId, riderUserId -AutoSize
    }
    catch {
        Write-Host "  Erreur : $_" -ForegroundColor Red
    }
} else {
    Write-Host "  [DRY-RUN] Simulation" -ForegroundColor DarkYellow
}

Write-Host ""
Write-Host "=== Instructions de nettoyage (apres test) ===" -ForegroundColor Cyan
Write-Host "  dotnet run --project tools/PurgeTestData -- --confirm" -ForegroundColor White
Write-Host "  dotnet run --project tools/CleanupTestVendors" -ForegroundColor White
Write-Host ""
Write-Host "=== Tests E2E prepares ===" -ForegroundColor Cyan
