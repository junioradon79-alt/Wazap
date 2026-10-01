<#
.SYNOPSIS
  Extrait les posts Facebook WAZAP (Jour ou Semaine complete) du kit 90 jours.

.DESCRIPTION
  Lit FACEBOOK_KIT_90JOURS.md et affiche, pour chaque jour et chaque categorie
  (5 categories), la legende prete a copier, la commande visuel, les hashtags
  et le commentaire epingle.

.PARAMETER Jour
  Numero du jour (1..90). Peut etre calcule automatiquement via -Lancement.

.PARAMETER Semaine
  Numero de semaine (1..13) : affiche TOUS les jours de la semaine
  (7 jours, 6 pour la semaine 13 : J85-J90) = jusqu'a 35 blocs.

.PARAMETER Lancement
  Date de lancement (format yyyy-MM-dd). Calcule le jour courant.

.PARAMETER OuvrirVisuels
  Ouvre les visuels (jour ou semaine) dans le navigateur.

.PARAMETER PressePapier
  Copie le bloc prete a poster dans le presse-papiers.

.PARAMETER Sortie
  Chemin de fichier : y ecrit aussi le bloc (UTF-8).

.EXAMPLE
  .\jour.ps1 -Jour 7
  .\jour.ps1 -Semaine 1
  .\jour.ps1 -Lancement 2026-09-09
  .\jour.ps1 -Semaine 1 -OuvrirVisuels -PressePapier -Sortie .\semaine1.txt
#>
param(
  [int]$Jour = 0,
  [int]$Semaine = 0,
  [datetime]$Lancement,
  [switch]$OuvrirVisuels,
  [switch]$PressePapier,
  [string]$Sortie
)

# --- UTF-8 (a definir AVANT toute sortie) ---
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$charL = [char]0x00AB   # guillemet ouvrant '<<'
$charR = [char]0x00BB   # guillemet fermant '>>'

$Kit = Join-Path $PSScriptRoot 'FACEBOOK_KIT_90JOURS.md'
if (-not (Test-Path -LiteralPath $Kit)) { Write-Error "Kit introuvable : $Kit"; exit 1 }

$Cats = @(
  @{ code='com';    label='COMMERCANTS & RESTAURANTS DE QUARTIER'; hashtags='#Abidjan #Commercant #Restaurant #Livraison #WAZAP'; ping="C'est pour quel quartier ? Dites-le en commentaire et on vous explique comment activer vos 15 commandes offertes" },
  @{ code='online'; label='VENDEURS EN LIGNE / BOUTIQUES WHATSAPP';   hashtags='#Ecommerce #BoutiqueEnLigne #VenteEnLigne #Abidjan #WAZAP'; ping="C'est pour quel quartier ? Dites-le en commentaire et on vous explique comment activer vos 15 commandes offertes" },
  @{ code='rider';  label='LIVREURS & COURSIERS';                     hashtags='#Livreur #Coursier #Job #Abidjan #WAZAP'; ping='Dis "je veux livrer" en commentaire ou en DM et on s''occupe du reste' },
  @{ code='biz';    label='ENTREPRENEURS & INDEPENDANTS';            hashtags='#Entrepreneur #BusinessCI #StartUp #Abidjan #WAZAP'; ping="C'est pour quel quartier ? Dites-le en commentaire et on vous explique comment activer vos 15 commandes offertes" },
  @{ code='gen';    label='GENERAL / ANNONCES / VIE LOCALE';          hashtags='#Abidjan #Livraison #Quartier #Commande #WAZAP'; ping='Ton quartier est-il couvert ? Dis-nous ou tu es, on repond en DM' }
)

$lignes = Get-Content -LiteralPath $Kit -Encoding UTF8

# --- Bornes des 13 semaines ---
$plan = @(
  @{ n=1;  d1=1;  d2=7  }, @{ n=2; d1=8;  d2=14 },
  @{ n=3;  d1=15; d2=21 }, @{ n=4; d1=22; d2=28 },
  @{ n=5;  d1=29; d2=35 }, @{ n=6; d1=36; d2=42 },
  @{ n=7;  d1=43; d2=49 }, @{ n=8; d1=50; d2=56 },
  @{ n=9;  d1=57; d2=63 }, @{ n=10; d1=64; d2=70 },
  @{ n=11; d1=71; d2=77 }, @{ n=12; d1=78; d2=84 },
  @{ n=13; d1=85; d2=90 }
)

# ============================================================
# Extraire le bloc d'UN jour (J1..J90)
# ============================================================
function Get-Jour {
  param([int]$J)

  $ligne = $lignes | Where-Object { $_ -match ("^\| J" + $J + " \|") } | Select-Object -First 1
  if (-not $ligne) { Write-Warning "Jour J$J introuvable dans le kit."; return $null }

  # Titre de la semaine contenant le jour
  $titreSemaine = ''
  foreach ($l in $lignes) {
    if ($l -match '^### Semaine .*\(J(\d+)-J(\d+)\)') {
      $a = [int]$Matches[1]; $b = [int]$Matches[2]
      if ($J -ge $a -and $J -le $b) { $titreSemaine = ($l.Trim() -replace '^###\s*',''); break }
    }
  }

  # Decouper : | J | Pilier | com | online | rider | biz | gen |
  $champs = $ligne.Split('|') | ForEach-Object { $_.Trim() }
  $pilier = $champs[2]

  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine('======================================================')
  [void]$sb.AppendLine(("JOUR J{0} : {1}" -f $J, $pilier))
  if ($titreSemaine) { [void]$sb.AppendLine(("  Semaine : {0}" -f $titreSemaine)) }
  [void]$sb.AppendLine('======================================================')
  [void]$sb.AppendLine('')

  for ($i = 0; $i -lt 5; $i++) {
    $cell = $champs[$i + 3]
    $mL = [regex]::Match($cell, [regex]::Escape([string]$charL) + '(.*?)' + [regex]::Escape([string]$charR))
    $legende = if ($mL.Success) { $mL.Groups[1].Value.Trim() } else { $cell }
    $mC = [regex]::Match($cell, '([a-z]+-j\d+)\s*$')
    $code = if ($mC.Success) { $mC.Groups[1].Value } else { $Cats[$i].code + '-j' + $J }
    $cat  = $Cats[$i]
    $url  = 'visuels/post.html?c=' + $cat.code + '&j=' + $J

    [void]$sb.AppendLine(("### [{0}] {1}" -f $cat.code, $cat.label))
    [void]$sb.AppendLine('LEGENDE (copier-coller) :')
    [void]$sb.AppendLine('  ' + $legende)
    [void]$sb.AppendLine('VISUEL : ' + $url)
    [void]$sb.AppendLine('HASHTAGS : ' + $cat.hashtags)
    [void]$sb.AppendLine('COMMENTAIRE EPINGLE : ' + $cat.ping)
    [void]$sb.AppendLine('')

    if ($OuvrirVisuels) {
      try {
        $fichier = Join-Path $PSScriptRoot 'visuels\post.html'
        $uri     = ([System.Uri]$fichier).AbsoluteUri + '?c=' + $cat.code + '&j=' + $J
        Start-Process $uri
      } catch { Write-Warning "Ouverture visuel impossible : $url" }
    }
  }

  return $sb.ToString()
}

# ============================================================
# Choix de l'intervalle : Semaine ou Jour
# ============================================================
$sbGlobal = New-Object System.Text.StringBuilder
$entete = ''

if ($Semaine -ne 0) {
  $s = $plan | Where-Object { $_.n -eq $Semaine } | Select-Object -First 1
  if (-not $s) { Write-Error "Semaine invalide (1..13)."; exit 1 }
  for ($j = $s.d1; $j -le $s.d2; $j++) {
    $bloc = Get-Jour -J $j
    if ($bloc) { [void]$sbGlobal.AppendLine($bloc) }
  }
  $entete = ("SEMAINE {0} : J{1} a J{2}" -f $Semaine, $s.d1, $s.d2)
}
else {
  # Jour : resolution (si 0, depuis Lancement)
  if ($Jour -le 0) {
    if (-not $Lancement) { Write-Error "Indiquez -Jour, -Semaine ou -Lancement 'aaaa-MM-jj'."; exit 1 }
    $diff = [int]((Get-Date).Date - $Lancement.Date).TotalDays
    $Jour = [Math]::Max(1, [Math]::Min(90, $diff + 1))
  }
  if ($Jour -lt 1 -or $Jour -gt 90) { Write-Error "Jour invalide (1..90)."; exit 1 }
  $bloc = Get-Jour -J $Jour
  if (-not $bloc) { exit 1 }
  [void]$sbGlobal.AppendLine($bloc)
  $entete = ("JOUR J{0}" -f $Jour)
}

# --- Rappels communs ---
[void]$sbGlobal.AppendLine('------------------------------------------------------')
[void]$sbGlobal.AppendLine('Rappels : 5 categories/jour max ; rotation des groupes ; repondre aux commentaires < 48 h ; QR trackes = leads dans /app/leads.')

$blocFinal = "========== $entete ==========`n" + $sbGlobal.ToString()

# --- Affichage ---
[Console]::WriteLine($blocFinal)

if ($PressePapier) {
  try { Set-Clipboard -Value $blocFinal; Write-Host 'OK : bloc copie dans le presse-papiers.' }
  catch { Write-Warning 'Copie presse-papier indisponible.' }
}

if ($Sortie) {
  if (-not (Split-Path -Parent $Sortie)) { $Sortie = Join-Path $PSScriptRoot $Sortie }
  [System.IO.File]::WriteAllText($Sortie, $blocFinal, (New-Object System.Text.UTF8Encoding($false)))
  Write-Host "OK : bloc enregistre dans $Sortie"
}