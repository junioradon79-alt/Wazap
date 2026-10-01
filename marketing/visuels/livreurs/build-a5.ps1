#Requires -Version 5.1
<#
.SYNOPSIS
    Genere l'affiche A5 "recrutement livreurs" : QR code (hors ligne) + PDF A5 + PNG ~300 dpi.
.NOTES
    Script volontairement ASCII (PowerShell 5.1 lit les .ps1 sans BOM en ANSI).
.EXAMPLE
    ./build-a5.ps1
    ./build-a5.ps1 -SkipQr
#>
[CmdletBinding()]
param(
    [string]$Html  = (Join-Path $PSScriptRoot 'A5_recrutement_livreur.html'),
    [switch]$SkipQr,
    [string]$WaUrl = 'https://junioradon79gm-001-site1.jtempurl.com/devenir-livreur'
)
$ErrorActionPreference = 'Stop'

# 1) QR code hors ligne (app mono-fichier .NET 10 + QRCoder 1.8.0 - meme lib que l'API)
if (-not $SkipQr) {
    Push-Location $PSScriptRoot
    try {
        & dotnet run gen-qr.cs -- $WaUrl qr-livreur.png 1000
        if ($LASTEXITCODE -ne 0) { throw 'Generation du QR en echec.' }
    } finally { Pop-Location }
}

# 2) Navigateur headless (Edge puis Chrome)
$navs = @(
    'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
    'C:\Program Files\Microsoft\Edge\Application\msedge.exe',
    'C:\Program Files\Google\Chrome\Application\chrome.exe',
    'C:\Program Files (x86)\Google\Chrome\Application\chrome.exe'
)
$nav = $navs | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $nav) { throw 'Aucun navigateur (Edge/Chrome) trouve.' }
Write-Host ("Navigateur : {0}" -f $nav)

$htmlPath = (Resolve-Path -LiteralPath $Html).Path
$uri = ([System.Uri]$htmlPath).AbsoluteUri
$pdf = Join-Path $PSScriptRoot 'A5_recrutement_livreur.pdf'
$png = Join-Path $PSScriptRoot 'A5_recrutement_livreur.png'

# Rendu headless : supprime la sortie au prealable, attend l'ecriture (Edge rend en asynchrone),
# et reessaie une fois si besoin.
function Invoke-Headless {
    param([string[]]$Extra, [string]$Out)
    for ($try = 1; $try -le 2; $try++) {
        if (Test-Path -LiteralPath $Out) { Remove-Item -LiteralPath $Out -Force -ErrorAction SilentlyContinue }
        $ud = Join-Path $env:TEMP ('wazap-a5-' + [guid]::NewGuid().ToString('N'))
        $common = @('--headless', '--disable-gpu', '--no-sandbox', '--no-first-run', "--user-data-dir=$ud")
        $null = & $nav @common @Extra 2>$null
        Remove-Item $ud -Recurse -Force -ErrorAction SilentlyContinue

        $deadline = (Get-Date).AddSeconds(15)
        while ((Get-Date) -lt $deadline) {
            if (Test-Path -LiteralPath $Out) { Start-Sleep -Milliseconds 400; return $true }
            Start-Sleep -Milliseconds 250
        }
    }
    return $false
}

# 3) PDF A5 (respecte @page size:A5 ; sans en-tete ni pied de page)
$okPdf = Invoke-Headless -Out $pdf -Extra @(
    '--no-pdf-header-footer', '--print-to-pdf-no-header', '--virtual-time-budget=8000',
    "--print-to-pdf=$pdf", $uri)

# 4) PNG ~300 dpi (A5 = 148x210 mm ; 148mm ~ 560 px CSS ; x3.125 -> ~1750 px)
$okPng = Invoke-Headless -Out $png -Extra @(
    '--hide-scrollbars', '--force-device-scale-factor=3.125', '--window-size=560,794',
    '--virtual-time-budget=8000', "--screenshot=$png", "$uri#export")

$fail = 0
foreach ($pair in @(@{ ok = $okPdf; f = $pdf }, @{ ok = $okPng; f = $png })) {
    if ($pair.ok -and (Test-Path -LiteralPath $pair.f)) {
        Write-Host ("OK  {0}  ({1:N0} o)" -f (Split-Path $pair.f -Leaf), (Get-Item $pair.f).Length)
    } else {
        Write-Warning ("Echec : {0}" -f (Split-Path $pair.f -Leaf)); $fail++
    }
}
if ($fail -gt 0) { exit 1 }
