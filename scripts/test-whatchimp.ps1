<#
Utilitaire de test WhatChimp.
Lit la config dans les user-secrets (ApiToken, WebhookToken) et appsettings.json (BaseUrl, PhoneNumberId).

Usage :
  .\scripts\test-whatchimp.ps1 -Action send -ToNumber 33612345678
  .\scripts\test-whatchimp.ps1 -Action webhook-info
#>

param(
    [ValidateSet("send", "webhook-info")]
    [string]$Action = "webhook-info",
    [string]$ToNumber = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent

# Config depuis les user-secrets
$secretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\wazap-api\secrets.json"
if (-not (Test-Path $secretsPath)) { throw "User-secrets introuvable : $secretsPath" }
$secrets = Get-Content $secretsPath -Raw | ConvertFrom-Json
$apiToken = $secrets."WhatChimp:ApiToken"
$webhookToken = $secrets."WhatChimp:WebhookToken"

# Config non-secret depuis appsettings.json
$appsettings = Get-Content (Join-Path $root "src\Wazap.API\appsettings.json") -Raw | ConvertFrom-Json
$baseUrl = $appsettings.WhatChimp.BaseUrl
$phoneNumberId = $appsettings.WhatChimp.PhoneNumberId

if (-not $apiToken -or -not $phoneNumberId) {
    throw "Config WhatChimp incomplète (ApiToken / PhoneNumberId)."
}

switch ($Action) {
    "send" {
        if (-not $ToNumber) { throw "Spécifiez -ToNumber (ex: 33612345678)." }
        $message = [Uri]::EscapeDataString("Test Wazap : message de test envoyé depuis le script.")
        $url = "${baseUrl}send?apiToken=$apiToken&phone_number_id=$phoneNumberId&phone_number=$ToNumber&message_type=text&message=$message"
        Write-Host "Envoi d'un message de test à $ToNumber ..."
        $response = Invoke-RestMethod -Uri $url -Method Get
        $response | ConvertTo-Json -Depth 5
    }

    "webhook-info" {
        Write-Host "=== Configuration webhook WhatChimp ==="
        Write-Host "URL de callback : https://VOTRE-DOMAINE/api/webhook/whatsapp"
        Write-Host "Token de vérification : $webhookToken"
        Write-Host ""
        Write-Host "Renseignez ces deux valeurs dans le tableau de bord WhatChimp."
    }
}
