#Requires -Version 5.1
<#
.SYNOPSIS
    Prepare une campagne WhatsApp prospects (72 mobiles) une fois les templates approuves.
.EXAMPLE
    .\07-prepare-campaign.ps1 -CsvPath "prospection\Prospects_campagne_mobiles_20260902.csv" -Zone "Marcory"
#>
param(
    [string]$CsvPath = "prospection\Prospects_campagne_mobiles_20260902.csv",
    [string]$Zone = "",
    [string]$Commercial = "L'equipe WAZAP",
    [string]$VideoUrl = "https://junioradon79gm-001-site1.jtempurl.com/app/vente",
    [int]$Limit = 0,
    [switch]$DryRun
)
$ErrorActionPreference = "Stop"
Write-Host "=== Preparation Campagne WhatsApp Prospects ===" -ForegroundColor Cyan
Write-Host ""
$apiToken = $env:WHATCHIMP_API_TOKEN
if ([string]::IsNullOrWhiteSpace($apiToken)) {
    $apiToken = Read-Host "WHATCHIMP_API_TOKEN (ou definir la variable d'environnement)"
}
$baseUrl = "https://app.whatchimp.com/api/v1/whatsapp/"
$phoneNumberId = "735886129615120"
Write-Host "[1] Verification du template prospect_approach_v2..." -ForegroundColor Yellow
$templateUrl = "${baseUrl}template/list?apiToken=$apiToken&phone_number_id=$phoneNumberId"
try {
    $response = Invoke-RestMethod -Uri $templateUrl -Method Get
    $template = $response.message | Where-Object { $_.name -eq "prospect_approach_v2" }
    if (-not $template) {
        Write-Host "      Template 'prospect_approach_v2' non trouve !" -ForegroundColor Red
        Write-Host "      Verifiez que le template est approuve dans WhatsApp Manager." -ForegroundColor Yellow
        exit 1
    }
    Write-Host "      Statut : $($template.status)" -ForegroundColor $(if ($template.status -eq "Approved") { "Green" } else { "Yellow" })
    if ($template.status -ne "Approved") {
        Write-Host "      Le template n'est pas encore approuve. Attendez l'approbation Meta." -ForegroundColor Yellow
        exit 1
    }
}
catch {
    Write-Error "Erreur lors de la verification du template : $_"
    exit 1
}
Write-Host ""
Write-Host "[2] Preparation de la campagne..." -ForegroundColor Yellow
$projectRoot = "C:\Dev\Wazap\WazapSln"
$campaignArgs = @(
    "--project", "tools\WhatsAppCampaign",
    $CsvPath,
    "--zone=$Zone",
    "--commercial=$Commercial",
    "--video-url=$VideoUrl"
)
if ($Limit -gt 0) { $campaignArgs += "--limit=$Limit" }
if ($DryRun) { $campaignArgs += "--dry-run" }
Write-Host "      CSV : $CsvPath" -ForegroundColor Gray
Write-Host "      Zone : $(if ($Zone) { $Zone } else { 'Toutes' })" -ForegroundColor Gray
Write-Host "      Commercial : $Commercial" -ForegroundColor Gray
Write-Host "      Video : $VideoUrl" -ForegroundColor Gray
Write-Host ""
if (-not $DryRun) {
    Write-Host "[3] Lancement de la campagne..." -ForegroundColor Yellow
    try {
        Push-Location $projectRoot
        $env:WHATCHIMP_API_TOKEN = $apiToken
        dotnet run @campaignArgs 2>&1 | Out-Null
        Write-Host "      Campagne terminee !" -ForegroundColor Green
    }
    catch {
        Write-Error "Erreur lors de la campagne : $_"
        exit 1
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "      [DRY-RUN] Campagne simulee" -ForegroundColor DarkYellow
}
Write-Host ""
Write-Host "=== Preparation terminee ===" -ForegroundColor Cyan
