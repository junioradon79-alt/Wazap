#Requires -Version 5.1
<#
.SYNOPSIS
    Lance le moteur de generation video batch TikTok WAZAP (Node.js + Edge + ffmpeg).
.EXAMPLE
    .\build_tiktok_videos.ps1 -Start 1 -End 3     # Genere les 3 premieres videos
    .\build_tiktok_videos.ps1 -Id 5              # Genere uniquement la video 5
    .\build_tiktok_videos.ps1 -All               # Genere tout le catalogue (90 videos)
#>
param(
    [int]$Start = 1,
    [int]$End = 5,
    [int]$Id = 0,
    [switch]$All,
    [switch]$Force
)

$scriptMjs = Join-Path $PSScriptRoot "build_tiktok_videos.mjs"

$nodeArgs = @($scriptMjs)
if ($Id -gt 0) {
    $nodeArgs += @("--id", $Id)
} elseif ($All) {
    $nodeArgs += @("--all")
} else {
    $nodeArgs += @("--start", $Start, "--end", $End)
}

if ($Force) {
    $nodeArgs += @("--force")
}

& node @nodeArgs
