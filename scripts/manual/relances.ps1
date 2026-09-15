#Requires -Version 5.1
<#
.SYNOPSIS
    Compagnon du canal WhatsApp MANUEL WAZAP : suivi, messages a coller, relances, recap, journal.
.DESCRIPTION
    Complete manuel.ps1 (creation de commande / avancement des statuts) en couvrant la routine
    quotidienne de l'operateur (prospection/CANAL_MANUEL_WHATSAPP.md §4-§6) :

      suivi   : les commandes actives, leur age et la prochaine action a faire.
      msg     : les messages prets a coller pour une commande (client, livreur, annulation).
      relance : les relances a coller pour les commandes qui depassent les seuils.
      recap   : le recapitulatif du jour, pret a coller (equipe + MEMOIRE.md §11).
      journal : journal quotidien semi-automatise (note libre et/ou recap du jour).

    Aucun envoi WhatsApp automatique n'est effectue : l'operateur colle lui-meme chaque
    message dans l'app WhatsApp Business (opt-in explicite, jamais de diffusion de masse).
    Les textes reprennent les modeles §5.4 / §5.5 / §5.6 de la procedure (sans accents :
    convention ASCII du repertoire, comme manuel.ps1).
.EXAMPLE
    .\relances.ps1 -Action suivi
    # Vue de la journee : commandes actives, age, prochaine action.
.EXAMPLE
    .\relances.ps1 -Action msg -OrderId 3f1c9a2e-1111-2222-3333-444455556666
    # Messages client + livreur prets a coller pour cette commande.
.EXAMPLE
    .\relances.ps1 -Action msg -OrderId 3f1c9a2e-... -Reason "vendeur ferme aujourd'hui"
    # Message d'annulation au client (§5.6).
.EXAMPLE
    .\relances.ps1 -Action relance
    # Relances a coller pour les commandes hors delai (-VendorMin/-RiderMin/-ProgressMin).
.EXAMPLE
    .\relances.ps1 -Action recap
    .\relances.ps1 -Action journal -Note "2 livreurs de permanence : Karim et Awa"
    .\relances.ps1 -Action journal -Recap
    # Recap du jour + journal quotidien dans logs/journal_canal_manuel/<AAAA-MM>.md.
.NOTES
    Base produit ciblee par defaut : prod SmarterASP (jtempurl).
    En dev local : -BaseUrl http://localhost:5297
    Heure de reference : Abidjan = UTC+0 (pas de changement d'heure) : l'age des commandes
    est calcule depuis createdAt sans conversion.
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("suivi", "msg", "relance", "recap", "journal")]
    [string]$Action,

    [string]$BaseUrl = "https://junioradon79gm-001-site1.jtempurl.com",
    [string]$Username = "admin",
    [string]$Password = $env:WAZAP_ADMIN_PASSWORD,
    [string]$TotpCode = "",

    [string]$OrderId = "",
    [string]$Reason = "",

    # Seuils de relance (minutes depuis la creation de la commande)
    [int]$VendorMin = 15,
    [int]$RiderMin = 15,
    [int]$ProgressMin = 60,

    [string]$Date = "",
    [int]$Top = 200,

    [string]$Note = "",
    [switch]$Recap,
    [string]$JournalDir = ""
)

$ErrorActionPreference = "Stop"
$script:ApiBase = $BaseUrl.TrimEnd('/')
$script:Jwt = ""

# Cycle de vie impose par le domaine (Wazap.Domain/Entities/Order.cs).
$script:StatusOrder = @(
    "PendingVendorConfirmation",
    "VendorConfirmed",
    "AwaitingRiderAcceptance",
    "RiderAssigned",
    "ReadyForPickup",
    "PickedUp",
    "InTransit",
    "Delivered"
)
$script:ClosingStatuses = @("Delivered", "Cancelled")

# ---- Helpers repris de manuel.ps1 (script autoportant, meme convention) ----

function Read-AdminPassword {
    if ([string]::IsNullOrWhiteSpace($Password)) {
        $secure = Read-Host "Mot de passe admin ($Username)" -AsSecureString
        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try {
            $script:Password = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
    else {
        $script:Password = $Password
    }
}

function Connect-Wazap {
    Read-AdminPassword
    $body = @{ username = $Username; password = $script:Password } | ConvertTo-Json -Compress
    try {
        $res = Invoke-RestMethod -Uri "$script:ApiBase/api/auth/login" -Method Post -Body $body `
            -ContentType "application/json; charset=utf-8"
    }
    catch {
        throw "Connexion refusee ($($_.Exception.Message)). Verifiez -Username / -Password ou -BaseUrl."
    }

    if ($res.mfaRequired -and -not $res.token) {
        if ([string]::IsNullOrWhiteSpace($TotpCode)) {
            throw "2FA active sur ce compte : relancez avec -TotpCode <code a 6 chiffres>."
        }
        $body2 = @{ username = $Username; password = $script:Password; code = $TotpCode } | ConvertTo-Json -Compress
        $res = Invoke-RestMethod -Uri "$script:ApiBase/api/auth/2fa/verify" -Method Post -Body $body2 `
            -ContentType "application/json; charset=utf-8"
    }

    if (-not $res.token) {
        throw "Jeton absent dans la reponse d'authentification (role insuffisant ?)."
    }

    $script:Jwt = $res.token
    return $res
}

function Invoke-WazapApi {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        $Body = $null
    )

    $call = @{
        Uri     = "$script:ApiBase$Path"
        Method  = $Method
        Headers = @{ Authorization = "Bearer $script:Jwt" }
    }
    if ($null -ne $Body) {
        $call.Body = ($Body | ConvertTo-Json -Compress)
        $call.ContentType = "application/json; charset=utf-8"
    }

    try {
        return Invoke-RestMethod @call
    }
    catch {
        $response = $_.Exception.Response
        if ($null -ne $response) {
            $detail = ""
            $stream = $response.GetResponseStream()
            if ($null -ne $stream) {
                $reader = New-Object System.IO.StreamReader($stream)
                $detail = $reader.ReadToEnd()
            }
            throw "HTTP $([int]$response.StatusCode) sur $Method $Path : $detail"
        }
        throw
    }
}

function Get-TrackingUrl {
    param([Parameter(Mandatory = $true)][string]$Id)
    # Meme format que Client:TrackingBaseUrl (appsettings) -> SPA /app/suivi/{id}
    return "$script:ApiBase/app/suivi/$Id"
}

function Get-ShortId {
    param([Parameter(Mandatory = $true)][string]$Id)
    if ($Id.Length -ge 8) { return $Id.Substring(0, 8) }
    return $Id
}

# ---- Helpers de journee ----

function Get-TargetDate {
    if ([string]::IsNullOrWhiteSpace($Date)) { return (Get-Date).Date }
    return [datetime]::ParseExact($Date, "yyyy-MM-dd", [Globalization.CultureInfo]::InvariantCulture)
}

function Get-Orders {
    $page = Invoke-WazapApi -Method Get -Path "/api/orders?page=1&pageSize=$Top"
    if ($page.total -gt $Top) {
        Write-Host "Attention : $($page.total) commandes en base, lecture des $Top dernieres seulement." -ForegroundColor DarkYellow
    }
    return @($page.items)
}

function Get-OrderCreatedAt {
    param($Order)
    $raw = $Order.createdAt
    if ($raw -is [datetime]) {
        if ($raw.Kind -eq [System.DateTimeKind]::Local) { return $raw.ToUniversalTime() }
        return $raw
    }
    return [datetime]::Parse($raw, [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind)
}

function Get-OrderAgeMinutes {
    param($Order, [datetime]$Now)
    return [int]($Now - (Get-OrderCreatedAt $Order)).TotalMinutes
}

function Get-NextAction {
    param([string]$Status)
    switch ($Status) {
        "PendingVendorConfirmation" { return "Appeler le vendeur pour confirmer, puis : manuel.ps1 -Action advance" }
        "VendorConfirmed"           { return "Appeler un livreur, puis : manuel.ps1 -Action advance -RiderPhone <numero>" }
        "AwaitingRiderAcceptance"   { return "Appeler un livreur (course toujours ouverte), puis advance -RiderPhone <numero>" }
        "RiderAssigned"             { return "Verifier le retrait chez le vendeur avec le livreur" }
        "ReadyForPickup"            { return "Verifier le retrait chez le vendeur, puis advance" }
        "PickedUp"                  { return "Course en cours : ETA a confirmer avec le livreur" }
        "InTransit"                 { return "A la remise : photo du colis + advance (Delivered)" }
        default                     { return "" }
    }
}

# $null si rien a relancer, sinon la description du depassement.
function Get-OrderDue {
    param($Order, [datetime]$Now)
    $age = Get-OrderAgeMinutes -Order $Order -Now $Now
    switch ($Order.status) {
        "PendingVendorConfirmation" {
            if ($age -ge $VendorMin) { return "vendeur non confirme depuis $age min (seuil $VendorMin)" }
        }
        { $_ -eq "VendorConfirmed" -or $_ -eq "AwaitingRiderAcceptance" } {
            if ($age -ge $RiderMin) { return "aucun livreur depuis $age min (seuil $RiderMin)" }
        }
        { $_ -eq "RiderAssigned" -or $_ -eq "ReadyForPickup" -or $_ -eq "PickedUp" -or $_ -eq "InTransit" } {
            if ($age -ge $ProgressMin) { return "sans progression depuis $age min (seuil $ProgressMin)" }
        }
    }
    return $null
}

function Write-PasteBlock {
    param([string]$To, [string]$Text)
    Write-Host ""
    Write-Host ">>> A coller a : $To" -ForegroundColor Cyan
    Write-Host $Text -ForegroundColor Yellow
}

# Modele §5.4 : lien de suivi au client (apres creation / pendant la course).
function New-ClientFollowMessage {
    param($Order)
    $link = Get-TrackingUrl -Id $Order.id
    return "Commande #$(Get-ShortId -Id $Order.id) enregistree chez WAZAP.`r`n" +
        "Livreur en route vers le vendeur. Suivez votre livraison en direct :`r`n$link"
}

# Modele §5.5 : brief du livreur appele par l'operateur.
function New-RiderMessage {
    param($Order)
    return "Course #$(Get-ShortId -Id $Order.id) - Vendeur : $($Order.vendorWhatsAppNumber) - " +
        "Client : $($Order.clientName) ($($Order.clientWhatsAppNumber))`r`n" +
        "Colis : $($Order.description)`r`n" +
        "Montant commande : $([int]$Order.amount) F - Frais livreur : 1 000 a 2 000 F (payes au livreur, a convenir)`r`n" +
        "Recupere au vendeur, puis appelle-moi a la remise. Merci !"
}

# Modele §5.6 : report / annulation au client.
function New-CancelMessage {
    param($Order, [string]$Why)
    if ([string]::IsNullOrWhiteSpace($Why)) { $Why = "<raison>" }
    return "Bonjour, nous ne pouvons pas honorer la course #$(Get-ShortId -Id $Order.id) " +
        "(raison : $Why).`r`nAucun frais n'est du. Nous pouvons reprogrammer des que possible - dites-nous."
}

function New-RiderRelanceMessage {
    param($Order)
    return "Petit point WAZAP : la course #$(Get-ShortId -Id $Order.id) est toujours en cours.`r`n" +
        "Ou en es-tu ? (retrait chez le vendeur / en route / livree)"
}

function New-ClientRelanceMessage {
    param($Order)
    $link = Get-TrackingUrl -Id $Order.id
    return "Bonjour, votre commande #$(Get-ShortId -Id $Order.id) est en cours de livraison.`r`n" +
        "Merci de garder votre telephone disponible. Suivi en direct : $link"
}

function Resolve-JournalDir {
    if (-not [string]::IsNullOrWhiteSpace($JournalDir)) { return $JournalDir }
    # Par defaut : <repo>\logs\journal_canal_manuel\ (hors solution, donnee d'exploitation)
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\")).Path
    return (Join-Path $repoRoot "logs\journal_canal_manuel")
}

# ---- Session (toutes les actions sauf 'journal' sans -Recap ont besoin d'un jeton) ----
$now = Get-Date
$session = $null
if ($Action -ne "journal" -or $Recap) {
    $session = Connect-Wazap
    Write-Host "Connecte : $($session.username) (role $($session.role))" -ForegroundColor DarkGray
}

function Assert-OrderSelected {
    if ([string]::IsNullOrWhiteSpace($OrderId)) {
        throw "-OrderId requis (l'ID complet de la commande)."
    }
}

switch ($Action) {
    "suivi" {
        $orders = @(Get-Orders | Where-Object { $script:ClosingStatuses -notcontains $_.status })
        if ($orders.Count -eq 0) {
            Write-Host "Aucune commande active. Journee vide (ou commandes toutes closees)." -ForegroundColor DarkGray
            return
        }

        $rows = foreach ($o in $orders) {
            $age = Get-OrderAgeMinutes -Order $o -Now $now
            $due = Get-OrderDue -Order $o -Now $now
            [pscustomobject]@{
                Commande = "#$(Get-ShortId -Id $o.id)"
                Statut   = $o.status
                Age      = "$age min"
                Alerte   = $(if ($null -ne $due) { "!! $due" } else { "" })
            }
        }
        $rows | Format-Table -AutoSize

        Write-Host "Prochaine action par commande :" -ForegroundColor Cyan
        foreach ($o in $orders) {
            Write-Host "  #$(Get-ShortId -Id $o.id) [$($o.status)] : $(Get-NextAction -Status $o.status)"
        }
        Write-Host ""
        Write-Host "Messages prets : .\relances.ps1 -Action msg -OrderId <id complet>" -ForegroundColor DarkGray
    }

    "msg" {
        Assert-OrderSelected
        $order = Invoke-WazapApi -Method Get -Path "/api/orders/$OrderId"

        Write-Host ""
        Write-Host "Commande #$(Get-ShortId -Id $order.id) [$($order.status)]" -ForegroundColor Cyan
        Write-Host "  Client  : $($order.clientName) ($($order.clientWhatsAppNumber))"
        Write-Host "  Vendeur : $($order.vendorWhatsAppNumber)"
        if (-not [string]::IsNullOrWhiteSpace($order.riderWhatsAppNumber)) {
            Write-Host "  Livreur : $($order.riderWhatsAppNumber)"
        }

        if ($script:ClosingStatuses -contains $order.status) {
            Write-Host ""
            Write-Host "Commande closee ($($order.status)) : rien a coller." -ForegroundColor DarkYellow
            return
        }

        # §5.4 : lien de suivi au client (toujours pertinent avant la remise).
        Write-PasteBlock -To "client $($order.clientWhatsAppNumber)" -Text (New-ClientFollowMessage -Order $order)

        # §5.5 : brief du livreur des qu'il est designe (RiderAssigned ou au-dela).
        if ($script:StatusOrder.IndexOf($order.status) -ge $script:StatusOrder.IndexOf("RiderAssigned")) {
            Write-PasteBlock -To "livreur $($order.riderWhatsAppNumber)" -Text (New-RiderMessage -Order $order)
        }
        else {
            Write-Host ""
            Write-Host "(Brief livreur §5.5 disponible des RiderAssigned - il faut d'abord designer un livreur." -ForegroundColor DarkGray
            Write-Host " Prochaine action : $(Get-NextAction -Status $order.status))" -ForegroundColor DarkGray
        }

        # §5.6 : annulation, si -Reason est fourni.
        if (-not [string]::IsNullOrWhiteSpace($Reason)) {
            Write-PasteBlock -To "client $($order.clientWhatsAppNumber)" -Text (New-CancelMessage -Order $order -Why $Reason)
            Write-Host "(Apres envoi : manuel.ps1 -Action set -Status Cancelled -OrderId $OrderId)" -ForegroundColor DarkGray
        }
    }

    "relance" {
        $orders = @(Get-Orders | Where-Object { $script:ClosingStatuses -notcontains $_.status })
        $dueOrders = @($orders | Where-Object { $null -ne (Get-OrderDue -Order $_ -Now $now) })

        if ($dueOrders.Count -eq 0) {
            Write-Host "Rien a relancer : aucune commande active ne depasse les seuils " -NoNewline
            Write-Host "(vendeur $VendorMin / livreur $RiderMin / progression $ProgressMin min)." -ForegroundColor DarkGray
            return
        }

        Write-Host "$($dueOrders.Count) commande(s) hors delai :" -ForegroundColor Yellow
        foreach ($o in $dueOrders) {
            $due = Get-OrderDue -Order $o -Now $now
            Write-Host ""
            Write-Host "--- #$(Get-ShortId -Id $o.id) [$($o.status)] : $due" -ForegroundColor Magenta

            switch ($o.status) {
                "PendingVendorConfirmation" {
                    Write-Host "APPELER le vendeur $($o.vendorWhatsAppNumber) puis advance (VendorConfirmed)." -ForegroundColor Green
                }
                { $_ -eq "VendorConfirmed" -or $_ -eq "AwaitingRiderAcceptance" } {
                    Write-Host "APPELER un livreur, puis advance -RiderPhone <numero>." -ForegroundColor Green
                    $age = Get-OrderAgeMinutes -Order $o -Now $now
                    if ($age -ge (2 * $RiderMin)) {
                        # Attente longue : rassurer le client pendant la recherche.
                        Write-PasteBlock -To "client $($o.clientWhatsAppNumber)" -Text (New-ClientFollowMessage -Order $o)
                    }
                }
                { $_ -eq "RiderAssigned" -or $_ -eq "ReadyForPickup" -or $_ -eq "PickedUp" -or $_ -eq "InTransit" } {
                    $riderTo = $(if (-not [string]::IsNullOrWhiteSpace($o.riderWhatsAppNumber)) { "livreur $($o.riderWhatsAppNumber)" } else { "livreur (numero inconnu : verifier la commande)" })
                    Write-PasteBlock -To $riderTo -Text (New-RiderRelanceMessage -Order $o)
                    if ($o.status -eq "InTransit") {
                        Write-PasteBlock -To "client $($o.clientWhatsAppNumber)" -Text (New-ClientRelanceMessage -Order $o)
                    }
                }
            }
        }
    }

    "recap" {
        $day = Get-TargetDate
        $orders = @(Get-Orders | Where-Object { (Get-OrderCreatedAt -Order $_).Date -eq $day })

        $delivered = @($orders | Where-Object { $_.status -eq "Delivered" })
        $cancelled = @($orders | Where-Object { $_.status -eq "Cancelled" })
        $active = @($orders | Where-Object { $script:ClosingStatuses -notcontains $_.status })
        $deliveredAmount = ($delivered | Measure-Object -Property amount -Sum).Sum
        if ($null -eq $deliveredAmount) { $deliveredAmount = 0 }

        $label = $day.ToString("yyyy-MM-dd")
        Write-Host ""
        Write-Host "Recap du $label (API) : creees $($orders.Count) | livrees $($delivered.Count) | annulees $($cancelled.Count) | en cours $($active.Count)" -ForegroundColor Cyan
        foreach ($a in $active) {
            Write-Host "  en cours : #$(Get-ShortId -Id $a.id) [$($a.status)] $(Get-NextAction -Status $a.status)" -ForegroundColor DarkGray
        }

        Write-Host ""
        Write-Host ">>> A coller a l'equipe (puis reporter dans MEMOIRE.md §11) :" -ForegroundColor Cyan
        $recapText = "Recap WAZAP du $label`r`n" +
            "Courses creees : $($orders.Count)`r`n" +
            "Livrees : $($delivered.Count) (montant cumule : $([int]$deliveredAmount) F)`r`n" +
            "Annulees : $($cancelled.Count)`r`n" +
            "En cours : $($active.Count)`r`n" +
            "Incidents : <a completer>"
        Write-Host $recapText -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Journaliser ce recap : .\relances.ps1 -Action journal -Recap" -ForegroundColor DarkGray
    }

    "journal" {
        $dir = Resolve-JournalDir
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        $file = Join-Path $dir "$($now.ToString('yyyy-MM')).md"
        $dayLabel = $now.ToString("yyyy-MM-dd")

        if ([string]::IsNullOrWhiteSpace($Note) -and -not $Recap) {
            # Consultation : affiche le journal du mois.
            if (Test-Path $file) { Get-Content $file -Encoding UTF8 }
            else { Write-Host "Journal vide : $file" -ForegroundColor DarkGray }
            return
        }

        $block = ""
        $existing = ""
        if (Test-Path $file) { $existing = [System.IO.File]::ReadAllText($file) }

        if ($existing -notmatch [regex]::Escape("## $dayLabel")) {
            $block += "## $dayLabel`r`n`r`n"
        }

        if ($Recap) {
            $day = Get-TargetDate
            $orders = @(Get-Orders | Where-Object { (Get-OrderCreatedAt -Order $_).Date -eq $day })
            $delivered = @($orders | Where-Object { $_.status -eq "Delivered" })
            $cancelled = @($orders | Where-Object { $_.status -eq "Cancelled" })
            $active = @($orders | Where-Object { $script:ClosingStatuses -notcontains $_.status })
            $deliveredAmount = ($delivered | Measure-Object -Property amount -Sum).Sum
            if ($null -eq $deliveredAmount) { $deliveredAmount = 0 }

            $block += "- **Recap $dayLabel** : creees $($orders.Count) / livrees $($delivered.Count) " +
                "(montant $([int]$deliveredAmount) F) / annulees $($cancelled.Count) / en cours $($active.Count)`r`n"
        }

        if (-not [string]::IsNullOrWhiteSpace($Note)) {
            $block += "- **$($now.ToString('HH:mm'))** : $Note`r`n"
        }

        [System.IO.File]::AppendAllText($file, $block, [System.Text.Encoding]::UTF8)
        Write-Host "Journal mis a jour : $file" -ForegroundColor Green
    }
}
