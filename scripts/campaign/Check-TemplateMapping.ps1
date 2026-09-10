<#
.SYNOPSIS
    Diagnostique le mapping des templates WhatChimp avant une campagne (resync / map_needed).
.DESCRIPTION
    Interroge template/list et affiche, pour les 9 templates de campagne (+ templates
    transactionnels connus), le statut Meta (Approved/Submitted) et le besoin de mapping
    WhatChimp (map_needed / variable_map). Ne fait AUCUN envoi.
.EXAMPLE
    .\Check-TemplateMapping.ps1
    # Lit WHATCHIMP_API_TOKEN (env) ou le demande ; PhoneNumberId/URL par défaut WAZAP.
.EXAMPLE
    .\Check-TemplateMapping.ps1 -SampleJson .\sample.json
    # Parse un exemple de réponse sans appeler l'API (utile pour tests / CI).
#>
param(
    [string]$ApiToken = $env:WHATCHIMP_API_TOKEN,
    [string]$PhoneNumberId = "735886129615120",
    [string]$BaseUrl = "https://app.whatchimp.com/api/v1/whatsapp/",
    [string]$SampleJson = ""
)
$ErrorActionPreference = "Stop"

$expected = @(
    "prospect_approach_v2", "prospect_followup_v2", "prospect_offer_v2",
    "rider_recruit_v2", "rider_company_v2", "rider_offer_v2",
    "vendor_onboarding_day1", "vendor_onboarding_day3", "vendor_onboarding_day7"
)

if ($SampleJson) {
    $payload = Get-Content $SampleJson -Raw | ConvertFrom-Json
} else {
    if ([string]::IsNullOrWhiteSpace($ApiToken)) {
        $ApiToken = Read-Host "WHATCHIMP_API_TOKEN (ou definir la variable d'environnement)"
    }
    $url = "${BaseUrl}template/list?apiToken=$ApiToken&phone_number_id=$PhoneNumberId"
    try { $payload = Invoke-RestMethod -Uri $url -Method Get }
    catch { Write-Error "Appel template/list impossible : $_"; exit 2 }
}

$templates = $payload.message
if (-not $templates) { Write-Error "Reponse inattendue : pas de champ 'message'."; exit 2 }

$exitCode = 0
foreach ($name in $expected) {
    $t = $templates | Where-Object { $_.name -eq $name }
    if (-not $t) {
        Write-Host "MISSING  $name (absent de la reponse API)" -ForegroundColor Red
        $exitCode = 1
        continue
    }
    $mapNeeded = $t.map_needed
    $status = $t.status
    $ok = ($status -eq "Approved") -and ($mapNeeded -eq 0 -or $mapNeeded -eq "0" -or $null -eq $mapNeeded)
    $color = if ($ok) { "Green" } else { "Yellow" }
    if (-not $ok) { $exitCode = 1 }
    Write-Host ("{0,-24} statut={1,-10} map_needed={2}" -f $name, $status, $mapNeeded) -ForegroundColor $color
}

if ($exitCode -eq 0) {
    Write-Host "`nOK : 9/9 templates approuves et mappes — campagne debloquee." -ForegroundColor Green
} else {
    Write-Host "`nACTION : dans WhatChimp, resynchroniser (Sync) puis mapper les variables" -ForegroundColor Yellow
    Write-Host "Guide : prospection/MAPPING_VARIABLES_WHATCHIMP.md" -ForegroundColor Gray
}
exit $exitCode
