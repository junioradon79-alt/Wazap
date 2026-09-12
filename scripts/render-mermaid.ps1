#Requires -Version 5.1
<#
.SYNOPSIS
    Renders the Mermaid diagrams of a Markdown file to SVG and PNG images (via Kroki),
    writes the .mmd sources, and can embed the images into the document
    (the Mermaid source is then kept inside a collapsible <details> block).
.NOTES
    Kept ASCII-only on purpose: Windows PowerShell 5.1 reads BOM-less .ps1 files as ANSI,
    which would corrupt any accented literal (transliteration table, messages, ...).
.EXAMPLE
    ./scripts/render-mermaid.ps1
    ./scripts/render-mermaid.ps1 -Embed
#>
[CmdletBinding()]
param(
    [string]$MarkdownPath = (Join-Path $PSScriptRoot '..\docs\parcours-client.md'),
    [string]$OutputDir    = (Join-Path $PSScriptRoot '..\docs\diagrams'),
    [string[]]$Formats    = @('svg', 'png'),
    [string]$KrokiBaseUrl = 'https://kroki.io',
    [switch]$Embed
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

# Canonical decomposition + removal of diacritics => safe file names (no accented literal).
function Get-Slug([string]$Text) {
    $t = $Text.Normalize([System.Text.NormalizationForm]::FormD)
    $t = [regex]::Replace($t, '\p{Mn}', '')
    $t = $t.Replace("'", '').Replace([string][char]0x2019, '')
    $t = $t.ToLowerInvariant()
    $t = [regex]::Replace($t, '[^a-z0-9]+', '-').Trim('-')
    if ($t.Length -gt 48) { $t = $t.Substring(0, 48).Trim('-') }
    return $t
}

# Renders a Mermaid source through the Kroki service (SVG or PNG).
function Invoke-Kroki([string]$Source, [string]$Format) {
    $client = New-Object System.Net.Http.HttpClient
    $client.Timeout = [TimeSpan]::FromSeconds(90)
    try {
        $content = New-Object System.Net.Http.StringContent($Source, [System.Text.Encoding]::UTF8, 'text/plain')
        $resp = $client.PostAsync("$KrokiBaseUrl/mermaid/$Format", $content).GetAwaiter().GetResult()
        if (-not $resp.IsSuccessStatusCode) {
            throw "Kroki $Format -> HTTP $([int]$resp.StatusCode)"
        }
        return $resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    }
    finally { $client.Dispose() }
}

$lines = [IO.File]::ReadAllLines($MarkdownPath)
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# 1) Extract ```mermaid blocks (the name comes from the preceding ## heading).
$blocks = New-Object System.Collections.Generic.List[object]
$currentHeading = 'diagram'
$i = 0
while ($i -lt $lines.Count) {
    $line = $lines[$i]
    if ($line -match '^##\s+(.+)$') {
        $currentHeading = ($Matches[1].Trim() -replace '^\d+\.\s*', '')
    }
    elseif ($line.Trim() -eq '```mermaid') {
        $buf = New-Object System.Collections.Generic.List[string]
        $j = $i + 1
        while ($j -lt $lines.Count -and $lines[$j].Trim() -ne '```') { $buf.Add($lines[$j]); $j++ }
        $name = ('{0:D2}-{1}' -f ($blocks.Count + 1), (Get-Slug $currentHeading))
        $blocks.Add([pscustomobject]@{ Name = $name; Source = ($buf -join "`n") })
        $i = $j
    }
    $i++
}

Write-Host "Diagrams found: $($blocks.Count)" -ForegroundColor Cyan

# 2) Render each block (+ write the .mmd source).
foreach ($b in $blocks) {
    [IO.File]::WriteAllText((Join-Path $OutputDir "$($b.Name).mmd"), $b.Source,
        (New-Object System.Text.UTF8Encoding($false)))
    foreach ($fmt in $Formats) {
        try {
            $bytes = Invoke-Kroki $b.Source $fmt
            [IO.File]::WriteAllBytes((Join-Path $OutputDir "$($b.Name).$fmt"), $bytes)
            Write-Host ("  OK  {0,-58} {1,8} o" -f "$($b.Name).$fmt", $bytes.Length) -ForegroundColor Green
        }
        catch {
            Write-Host ("  ERR {0} : {1}" -f "$($b.Name).$fmt", $_.Exception.Message) -ForegroundColor Red
        }
    }
}

# 3) Optional embedding: image + collapsible Mermaid source.
if ($Embed) {
    $sb = New-Object System.Text.StringBuilder
    $i = 0; $n = 0
    $lastNonEmpty = ''
    while ($i -lt $lines.Count) {
        $line = $lines[$i]
        if ($line.Trim() -eq '```mermaid') {
            $buf = New-Object System.Collections.Generic.List[string]
            $j = $i + 1
            while ($j -lt $lines.Count -and $lines[$j].Trim() -ne '```') { $buf.Add($lines[$j]); $j++ }
            $b = $blocks[$n]
            # Idempotence : un bloc deja precede du <details> n'est pas re-emballe.
            $already = ($lastNonEmpty -eq '<details><summary>Source Mermaid</summary>')
            if (-not $already) {
                [void]$sb.AppendLine("![$($b.Name)](diagrams/$($b.Name).svg)")
                [void]$sb.AppendLine()
                [void]$sb.AppendLine('<details><summary>Source Mermaid</summary>')
                [void]$sb.AppendLine()
            }
            [void]$sb.AppendLine('```mermaid')
            foreach ($s in $buf) { [void]$sb.AppendLine($s) }
            [void]$sb.AppendLine('```')
            if (-not $already) {
                [void]$sb.AppendLine()
                [void]$sb.AppendLine('</details>')
            }
            $lastNonEmpty = '```'
            $n++; $i = $j + 1
        }
        else {
            [void]$sb.AppendLine($line)
            if ($line.Trim().Length -gt 0) { $lastNonEmpty = $line.Trim() }
            $i++
        }
    }
    [IO.File]::WriteAllText($MarkdownPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Document updated (images + collapsible source): $MarkdownPath" -ForegroundColor Cyan
}

