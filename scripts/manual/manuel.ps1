#Requires -Version 5.1
<#
.SYNOPSIS
    Console du canal WhatsApp MANUEL WAZAP (palliatif pendant le blocage Meta).
.DESCRIPTION
    Pilote l'API WAZAP avec un compte Admin pour operer une course a la main :
    creer la commande, avancer les statuts (cycle de vie complet), recuperer le lien
    de suivi client et l'ID de la commande.

    Aucun envoi WhatsApp automatique n'est effectue : l'operateur ecrit et telephone
    lui-meme (regles d'or : opt-in explicite, jamais de diffusion de masse).
    Procedure complete : prospection/CANAL_MANUEL_WHATSAPP.md
.PARAMETER Action
    login    : verifie les identifiants et affiche le role.
    list     : liste les dernieres commandes.
    new      : cree une commande (mode texte).
    show     : detail d'une commande (statut, code de livraison, lien client).
    advance  : passe la commande au statut suivant du cycle de vie.
    set      : force un statut precis (dont Cancelled).
    link     : affiche le lien de suivi a envoyer au client.
    statuses : rappelle le cycle de vie et les prerequis.
.EXAMPLE
    .\manuel.ps1 -Action login
    # Demande le mot de passe admin (ou utilise $env:WAZAP_ADMIN_PASSWORD).
.EXAMPLE
    .\manuel.ps1 -Action new -Client "Awa" -ClientPhone "+2250102030405" -VendorPhone "+2250708091011" -Description "Poulet braise x2" -Amount 5000
    # Cree la commande, affiche l'ID complet, le lien client et le message a coller.
.EXAMPLE
    .\manuel.ps1 -Action advance -OrderId 3f1c9a2e-1111-2222-3333-444455556666 -RiderPhone "+2250506070809"
    # Passe au statut suivant ; -RiderPhone est requis pour RiderAssigned.
.EXAMPLE
    .\manuel.ps1 -Action list
    .\manuel.ps1 -Action show -OrderId 3f1c9a2e-1111-2222-3333-444455556666
    .\manuel.ps1 -Action set -OrderId 3f1c9a2e-... -Status Cancelled
.NOTES
    Base produit ciblee par defaut : prod SmarterASP (jtempurl).
    En dev local : -BaseUrl http://localhost:5297
    Compagnon de routine : relances.ps1 (suivi, messages prets a coller, relances, recap, journal).
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("login", "new", "list", "show", "set", "advance", "link", "statuses", "vendor")]
    [string]$Action,

    [string]$BaseUrl = "https://junioradon79gm-001-site1.jtempurl.com",
    [string]$Username = "admin",
    [string]$Password = $env:WAZAP_ADMIN_PASSWORD,
    [string]$TotpCode = "",

    [string]$OrderId = "",
    [string]$Client = "",
    [string]$ClientPhone = "",
    [string]$VendorPhone = "",
    [string]$Description = "",
    [decimal]$Amount = 0,

    [string]$Status = "",
    [string]$RiderPhone = "",
    [string]$VendorPassword = "",
    [int]$Top = 10
)

$ErrorActionPreference = "Stop"
$script:ApiBase = $BaseUrl.TrimEnd('/')
$script:Jwt = ""

# Cycle de vie impose par le domaine (Wazap.Domain/Entities/Order.cs) : chaque
# transition exige le statut precedent.
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

function Get-NextStatus {
    param([Parameter(Mandatory = $true)][string]$Current)
    $index = $script:StatusOrder.IndexOf($Current)
    if ($index -lt 0 -or $index -ge ($script:StatusOrder.Count - 1)) { return "" }
    return $script:StatusOrder[$index + 1]
}

function Assert-OrderId {
    if ([string]::IsNullOrWhiteSpace($OrderId)) {
        throw "-OrderId requis (l'ID complet de la commande)."
    }
}

function Set-OrderStatus {
    param([string]$Target)

    if ($script:StatusOrder -notcontains $Target -and $Target -ne "Cancelled") {
        throw "Statut inconnu : '$Target' (voir -Action statuses)."
    }
    if ($Target -eq "RiderAssigned" -and [string]::IsNullOrWhiteSpace($RiderPhone)) {
        throw "-RiderPhone requis pour le statut RiderAssigned (numero WhatsApp du livreur)."
    }

    $payload = @{ status = $Target }
    if (-not [string]::IsNullOrWhiteSpace($RiderPhone)) {
        $payload.riderWhatsAppNumber = $RiderPhone
    }

    Invoke-WazapApi -Method Put -Path "/api/orders/$OrderId/status" -Body $payload | Out-Null
}

# ---- Session (toutes les actions sauf 'statuses' ont besoin d'un jeton) ----
$session = $null
if ($Action -ne "statuses") {
    $session = Connect-Wazap
    Write-Host "Connecte : $($session.username) (role $($session.role))" -ForegroundColor DarkGray
}

switch ($Action) {
    "login" {
        Write-Host "OK : $($session.username) / $($session.role)" -ForegroundColor Green
    }

    "statuses" {
        Write-Host "Cycle de vie d'une commande (mode manuel) :" -ForegroundColor Cyan
        $script:StatusOrder | ForEach-Object { Write-Host "  - $_" }
        Write-Host ""
        Write-Host "Prerequis : -RiderPhone pour RiderAssigned ; Cancelled possible avant InTransit." -ForegroundColor DarkGray
        Write-Host "Le code de livraison n'est genere que par le flux WhatsApp (mode manuel : aucun code)." -ForegroundColor DarkGray
    }

    "new" {
        if ([string]::IsNullOrWhiteSpace($Client) -or [string]::IsNullOrWhiteSpace($ClientPhone) -or
            [string]::IsNullOrWhiteSpace($VendorPhone) -or [string]::IsNullOrWhiteSpace($Description) -or
            $Amount -le 0) {
            throw "Parametres requis : -Client, -ClientPhone, -VendorPhone, -Description, -Amount (> 0)."
        }

        $payload = @{
            clientName           = $Client
            clientWhatsAppNumber = $ClientPhone
            vendorWhatsAppNumber = $VendorPhone
            description          = $Description
            amount               = $Amount
        }

        $order = Invoke-WazapApi -Method Post -Path "/api/orders" -Body $payload
        $link = Get-TrackingUrl -Id $order.id
        $short = Get-ShortId -Id $order.id

        Write-Host ""
        Write-Host "Commande creee : #$short" -ForegroundColor Green
        Write-Host "  ID complet  : $($order.id)"
        Write-Host "  Statut      : $($order.status)"
        Write-Host "  Lien client : $link" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Message a coller au client :" -ForegroundColor Cyan
        Write-Host "  Commande #$short enregistree. Suivez votre livraison ici : $link"
        Write-Host ""
        Write-Host "Suite : .\manuel.ps1 -Action advance -OrderId $($order.id)" -ForegroundColor DarkGray
    }

    "vendor" {
        if ([string]::IsNullOrWhiteSpace($Client)) { throw "-Client requis (nom du vendeur, ex. 'Boutique Awa')." }
        if ([string]::IsNullOrWhiteSpace($VendorPhone)) { throw "-VendorPhone requis (numero WhatsApp E.164 du vendeur)." }
        if ([string]::IsNullOrWhiteSpace($VendorPassword)) { throw "-VendorPassword requis (mot de passe initial remis au vendeur)." }

        $payload = @{
            username    = $Client
            password    = $VendorPassword
            role        = "Vendor"
            phoneNumber = $VendorPhone
        }
        $user = Invoke-WazapApi -Method Post -Path "/api/auth/register" -Body $payload
        Write-Host "Vendeur enregistre : $($user.username) ($($user.role))" -ForegroundColor Green
        Write-Host "  ID             : $($user.id)"
        Write-Host "  Numero a reutiliser dans -Action new : $VendorPhone" -ForegroundColor Yellow
    }

    "list" {
        $page = Invoke-WazapApi -Method Get -Path "/api/orders?page=1&pageSize=$Top"
        $rows = @($page.items) | ForEach-Object {
            [pscustomobject]@{
                Id      = Get-ShortId -Id $_.id
                Statut  = $_.status
                Client  = $_.clientName
                Montant = $_.amount
                Cree    = $_.createdAt
            }
        }
        $rows | Format-Table -AutoSize
        Write-Host "Total : $($page.total) commande(s) en base." -ForegroundColor DarkGray
    }

    "show" {
        Assert-OrderId
        $order = Invoke-WazapApi -Method Get -Path "/api/orders/$OrderId"
        Write-Host ""
        Write-Host "Commande #$(Get-ShortId -Id $order.id) - $($order.status)" -ForegroundColor Cyan
        Write-Host "  Client         : $($order.clientName) ($($order.clientWhatsAppNumber))"
        Write-Host "  Vendeur        : $($order.vendorWhatsAppNumber)"
        Write-Host "  Description    : $($order.description)"
        Write-Host "  Montant        : $($order.amount) FCFA"
        Write-Host "  Livreur (tel.) : $($order.riderWhatsAppNumber)"
        if ($order.deliveryCode) {
            Write-Host "  Code livraison : $($order.deliveryCode)" -ForegroundColor Yellow
        }
        else {
            Write-Host "  Code livraison : (non genere - normal en mode manuel)" -ForegroundColor DarkGray
        }
        Write-Host "  Suivant        : $(Get-NextStatus -Current $order.status)"
        Write-Host "  Lien client    : $(Get-TrackingUrl -Id $order.id)" -ForegroundColor Yellow
    }

    "link" {
        Assert-OrderId
        Write-Host (Get-TrackingUrl -Id $OrderId) -ForegroundColor Yellow
    }

    "set" {
        Assert-OrderId
        if ([string]::IsNullOrWhiteSpace($Status)) {
            throw "-Status requis (voir -Action statuses)."
        }
        Set-OrderStatus -Target $Status
        Write-Host "Statut applique : $Status" -ForegroundColor Green
        Write-Host "Lien client : $(Get-TrackingUrl -Id $OrderId)" -ForegroundColor DarkGray
    }

    "advance" {
        Assert-OrderId
        $order = Invoke-WazapApi -Method Get -Path "/api/orders/$OrderId"
        $next = Get-NextStatus -Current $order.status

        if ([string]::IsNullOrWhiteSpace($next)) {
            Write-Host "Aucune transition automatique depuis '$($order.status)' (course close ou annulee)." -ForegroundColor DarkYellow
        }
        else {
            if ($next -eq "RiderAssigned" -and [string]::IsNullOrWhiteSpace($RiderPhone)) {
                $RiderPhone = Read-Host "Numero WhatsApp du livreur (ex. +2250506070809)"
            }
            Set-OrderStatus -Target $next
            Write-Host "$($order.status) -> $next" -ForegroundColor Green
            if ($next -eq "Delivered") {
                Write-Host "Course close : penser au recapitulatif de fin de journee (prospection/CANAL_MANUEL_WHATSAPP.md)." -ForegroundColor DarkGray
            }
            else {
                Write-Host "Suite : .\manuel.ps1 -Action advance -OrderId $OrderId" -ForegroundColor DarkGray
            }
        }
    }
}