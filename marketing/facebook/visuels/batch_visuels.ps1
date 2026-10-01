# Batch : genere les visuels PNG Facebook WAZAP pour une semaine donnee.
# Usage :
#   .\batch_visuels.ps1 -Semaine 1            # semaine 1 (J1-J7) -> 35 visuels
#   .\batch_visuels.ps1 -Debut 1 -Fin 7       # plage de jours
#   .\batch_visuels.ps1 -Semaine 1 -Essai 2   # 2 visuels de test seulement
#   .\batch_visuels.ps1 -Categories gen       # categorie gen seule (J1-J90) -> 90 visuels
param(
  [int]$Semaine = 0,
  [int]$Debut = 0,
  [int]$Fin = 0,
  [int]$Essai = 0,
  [string]$Categories = ''   # ex. 'gen' ou 'com,gen' (defaut : les 5 categories)
)

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$charL = [char]0x00AB
$charR = [char]0x00BB

$Root   = Split-Path $PSScriptRoot -Parent          # marketing\facebook
$Kit    = Join-Path $Root 'FACEBOOK_KIT_90JOURS.md'
$Post   = Join-Path $PSScriptRoot 'post.html'        # visuels\post.html
$Out    = Join-Path $PSScriptRoot 'generated'
New-Item -ItemType Directory -Force -Path $Out | Out-Null

# Decouvrir un navigateur headless
$Navs = @(
  'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
  'C:\Program Files\Microsoft\Edge\Application\msedge.exe',
  'C:\Program Files\Google\Chrome\Application\chrome.exe',
  'C:\Program Files (x86)\Google\Chrome\Application\chrome.exe'
)
$Nav = $Navs | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Nav) { Write-Error 'Aucun navigateur (Edge/Chrome) trouve.'; exit 1 }
Write-Host ("Navigateur : {0}" -f $Nav)

$AllCats = @('com','online','rider','biz','gen')
$Cats    = $AllCats
if ($Categories) {
  $demandees = @($Categories.Split(',') | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
  $inconnues = @($demandees | Where-Object { $AllCats -notcontains $_ })
  if ($inconnues.Count -gt 0) {
    Write-Error ('Categorie inconnue : ' + ($inconnues -join ', ') + ' (valides : ' + ($AllCats -join ', ') + ')')
    exit 1
  }
  $Cats = @($AllCats | Where-Object { $demandees -contains $_ })   # ordre canonique des colonnes
}

# Bornes des 13 semaines
$plan = @(
  @{ n=1; d1=1; d2=7 }, @{ n=2; d1=8; d2=14 }, @{ n=3; d1=15; d2=21 },
  @{ n=4; d1=22; d2=28 }, @{ n=5; d1=29; d2=35 }, @{ n=6; d1=36; d2=42 },
  @{ n=7; d1=43; d2=49 }, @{ n=8; d1=50; d2=56 }, @{ n=9; d1=57; d2=63 },
  @{ n=10; d1=64; d2=70 }, @{ n=11; d1=71; d2=77 }, @{ n=12; d1=78; d2=84 },
  @{ n=13; d1=85; d2=90 }
)

# Resoudre la plage de jours
if ($Semaine -ne 0) {
  $s = $plan | Where-Object { $_.n -eq $Semaine } | Select-Object -First 1
  if (-not $s) { Write-Error 'Semaine invalide (1..13).'; exit 1 }
  $Debut = $s.d1; $Fin = $s.d2
}
elseif ($Debut -eq 0) { $Debut = 1; $Fin = 7 }   # defaut : semaine 1
if ($Fin -eq 0) { $Fin = $Debut }
$nbJours = $Fin - $Debut + 1
$nbTotal = $nbJours * $Cats.Count
Write-Host ("Plage : J{0} a J{1} : {2} jour(s) x {3} categorie(s) = {4} visuel(s)" -f $Debut, $Fin, $nbJours, $Cats.Count, $nbTotal)

$lignes = Get-Content -LiteralPath $Kit -Encoding UTF8

# Extraire la legende d'une categorie pour un jour donne
function Get-Legende {
  param([int]$J, [int]$Col)
  $ligne = $lignes | Where-Object { $_ -match ("^\| J" + $J + " \|") } | Select-Object -First 1
  if (-not $ligne) { return '' }
  $cell   = ($ligne.Split('|') | ForEach-Object { $_.Trim() })[$Col]
  $m      = [regex]::Match($cell, [regex]::Escape([string]$charL) + '(.*?)' + [regex]::Escape([string]$charR))
  if ($m.Success) { return $m.Groups[1].Value.Trim() }
  return $cell
}

$ok = 0; $echec = 0; $total = 0

for ($j = $Debut; $j -le $Fin; $j++) {
  foreach ($cat in $Cats) {
    if ($Essai -gt 0 -and ($ok + $echec) -ge $Essai) { break }
    $total++
    $leg   = Get-Legende -J $j -Col ([array]::IndexOf($AllCats, $cat) + 3)
    if ($leg) {
      # Tronquer a ~150 caracteres pour rester lisible sur l'image
      if ($leg.Length -gt 150) { $leg = $leg.Substring(0, 147) + '...' }
      $legUrl = [System.Uri]::EscapeDataString($leg)
    } else { $legUrl = '' }

    $uri   = ([System.Uri]$Post).AbsoluteUri + '?c=' + $cat + '&j=' + $j + '&t=' + $legUrl
    $png   = Join-Path $Out (('j{0:D2}_{1}.png' -f $j, $cat))

    & $Nav '--headless' '--disable-gpu' '--hide-scrollbars' '--window-size=1080,1080' '--force-device-scale-factor=1' '--virtual-time-budget=8000' ("--screenshot=$png") $uri 2>&1 | Out-Null

    if (Test-Path -LiteralPath $png) { $ok++ }
    else { $echec++; Write-Warning ("Echec : j{0}_{1}" -f $j, $cat) }
  }
  if ($Essai -gt 0 -and ($ok + $echec) -ge $Essai) { break }
}

Write-Host ("Termine : {0}/{1} visuels generes, {2} echec(s). Dossier : {3}" -f $ok, $total, $echec, $Out)
if ($echec -gt 0) { exit 1 }