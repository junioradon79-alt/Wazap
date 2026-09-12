#Requires -Version 5.1
<#
.SYNOPSIS
    Genere les visuels Facebook "recrutement livreurs" (1 visuel/jour, teasing)
    a partir de post.html + serie.js, en PNG carre 1080x1080 (x facteur d'echelle).
.NOTES
    Script volontairement ASCII (PowerShell 5.1 lit les .ps1 sans BOM en ANSI).
.EXAMPLE
    ./gen-visuels.ps1                 # les 7 jours
    ./gen-visuels.ps1 -Debut 1 -Fin 3 # seulement J1 a J3
    ./gen-visuels.ps1 -Scale 1        # PNG strictement 1080x1080
#>
[CmdletBinding()]
param(
    [int[]]$Jours,
    [int]$Debut = 1,
    [int]$Fin = 7,
    [int]$Scale = 2,
    [string]$Out = (Join-Path $PSScriptRoot 'generated')
)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$html = Join-Path $PSScriptRoot 'post.html'
if (-not (Test-Path -LiteralPath $html)) { throw "Gabarit introuvable : $html" }
New-Item -ItemType Directory -Force -Path $Out | Out-Null

# Navigateur headless (Edge puis Chrome)
$navs = @(
    'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
    'C:\Program Files\Microsoft\Edge\Application\msedge.exe',
    'C:\Program Files\Google\Chrome\Application\chrome.exe',
    'C:\Program Files (x86)\Google\Chrome\Application\chrome.exe'
)
$nav = $navs | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $nav) { throw 'Aucun navigateur (Edge/Chrome) trouve.' }
Write-Host ("Navigateur : {0}" -f $nav)

$uri = ([System.Uri]$html).AbsoluteUri
$size = [int](1080 * $Scale)
try { Add-Type -AssemblyName System.Drawing } catch {}

# Rendu headless : supprime la sortie, attend l'ecriture (Edge rend en asynchrone), reessaie.
function Invoke-Shot {
    param([string]$Url, [string]$Png)
    for ($try = 1; $try -le 2; $try++) {
        if (Test-Path -LiteralPath $Png) { Remove-Item -LiteralPath $Png -Force -ErrorAction SilentlyContinue }
        $ud = Join-Path $env:TEMP ('wazap-fb-' + [guid]::NewGuid().ToString('N'))
        $args = @(
            '--headless', '--disable-gpu', '--no-sandbox', '--no-first-run', '--hide-scrollbars',
            "--user-data-dir=$ud", '--window-size=1080,1080',
            "--force-device-scale-factor=$Scale", '--virtual-time-budget=9000',
            "--screenshot=$Png", $Url
        )
        $null = & $nav @args 2>$null
        Remove-Item $ud -Recurse -Force -ErrorAction SilentlyContinue

        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Date) -lt $deadline) {
            if (Test-Path -LiteralPath $Png) { Start-Sleep -Milliseconds 500; return $true }
            Start-Sleep -Milliseconds 250
        }
    }
    return $false
}

# Plage de jours
if ($Jours -and $Jours.Count -gt 0) { $liste = $Jours }
else { $liste = $Debut..$Fin }

Write-Host ("Visuels a produire : {0} (J{1})" -f $liste.Count, ($liste -join ',J'))

$ok = 0; $echec = 0
foreach ($j in $liste) {
    $png = Join-Path $Out ('fb_livreurs_j{0:D2}.png' -f $j)
    $url = $uri + '?j=' + $j

    if (Invoke-Shot -Url $url -Png $png) {
        $img = [System.Drawing.Image]::FromFile($png)
        $w = $img.Width; $h = $img.Height; $img.Dispose()
        Write-Host ("OK  {0}  {1}x{2}  ({3:N0} o)" -f (Split-Path $png -Leaf), $w, $h, (Get-Item $png).Length)
        $ok++
    } else {
        Write-Warning ("Echec : {0}" -f (Split-Path $png -Leaf)); $echec++
    }
}

Write-Host ("Termine : {0} visuel(s) OK, {1} echec(s). Dossier : {2}" -f $ok, $echec, $Out)
if ($echec -gt 0) { exit 1 }
