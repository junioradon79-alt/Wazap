#Requires -Version 5.1
<#
.SYNOPSIS
    Heberge la video demo WAZAP (30 s) pour la campagne prospects.
.DESCRIPTION
    - Copie/encode une video source vers src/Wazap.API/wwwroot/demo.mp4 (deploiement CI).
    - Si ffmpeg est dispo : normalise en MP4 H.264 + AAC, 720p, ~35 s max, prudent pour mobile.
    - Verifie la taille (alerte > 8 Mo) et la duree (min 20 s) si ffprobe est dispo.
    - Affiche l'URL publique finale (variable {{3}} prospect) et le commit a pousser.
.PARAMETER Source
    Chemin du fichier video source (MP4/MOV/MKV/AVI...). Si vide : auto-recherche (demo|wazap).
.PARAMETER Keep
    Ne pas transcoder meme si ffmpeg est disponible (copie directe du fichier).
.EXAMPLE
    .\08-host-demo-video.ps1 -Source "C:\Users\DELL\Downloads\demo_wazap.mp4"
    .\08-host-demo-video.ps1 -Source "C:\video.mov" -Keep
#>
param(
    [string]$Source = "",
    [switch]$Keep
)
$ErrorActionPreference = "Stop"

$wwwroot = "C:\Dev\Wazap\WazapSln\src\Wazap.API\wwwroot"
$target  = Join-Path $wwwroot "demo.mp4"
$pageUrl = "https://junioradon79gm-001-site1.jtempurl.com/demo-video.html"
$publicUrl = "https://junioradon79gm-001-site1.jtempurl.com/demo.mp4"

Write-Host "=== Hebergement video demo WAZAP ===" -ForegroundColor Cyan

# 1) Localisation de la source
if ([string]::IsNullOrWhiteSpace($Source)) {
    # Auto-recherche TRES restrictive : on ne copie JAMAIS un MP4 au hasard.
    # Seuls les fichiers dont le nom contient "demo" (ou "wazap" hors sous-dossier tiktok)
    # sont candidats ; sinon on demande explicitement -Source.
    $dirs = @("C:\Users\DELL\Downloads", "C:\Users\DELL\Desktop")
    $all = @()
    foreach ($d in $dirs) {
        if (Test-Path $d) { $all += Get-ChildItem $d -Recurse -File -Include *.mp4,*.mov,*.mkv,*.avi -ErrorAction SilentlyContinue }
    }
    $cands = $all | Where-Object {
        $_.FullName -notmatch '(?i)tiktok' -and $_.Name -match '(?i)demo|wazap'
    } | Sort-Object LastModified -Descending
    if (-not $cands.Count) {
        Write-Error "Aucune video 'demo/wazap' trouvee (hors TikTok) dans Downloads/Desktop. Passez -Source <chemin>."
    }
    $Source = $cands[0].FullName
    Write-Host "[i] Source automatique : $Source" -ForegroundColor Gray
}
elseif (-not (Test-Path $Source)) { Write-Error "Source introuvable : $Source" }

Write-Host "[1] Source : $Source" -ForegroundColor Yellow

# 2) Transcodage optionnel
$ffmpeg = $null
try { $ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source } catch { }
try { $ffprobe = (Get-Command ffprobe -ErrorAction Stop).Source } catch { $ffprobe = $null }

if ($Keep -or -not $ffmpeg) {
    Write-Host "[2] Copie directe (pas de ffmpeg dispo ou -Keep)" -ForegroundColor Yellow
    Copy-Item -Force $Source $target
}
else {
    Write-Host "[2] Encodage H.264/AAC 720p (ffmpeg)..." -ForegroundColor Yellow
    & $ffmpeg -y -i $Source -vf "scale='min(1280,iw)':-2" -c:v libx264 -preset medium -crf 23 -c:a aac -b:a 96k -movflags +faststart -t 35 $target
    if ($LASTEXITCODE -ne 0) { Write-Error "ffmpeg a echoue (code $LASTEXITCODE)." }
}

# 3) Verifications
$sizeMb = [math]::Round((Get-Item $target).Length / 1MB, 2)
Write-Host "[3] Taille : $sizeMb Mo" -ForegroundColor Yellow
if ($sizeMb -gt 8) { Write-Host "     ATTENTION : depasse 8 Mo, viser < 5 Mo pour le chargement mobile." -ForegroundColor Red }

if ($ffprobe) {
    $durOut = & $ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 $target
    $dur = [double]($durOut | Select-Object -First 1)
    Write-Host "[3] Duree : $([math]::Round($dur,1)) s" -ForegroundColor Yellow
    if ($dur -lt 18) { Write-Host "     ATTENTION : moins de 20 s, viser 25-30 s pour une demo efficace." -ForegroundColor Yellow }
}

# 4) Recapitulatif
Write-Host ""
Write-Host "=== PRET A DEPLOYER ===" -ForegroundColor Green
Write-Host "  Fichier       : $target"
Write-Host "  Variable {{3}} prospect : $publicUrl"
Write-Host "  Page lecture  : $pageUrl"
Write-Host ""
Write-Host "  Commit :" -ForegroundColor Yellow
Write-Host "    git add src/Wazap.API/wwwroot/demo.mp4 && git commit -m ""Heberge la video demo 30 s"" && git push"
Write-Host ""
Write-Host "  Puis dans scripts/activation/07-prepare-campaign.ps1 :" -ForegroundColor Yellow
Write-Host "    VideoUrl = $publicUrl"
Write-Host "=== Termine ===" -ForegroundColor Cyan