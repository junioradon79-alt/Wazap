#Requires -Version 5.1
<#
.SYNOPSIS
    Certifie les livreurs en production via l'API admin WAZAP.
.EXAMPLE
    .\02-certify-riders.ps1 -AdminToken "eyJ..." -ListOnly
    .\02-certify-riders.ps1 -AdminToken "eyJ..." -AutoVerifyAll
    .\02-certify-riders.ps1 -AdminToken "eyJ..." -RiderId "..." -FullName "Ibrahim" -IdNumber "CI123"
#>
param(
    [string]$BaseUrl = "https://junioradon79gm-001-site1.jtempurl.com",
    [string]$AdminToken = "",
    [string]$RiderId = "",
    [string]$FullName = "",
    [string]$IdNumber = "",
    [string]$Motorcycle = "",
    [switch]$AutoVerifyAll,
    [switch]$ListOnly
)
$ErrorActionPreference = "Stop"
Write-Host "=== Certification des Livreurs ===" -ForegroundColor Cyan
if ([string]::IsNullOrWhiteSpace($AdminToken)) { $AdminToken = Read-Host "Token Admin JWT" }
$headers = @{ "Authorization" = "Bearer $AdminToken"; "Content-Type" = "application/json" }
Write-Host "[1] Récupération des certifications..." -ForegroundColor Yellow
try { $certifications = Invoke-RestMethod -Uri "$BaseUrl/api/riders/certifications" -Headers $headers -Method Get }
catch { Write-Error "Erreur : $_"; exit 1 }
$pending = $certifications | Where-Object { $_.status -eq "Pending" }
$verified = $certifications | Where-Object { $_.status -eq "Verified" }
Write-Host "  Pending : $($pending.Count) | Verified : $($verified.Count)" -ForegroundColor Cyan
if ($ListOnly) { $certifications | Format-Table -Property username, status, fullName, idNumber, motorcycle -AutoSize; exit 0 }
if (-not [string]::IsNullOrWhiteSpace($RiderId)) {
    $body = @{ fullName = $FullName; idNumber = $IdNumber; motorcycle = $Motorcycle } | ConvertTo-Json
    try { Invoke-RestMethod -Uri "$BaseUrl/api/riders/$RiderId/verify" -Headers $headers -Method Post -Body $body | Out-Null; Write-Host "Certifie !" -ForegroundColor Green }
    catch { Write-Error "Erreur : $_"; exit 1 }
    exit 0
}
if ($AutoVerifyAll) {
    if ($pending.Count -eq 0) { Write-Host "Aucun en attente." -ForegroundColor Green; exit 0 }
    $c = Read-Host "Certifier $($pending.Count) livreurs ? (O/N)"
    if ($c -ne "O") { Write-Host "Annule." -ForegroundColor DarkYellow; exit 0 }
    $s = 0; $f = 0
    foreach ($r in $pending) {
        $b = @{ fullName = $r.fullName; idNumber = $r.idNumber; motorcycle = $r.motorcycle } | ConvertTo-Json
        try { Invoke-RestMethod -Uri "$BaseUrl/api/riders/$r.riderId/verify" -Headers $headers -Method Post -Body $b | Out-Null; Write-Host "  $($r.username) OK"; $s++ }
        catch { Write-Host "  $($r.username) ECHEC"; $f++ }
    }
    Write-Host "Resultat : $s certifie(s), $f echec(s)" -ForegroundColor Cyan; exit 0
}
Write-Host "[2] Mode interactif" -ForegroundColor Yellow
foreach ($r in $pending) {
    Write-Host "--- $($r.username) ---" -ForegroundColor White
    Write-Host "  Nom: $($r.fullName) | Piece: $($r.idNumber) | Moto: $($r.motorcycle)" -ForegroundColor Gray
    $a = Read-Host "[V]erifier, [R]ejeter, [I]gnorer, [Q]uitter"
    switch ($a.ToUpper()) {
        "V" { $b = @{ fullName = $r.fullName; idNumber = $r.idNumber; motorcycle = $r.motorcycle } | ConvertTo-Json; try { Invoke-RestMethod -Uri "$BaseUrl/api/riders/$r.riderId/verify" -Headers $headers -Method Post -Body $b | Out-Null; Write-Host "  Certifie" -ForegroundColor Green } catch { Write-Host "  Echec : $_" -ForegroundColor Red } }
        "R" { $m = Read-Host "Motif"; $b = @{ reason = $m } | ConvertTo-Json; try { Invoke-RestMethod -Uri "$BaseUrl/api/riders/$r.riderId/reject" -Headers $headers -Method Post -Body $b | Out-Null; Write-Host "  Rejete" -ForegroundColor DarkYellow } catch { Write-Host "  Echec : $_" -ForegroundColor Red } }
        "Q" { Write-Host "Termine." -ForegroundColor Cyan; exit 0 }
        default { Write-Host "  Ignore" -ForegroundColor DarkGray }
    }
}
Write-Host "=== Termine ===" -ForegroundColor Cyan
