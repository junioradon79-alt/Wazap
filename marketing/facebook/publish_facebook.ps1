#Requires -Version 5.1
<#
.SYNOPSIS
    Publie le kit Facebook 90 jours WAZAP sur la Page <WAZAP Cote d'Ivoire> via l'API Graph.
.DESCRIPTION
    Chaine complete, reproductible et idempotente :
      1. lit le manifeste post_manifest_gen.json (90 jours : message, commentaire epingle, image) ;
      2. envoie l'image en MULTIPART (POST /{page_id}/photos) : aucune URL publique n'est requise,
         donc aucune dependance a un hebergement externe (le dossier gh_images_staging/ devient
         inutile pour la publication) ;
      3. poste ensuite le commentaire epingle du jour (POST /{post_id}/comments).

    DRY-RUN PAR DEFAUT : rien n'est publie sans -Publier.
    Etat local : published_state.json (jours deja publies) -> relance idempotente.

    Contexte : MEMOIRE.md (chantier 8e), marketing/facebook/FACEBOOK_KIT_90JOURS.md,
    marketing/POLITIQUE_COMMERCIALE_ET_POSITIONNEMENT.md (formulations commerciales).
.PARAMETER Action
    preflight : verifie le jeton, la Page et le prochain jour a publier (aucune ecriture). Defaut.
    next      : traite le prochain jour NON publie (simulation, ou publication avec -Publier).
    publish   : traite un jour precis (-Jour n).
    status    : liste les publications de la Page et signale les doublons (lecture seule).
    remove    : supprime une publication (-PostId id) : sert a corriger un doublon.
.PARAMETER Jour
    Numero de jour du kit (1..90). Defaut : prochain jour non publie.
.PARAMETER Publier
    Execute reellement l'ecriture (photo + commentaire, ou suppression). Sans lui : simulation.
.PARAMETER PostId
    Identifiant de la publication a supprimer (action remove).
.PARAMETER Republier
    Autorise la republication d'un jour deja present dans published_state.json.
.PARAMETER PageId
    Identifiant de la Page (defaut : .graph_api_config.json).
.PARAMETER TokenFile
    Fichier contenant le jeton de Page (1re ligne non vide).
    Defaut : C:\Dev\Wazap\secrets\meta_page_token.txt, puis .graph_api_config.json.
.EXAMPLE
    .\publish_facebook.ps1
    # Preflight : verifie le jeton + la Page, affiche le prochain jour (aucune publication).
.EXAMPLE
    .\publish_facebook.ps1 -Action next -Publier
    # Publie le prochain jour non publie (photo multipart + commentaire epingle).
.EXAMPLE
    .\publish_facebook.ps1 -Action publish -Jour 1 -Publier
.EXAMPLE
    .\publish_facebook.ps1 -Action status
    # Liste les publications de la Page et signale les doublons (ex. post "A propos" publie 2x).
.EXAMPLE
    .\publish_facebook.ps1 -Action remove -PostId 123_456 -Publier
.NOTES
    Le jeton de Page reste dans C:\Dev\Wazap\secrets\ (jamais dans le depot ni dans les assets).
    Codes de sortie : 0 = OK, 1 = erreur (jeton/Page/manifeste/API), 2 = refus par garde-fou.
#>
param(
    [ValidateSet('preflight', 'next', 'publish', 'status', 'remove')]
    [string]$Action = 'preflight',

    [int]$Jour = 0,
    [switch]$Publier,
    [string]$PostId = '',
    [switch]$Republier,
    [string]$PageId = '',
    [string]$TokenFile = ''
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$script:Root        = $PSScriptRoot                                              # marketing\facebook
$script:Manifest    = Join-Path $script:Root 'post_manifest_gen.json'
$script:Config      = Join-Path $script:Root '.graph_api_config.json'
$script:Images      = Join-Path $script:Root 'visuels\generated'
$script:Staging     = Join-Path $script:Root 'gh_images_staging'
$script:StateFile   = Join-Path $script:Root 'published_state.json'
$script:SecretsFile = Join-Path (Split-Path (Split-Path $script:Root -Parent) -Parent) 'secrets\meta_page_token.txt'
$script:ApiVersion  = 'v21.0'
$script:GraphUrl    = 'https://graph.facebook.com'
$script:Utf8        = New-Object System.Text.UTF8Encoding($false)

function Write-Info  { param([string]$m) Write-Host $m }
function Write-Ok    { param([string]$m) Write-Host ("[OK] " + $m) }
function Write-Warn2 { param([string]$m) Write-Host ("[!]  " + $m) }
function Write-Err2  { param([string]$m) Write-Host ("[X]  " + $m) }

# ---------------------------------------------------------------------------
# Manifeste / configuration / jeton / etat local
# ---------------------------------------------------------------------------

function Read-JsonFile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $raw = [System.IO.File]::ReadAllText($Path, $script:Utf8)
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    return ($raw | ConvertFrom-Json)
}

$script:ManifestData = Read-JsonFile -Path $script:Manifest
if (-not $script:ManifestData) { Write-Err2 "Manifeste illisible ou absent : $script:Manifest"; exit 1 }
$script:Cfg = Read-JsonFile -Path $script:Config

if ([string]::IsNullOrWhiteSpace($PageId) -and $script:Cfg -and $script:Cfg.page_id) {
    $PageId = [string]$script:Cfg.page_id
}
if ([string]::IsNullOrWhiteSpace($PageId)) {
    Write-Err2 "PageId introuvable (ni -PageId, ni .graph_api_config.json -> page_id)."
    exit 1
}
if ($script:Cfg -and $script:Cfg.api_version) { $script:ApiVersion = [string]$script:Cfg.api_version }

function Resolve-PageToken {
    param([string]$ExplicitPath)
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) { $candidates += $ExplicitPath }
    $candidates += $script:SecretsFile
    foreach ($c in $candidates) {
        if ($c -and (Test-Path -LiteralPath $c)) {
            $line = Get-Content -LiteralPath $c -Encoding UTF8 |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                    Select-Object -First 1
            if ($line) { return @{ Token = $line.Trim(); Source = $c } }
        }
    }
    if ($script:Cfg -and $script:Cfg.page_access_token) {
        return @{ Token = [string]$script:Cfg.page_access_token; Source = $script:Config }
    }
    return $null
}

function Read-State {
    $o = Read-JsonFile -Path $script:StateFile
    $h = @{}
    if ($o) { foreach ($p in $o.PSObject.Properties) { $h[$p.Name] = $p.Value } }
    return $h
}

function Write-State {
    param([hashtable]$State)
    $ordered = [ordered]@{}
    foreach ($k in ($State.Keys | Sort-Object { [int]$_ })) { $ordered[$k] = $State[$k] }
    $json = ($ordered | ConvertTo-Json -Depth 6)
    [System.IO.File]::WriteAllText($script:StateFile, $json, $script:Utf8)
}

function Get-DayEntry {
    param([int]$J)
    $p = $script:ManifestData.PSObject.Properties[[string]$J]
    if (-not $p) { return $null }
    return $p.Value
}

function Get-DayImagePath {
    param([int]$J, [string]$ManifestName)
    $png = Join-Path $script:Images ('j{0:D2}_gen.png' -f $J)
    if (Test-Path -LiteralPath $png) { return $png }
    if (-not [string]::IsNullOrWhiteSpace($ManifestName)) {
        $alt = Join-Path $script:Images $ManifestName
        if (Test-Path -LiteralPath $alt) { return $alt }
    }
    $jpg = Join-Path $script:Staging ('j{0:D2}_gen.jpg' -f $J)
    if (Test-Path -LiteralPath $jpg) { return $jpg }
    return ''
}

function Get-NextDay {
    param([hashtable]$State)
    for ($j = 1; $j -le 90; $j++) {
        if (-not $State.ContainsKey([string]$j)) { return $j }
    }
    return 0
}

# ---------------------------------------------------------------------------
# Appels API Graph (aucun secret sur la ligne de commande : tout en memoire)
# ---------------------------------------------------------------------------

function Add-Bytes {
    param([System.IO.MemoryStream]$Stream, [byte[]]$Bytes)
    if ($Bytes -and $Bytes.Length -gt 0) { $Stream.Write($Bytes, 0, $Bytes.Length) }
}

function Invoke-Graph {
    param(
        [ValidateSet('GET', 'POST', 'DELETE')] [string]$Method,
        [string]$Path,
        [hashtable]$Query = @{},
        [byte[]]$Body = $null,
        [string]$ContentType = ''
    )
    $url = "$script:GraphUrl/$script:ApiVersion/$Path"
    if ($Query.Count -gt 0) {
        $pairs = @()
        foreach ($k in $Query.Keys) { $pairs += ("{0}={1}" -f $k, [System.Uri]::EscapeDataString([string]$Query[$k])) }
        $url = $url + '?' + ($pairs -join '&')
    }
    $params = @{ Method = $Method; Uri = $url; TimeoutSec = 300; UseBasicParsing = $true }
    if ($Body) { $params.Body = $Body; $params.ContentType = $ContentType }
    try {
        return @{ Ok = $true; Data = (Invoke-RestMethod @params) }
    } catch {
        $msg = $_.Exception.Message
        $resp = $_.Exception.Response
        if ($resp) {
            try {
                $sr  = New-Object System.IO.StreamReader($resp.GetResponseStream())
                $txt = $sr.ReadToEnd()
                $sr.Close()
                if (-not [string]::IsNullOrWhiteSpace($txt)) {
                    $j = $txt | ConvertFrom-Json
                    if ($j.error) {
                        $msg = ("{0} (code {1}, sous-code {2})" -f $j.error.message, $j.error.code, $j.error.error_subcode)
                    } else { $msg = $txt }
                }
            } catch { }
        }
        return @{ Ok = $false; Error = $msg }
    }
}

# Envoi multipart : POST /{page_id}/photos avec le fichier local (champ "source").
# Aucune URL publique n'est necessaire -> pas besoin d'heberger les images.
function Send-PagePhoto {
    param([string]$Token, [string]$ImagePath, [string]$Message)
    $boundary = [System.Guid]::NewGuid().ToString('N')
    $nl       = "`r`n"
    $ms       = New-Object System.IO.MemoryStream
    Add-Bytes $ms ($script:Utf8.GetBytes("--$boundary$nl"))
    Add-Bytes $ms ($script:Utf8.GetBytes("Content-Disposition: form-data; name=`"message`"$nl$nl"))
    Add-Bytes $ms ($script:Utf8.GetBytes($Message))
    Add-Bytes $ms ($script:Utf8.GetBytes($nl))
    Add-Bytes $ms ($script:Utf8.GetBytes("--$boundary$nl"))
    Add-Bytes $ms ($script:Utf8.GetBytes("Content-Disposition: form-data; name=`"access_token`"$nl$nl"))
    Add-Bytes $ms ($script:Utf8.GetBytes($Token))
    Add-Bytes $ms ($script:Utf8.GetBytes($nl))
    Add-Bytes $ms ($script:Utf8.GetBytes("--$boundary$nl"))
    $fileName = [System.IO.Path]::GetFileName($ImagePath)
    $ext      = [System.IO.Path]::GetExtension($ImagePath).ToLowerInvariant()
    $mime     = if ($ext -eq '.png') { 'image/png' } else { 'image/jpeg' }
    Add-Bytes $ms ($script:Utf8.GetBytes("Content-Disposition: form-data; name=`"source`"; filename=`"$fileName`"$nl"))
    Add-Bytes $ms ($script:Utf8.GetBytes("Content-Type: $mime$nl$nl"))
    $fs = [System.IO.File]::OpenRead($ImagePath)
    try {
        $buf = New-Object byte[] 65536
        while (($n = $fs.Read($buf, 0, $buf.Length)) -gt 0) {
            if ($n -eq $buf.Length) { Add-Bytes $ms $buf } else { Add-Bytes $ms ($buf[0..($n - 1)]) }
        }
    } finally { $fs.Close() }
    Add-Bytes $ms ($script:Utf8.GetBytes("$nl--$boundary--$nl"))
    $body = $ms.ToArray()
    $ms.Close()
    return Invoke-Graph -Method POST -Path "$PageId/photos" -Body $body -ContentType "multipart/form-data; boundary=$boundary"
}

# Corps form-urlencode (UTF-8) : necessaire pour les textes accentues (commentaires).
function New-FormBody {
    param([hashtable]$Fields)
    $pairs = @()
    foreach ($k in $Fields.Keys) { $pairs += ("{0}={1}" -f $k, [System.Uri]::EscapeDataString([string]$Fields[$k])) }
    return $script:Utf8.GetBytes(($pairs -join '&'))
}

# ---------------------------------------------------------------------------
# Actions
# ---------------------------------------------------------------------------

function Show-DayPreview {
    param([int]$J, [string]$Prefix)
    $entry = Get-DayEntry -J $J
    if (-not $entry) { Write-Err2 ("Jour {0} absent du manifeste." -f $J); exit 1 }
    $img     = Get-DayImagePath -J $J -ManifestName ([string]$entry.image_file)
    $message = [string]$entry.message
    if ([string]::IsNullOrWhiteSpace($message)) { $message = [string]$entry.caption }
    $flat = ($message -replace "`r?`n", ' / ')
    if ($flat.Length -gt 160) { $flat = $flat.Substring(0, 157) + '...' }
    $imgTxt = $img
    if (-not $img) { $imgTxt = 'INTROUVABLE' }
    Write-Info ""
    Write-Info ("{0} JOUR {1}" -f $Prefix, $J)
    Write-Info ("  image       : {0}" -f $imgTxt)
    Write-Info ("  message     : {0} caracteres" -f $message.Length)
    Write-Info ("  apercu      : {0}" -f $flat)
    Write-Info ("  commentaire : {0}" -f [string]$entry.pinned_comment)
    return @{ Entry = $entry; Image = $img; Message = $message }
}

function Set-CommentPinned {
    param([string]$CommentId, [string]$Token)
    $body = New-FormBody -Fields @{ is_pinned = 'true'; access_token = $Token }
    $r = Invoke-Graph -Method POST -Path $CommentId -Body $body -ContentType 'application/x-www-form-urlencoded'
    if ($r.Ok) { Write-Ok 'Commentaire epingle.' }
    else { Write-Warn2 ("Epinglage automatique indisponible ({0}) -> epingler le commentaire a la main dans la Page." -f $r.Error) }
}

function Invoke-PublishDay {
    param([int]$J, [string]$Token, [hashtable]$State, [bool]$Actif)
    $pre = Show-DayPreview -J $J -Prefix 'A PUBLIER'
    if (-not $pre.Image) {
        Write-Err2 ("Image du jour {0} introuvable (cherchee dans visuels/generated puis gh_images_staging)." -f $J)
        return 1
    }
    if (-not $Actif) {
        Write-Warn2 'Simulation : aucune publication. Ajoutez -Publier pour publier reellement.'
        return 0
    }
    $r = Send-PagePhoto -Token $Token -ImagePath $pre.Image -Message $pre.Message
    if (-not $r.Ok) { Write-Err2 ("Publication refusee : {0}" -f $r.Error); return 1 }
    $postId = [string]$r.Data.id
    Write-Ok ("Publication creee : {0}" -f $postId)

    $comment = [string]$pre.Entry.pinned_comment
    if (-not [string]::IsNullOrWhiteSpace($comment)) {
        $body = New-FormBody -Fields @{ message = $comment; access_token = $Token }
        $rc   = Invoke-Graph -Method POST -Path "$postId/comments" -Body $body -ContentType 'application/x-www-form-urlencoded'
        if ($rc.Ok) {
            Write-Ok ("Commentaire publie : {0}" -f [string]$rc.Data.id)
            Set-CommentPinned -CommentId ([string]$rc.Data.id) -Token $Token
        } else {
            Write-Warn2 ("Commentaire non publie ({0}) -> a poster a la main sous la publication." -f $rc.Error)
        }
    }

    $State[[string]$J] = @{
        post_id = $postId
        at      = (Get-Date).ToString('o')
        image   = [System.IO.Path]::GetFileName($pre.Image)
    }
    Write-State -State $State
    Write-Ok ("Etat enregistre : {0}" -f $script:StateFile)
    return 0
}

# ---------------------------------------------------------------------------
# Execution
# ---------------------------------------------------------------------------

$state     = Read-State
$tokenInfo = Resolve-PageToken -ExplicitPath $TokenFile
if (-not $tokenInfo) {
    Write-Err2 'Jeton de Page introuvable.'
    Write-Info ("    Attendu : {0} (jeton en 1re ligne)." -f $script:SecretsFile)
    Write-Info '    Origine : Business Manager > Utilisateurs systeme > WAZAP Automation > Generer le jeton.'
    Write-Info '    Alternative : -TokenFile <chemin>.'
    exit 1
}
if ($tokenInfo.Source -eq $script:Config) {
    Write-Warn2 'Jeton lu depuis .graph_api_config.json (en clair dans les assets).'
    Write-Warn2 ("    A deplacer vers {0} (regle projet : aucun secret hors secrets/)." -f $script:SecretsFile)
}

Write-Info ''
Write-Info ("== WAZAP / Facebook - Page {0} (API {1}) ==" -f $PageId, $script:ApiVersion)

switch ($Action) {

    'preflight' {
        $info = Invoke-Graph -Method GET -Path $PageId -Query @{ fields = 'name,fan_count,link'; access_token = $tokenInfo.Token }
        if (-not $info.Ok) { Write-Err2 ("Page injoignable : {0}" -f $info.Error); exit 1 }
        Write-Ok ("Page : {0} - {1} abonne(s) - {2}" -f $info.Data.name, $info.Data.fan_count, $info.Data.link)
        $next = Get-NextDay -State $state
        if ($next -eq 0) { Write-Ok 'Les 90 jours du kit sont publies.'; exit 0 }
        $null = Show-DayPreview -J $next -Prefix 'PROCHAIN JOUR'
        Write-Warn2 'Simulation : ajoutez -Publier pour publier ce jour.'
        exit 0
    }

    'next' {
        $next = Get-NextDay -State $state
        if ($next -eq 0) { Write-Ok 'Tous les jours (1..90) sont deja publies.'; exit 0 }
        exit (Invoke-PublishDay -J $next -Token $tokenInfo.Token -State $state -Actif ([bool]$Publier))
    }

    'publish' {
        $j = $Jour
        if ($j -lt 1 -or $j -gt 90) {
            $j = Get-NextDay -State $state
            if ($j -eq 0) { Write-Ok 'Aucun jour restant.'; exit 0 }
            Write-Warn2 ("-Jour non fourni (ou hors 1..90) : jour retenu = {0}." -f $j)
        }
        if ($state.ContainsKey([string]$j) -and -not $Republier) {
            Write-Err2 ("Jour {0} deja publie ({1}) - utilisez -Republier pour forcer." -f $j, $state[[string]$j].post_id)
            exit 2
        }
        exit (Invoke-PublishDay -J $j -Token $tokenInfo.Token -State $state -Actif ([bool]$Publier))
    }

    'status' {
        $r = Invoke-Graph -Method GET -Path "$PageId/published_posts" -Query @{ fields = 'id,created_time,message'; limit = '100'; access_token = $tokenInfo.Token }
        if (-not $r.Ok) { Write-Err2 ("Lecture impossible : {0}" -f $r.Error); exit 1 }
        $posts = @($r.Data.data)
        Write-Ok ("{0} publication(s) lue(s) sur la Page." -f $posts.Count)
        Write-Info ''
        Write-Info 'DATE                         ID                                        APERCU'
        foreach ($p in $posts) {
            $m = [string]$p.message
            if ([string]::IsNullOrWhiteSpace($m)) { $m = '(publication sans texte : photo de profil / couverture)' }
            $m = ($m -replace "`r?`n", ' ')
            if ($m.Length -gt 58) { $m = $m.Substring(0, 55) + '...' }
            Write-Info ("{0}  {1}  {2}" -f $p.created_time, $p.id, $m)
        }
        $groups = @{}
        foreach ($p in $posts) {
            $key = ([string]$p.message).Trim()
            if ($key) {
                if (-not $groups.ContainsKey($key)) { $groups[$key] = @() }
                $groups[$key] += $p.id
            }
        }
        $dups = @($groups.Keys | Where-Object { $groups[$_].Count -gt 1 })
        Write-Info ''
        if ($dups.Count -gt 0) {
            Write-Warn2 'DOUBLONS detectes (meme texte publie plusieurs fois) :'
            foreach ($d in $dups) {
                $ids = $groups[$d]
                Write-Info ("    x{0}  garder {1}  |  supprimer : {2}" -f $ids.Count, $ids[0], (($ids | Select-Object -Skip 1) -join ' '))
            }
            Write-Info '    Correction : .\publish_facebook.ps1 -Action remove -PostId <id> -Publier'
        } else { Write-Ok 'Aucun doublon detecte.' }

        $byMsg = @{}
        foreach ($prop in $script:ManifestData.PSObject.Properties) {
            $mm = ([string]$prop.Value.message).Trim()
            if ($mm) { $byMsg[$mm] = [int]$prop.Name }
        }
        $found = @()
        foreach ($key in $groups.Keys) { if ($byMsg.ContainsKey($key)) { $found += $byMsg[$key] } }
        Write-Info ''
        if ($found.Count -gt 0) { Write-Ok ("Jours du kit 90 jours deja en ligne : {0}" -f (($found | Sort-Object) -join ', ')) }
        else { Write-Warn2 'Aucune publication du kit 90 jours en ligne (le jour 1 reste a lancer).' }
        exit 0
    }

    'remove' {
        if ([string]::IsNullOrWhiteSpace($PostId)) { Write-Err2 'Action remove : -PostId obligatoire.'; exit 1 }
        if (-not $Publier) { Write-Warn2 ("Simulation : la publication {0} serait supprimee. Ajoutez -Publier." -f $PostId); exit 0 }
        $r = Invoke-Graph -Method DELETE -Path $PostId -Query @{ access_token = $tokenInfo.Token }
        if (-not $r.Ok) { Write-Err2 ("Suppression refusee : {0}" -f $r.Error); exit 1 }
        Write-Ok ("Publication supprimee : {0}" -f $PostId)
        exit 0
    }

    default {
        Write-Err2 ("Action inconnue : {0}" -f $Action)
        exit 1
    }
}