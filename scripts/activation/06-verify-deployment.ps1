#Requires -Version 5.1
<#
.SYNOPSIS
    Verifie l'etat du deploiement en production.
.EXAMPLE
    .\06-verify-deployment.ps1
#>
param(
    [string]$BaseUrl = "https://junioradon79gm-001-site1.jtempurl.com"
)
$ErrorActionPreference = "SilentlyContinue"
Write-Host "=== Verification du deploiement ===" -ForegroundColor Cyan
Write-Host ""
$endpoints = @(
    @{ Name = "Health"; Url = "$BaseUrl/health"; Expected = 200 },
    @{ Name = "Health Details"; Url = "$BaseUrl/health/details"; Expected = 200 },
    @{ Name = "Metrics"; Url = "$BaseUrl/metrics"; Expected = 200 },
    @{ Name = "API v1 Overview"; Url = "$BaseUrl/api/v1/overview"; Expected = 401 },
    @{ Name = "Swagger"; Url = "$BaseUrl/swagger"; Expected = 200 }
)
foreach ($ep in $endpoints) {
    try {
        $r = Invoke-WebRequest -Uri $ep.Url -UseBasicParsing -TimeoutSec 10
        $status = $r.StatusCode
        $color = if ($status -eq $ep.Expected) { "Green" } else { "Yellow" }
        Write-Host "  [$status] $($ep.Name)" -ForegroundColor $color
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if (-not $status) { $status = "ERR" }
        Write-Host "  [$status] $($ep.Name)" -ForegroundColor Red
    }
}
Write-Host ""
Write-Host "=== Verification terminee ===" -ForegroundColor Cyan
