#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$htmlPath = Join-Path $PSScriptRoot 'render_dignite_motard.html'
$outPng = Join-Path $PSScriptRoot 'generated\visuel_dignite_motard_comparatif.png'
$outRootPng = Join-Path (Split-Path $PSScriptRoot -Parent) 'visuel_dignite_motard_comparatif.png'

New-Item -ItemType Directory -Force -Path (Split-Path $outPng) | Out-Null

$navs = @(
    'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
    'C:\Program Files\Microsoft\Edge\Application\msedge.exe',
    'C:\Program Files\Google\Chrome\Application\chrome.exe',
    'C:\Program Files (x86)\Google\Chrome\Application\chrome.exe'
)
$nav = $navs | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $nav) { throw "Aucun navigateur headless trouvé." }

$ud = Join-Path $env:TEMP ('wazap-dignite-' + [guid]::NewGuid().ToString('N'))
$uri = ([System.Uri]$htmlPath).AbsoluteUri

$args = @(
    '--headless', '--disable-gpu', '--no-sandbox', '--no-first-run', '--hide-scrollbars',
    "--user-data-dir=$ud", '--window-size=1080,1080',
    "--force-device-scale-factor=2", '--virtual-time-budget=9000',
    "--screenshot=$outPng", $uri
)

Write-Host "Rendu du visuel en 2160x2160 via $nav..."
& $nav @args 2>$null
Remove-Item $ud -Recurse -Force -ErrorAction SilentlyContinue

$deadline = (Get-Date).AddSeconds(15)
while ((Get-Date) -lt $deadline) {
    if (Test-Path -LiteralPath $outPng) {
        Start-Sleep -Milliseconds 500
        Copy-Item $outPng $outRootPng -Force
        Write-Host "✅ Visuel généré avec succès : $outPng"
        Write-Host "✅ Copie principale : $outRootPng"
        exit 0
    }
    Start-Sleep -Milliseconds 300
}

throw "Le screenshot n'a pas été produit à temps."
