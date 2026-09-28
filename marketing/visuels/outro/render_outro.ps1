# Render Outro Vidéo WAZAP Officielle (1080x1920 - 4 secondes à 30fps)
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$logoPath = (Resolve-Path (Join-Path $scriptDir "..\logo_officiel_transparent.png")).Path -replace '\\', '/'
$outputPath = Join-Path $scriptDir "outro_officielle_wazap_9_16.mp4"
$fontBold = "C\:/Windows/Fonts/arialbd.ttf"
$fontRegular = "C\:/Windows/Fonts/arial.ttf"

Write-Host "Rendering WAZAP Official Outro to $outputPath..." -ForegroundColor Cyan

# Résolution 1080x1920, 4 secondes, H.264 / AAC
# Animation :
# 1. Fond sombre dégradé émeraude / obsidian
# 2. Logo officiel avec fondu et zoom
# 3. Textes officiels nets incrustés

$filter = "[1:v]scale=550:550,format=rgba,fade=t=in:st=0.2:d=0.5:alpha=1[logo];" + `
"[0:v][logo]overlay=x=(W-w)/2:y=(H-h)/2-180:shortest=1[bg_logo];" + `
"[bg_logo]drawtext=text='WAZAP':fontcolor=white:fontsize=80:x=(w-text_w)/2:y=1120:fontfile='$fontBold'," + `
"drawtext=text='La livraison en direct a Abidjan':fontcolor=0x00FF9D:fontsize=36:x=(w-text_w)/2:y=1220:fontfile='$fontBold'," + `
"drawtext=text='WhatsApp - 05 44 05 19 72':fontcolor=white:fontsize=48:x=(w-text_w)/2:y=1380:fontfile='$fontBold':box=1:boxcolor=0x075E54@0.9:boxborderw=20," + `
"drawtext=text='LIVREURS - Envoie DISPO  |  COMMERCANTS - Envoie COLIS':fontcolor=0xFFD700:fontsize=30:x=(w-text_w)/2:y=1520:fontfile='$fontBold'," + `
"drawtext=text='www.wazap.ci':fontcolor=0x94A3B8:fontsize=26:x=(w-text_w)/2:y=1680:fontfile='$fontRegular'," + `
"fade=t=out:st=3.7:d=0.3[v]"

$ffmpegArgs = @(
    "-y",
    "-f", "lavfi",
    "-i", "color=c=0x081119:s=1080x1920:d=4,format=yuv420p",
    "-loop", "1",
    "-i", $logoPath,
    "-filter_complex", $filter,
    "-map", "[v]",
    "-t", "4",
    "-r", "30",
    "-c:v", "libx264",
    "-pix_fmt", "yuv420p",
    "-preset", "fast",
    $outputPath
)

& ffmpeg @ffmpegArgs

if (Test-Path $outputPath) {
    $size = (Get-Item $outputPath).Length / 1KB
    Write-Host "Success! Outro rendered: $outputPath ($([math]::Round($size, 1)) KB)" -ForegroundColor Green
} else {
    Write-Error "Failed to generate outro video."
}
