$nav = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
$html = (Resolve-Path 'A5_flyer_officiel.html').Path
$uri = ([System.Uri]$html).AbsoluteUri

$outPng = Join-Path $PSScriptRoot 'A5_flyer_livreurs_redmi_15c_rendered.png'
$outJpg = Join-Path $PSScriptRoot 'A5_flyer_livreurs_redmi_15c.jpg'
$outPdf = Join-Path $PSScriptRoot 'A5_flyer_livreurs_redmi_15c.pdf'

if (Test-Path $outPng) { Remove-Item $outPng -Force }
if (Test-Path $outPdf) { Remove-Item $outPdf -Force }

$ud = Join-Path $env:TEMP ('wazap-a5-' + [guid]::NewGuid().ToString('N'))
$argsPng = @(
    '--headless',
    '--disable-gpu',
    '--no-sandbox',
    '--no-first-run',
    "--user-data-dir=$ud",
    '--hide-scrollbars',
    '--window-size=896,1200',
    '--virtual-time-budget=6000',
    "--screenshot=$outPng",
    $uri
)

$null = & $nav @argsPng
Remove-Item $ud -Recurse -Force -ErrorAction SilentlyContinue

$deadline = (Get-Date).AddSeconds(15)
while ((Get-Date) -lt $deadline) {
    if (Test-Path $outPng) {
        Start-Sleep -Milliseconds 500
        Write-Host "OK PNG: $((Get-Item $outPng).Length) bytes"
        break
    }
    Start-Sleep -Milliseconds 300
}

# Convert / copy to JPG as well
Copy-Item $outPng $outJpg -Force
Write-Host "OK JPG: $((Get-Item $outJpg).Length) bytes"

# Also render PDF
$ud2 = Join-Path $env:TEMP ('wazap-a5-pdf-' + [guid]::NewGuid().ToString('N'))
$argsPdf = @(
    '--headless',
    '--disable-gpu',
    '--no-sandbox',
    '--no-first-run',
    "--user-data-dir=$ud2",
    '--no-pdf-header-footer',
    '--print-to-pdf-no-header',
    '--virtual-time-budget=6000',
    "--print-to-pdf=$outPdf",
    $uri
)
$null = & $nav @argsPdf
Remove-Item $ud2 -Recurse -Force -ErrorAction SilentlyContinue

if (Test-Path $outPdf) {
    Write-Host "OK PDF: $((Get-Item $outPdf).Length) bytes"
}

# Sync to wwwroot
$wwwroot = Join-Path $PSScriptRoot '..\..\..\src\Wazap.API\wwwroot'
if (Test-Path $wwwroot) {
    Copy-Item $outJpg (Join-Path $wwwroot 'flyer-livreurs.jpg') -Force
    Copy-Item $outPng (Join-Path $wwwroot 'flyer-livreurs.png') -Force
    if (Test-Path $outPdf) {
        Copy-Item $outPdf (Join-Path $wwwroot 'flyer-livreurs.pdf') -Force
    }
    Write-Host "OK synced to wwwroot"
}
