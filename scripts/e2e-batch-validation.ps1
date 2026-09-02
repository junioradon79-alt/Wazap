param(
    [Parameter(Mandatory = $true)][ValidateSet(1, 2, 3, 4)][int]$Phase,
    [string]$AdminPassword = $env:WAZAP_ADMIN_PASSWORD
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ([string]::IsNullOrWhiteSpace($AdminPassword)) {
    throw "Indiquez le mot de passe admin via -AdminPassword ou la variable d'environnement WAZAP_ADMIN_PASSWORD."
}
$Base = 'http://localhost:5297'
$StateFile = 'c:\Dev\Wazap\e2e-state.json'
$VendorId = '2d84c0a6-39f4-4fdb-a38a-bc8700c47fc8'   # Rôtisserie du Marché
$VendorPhone = '+33612456789'
$RiderIds = @(
    '6ef75419-194e-4bf8-8023-d30ae3315c38',   # Karim Diallo
    '9089cd4f-5617-4862-935b-bdf16222f7a7',   # Lucas Martin
    '179a27ff-9389-4313-89a7-1cbc5b64c280',   # Sofiane Benali
    'a7ad5ff7-646c-4d10-87b3-fa3b27f518cc',   # Yann Le Goff
    '7f8a76ec-659e-40de-a831-72b9127145ce'    # TestRider01
)
$VendorLat = 48.8536329
$VendorLon = 2.3707735

function Save-State {
    $script:state | ConvertTo-Json -Depth 6 | Out-File $StateFile -Encoding utf8
}
function Load-State {
    $script:state = @{}
    if (Test-Path $StateFile) {
        $json = Get-Content $StateFile -Raw | ConvertFrom-Json
        foreach ($prop in $json.PSObject.Properties) {
            $script:state[$prop.Name] = $prop.Value
        }
    }
}
function Get-H {
    if ($script:state.token) { @{ Authorization = "Bearer $($script:state.token)" } } else { @{} }
}
function Call-Api {
    param([string]$Method, [string]$Path, $Body)
    $params = @{ Method = $Method; Uri = "$Base$Path"; Headers = Get-H; UseBasicParsing = $true }
    if ($null -ne $Body) {
        $params.ContentType = 'application/json; charset=utf-8'
        $params.Body = ($Body | ConvertTo-Json -Depth 6)
    }
    $resp = Invoke-WebRequest @params
    if ($resp.Content) { return $resp.Content | ConvertFrom-Json }
    return $null
}
function As-Array {
    param($x)
    if ($null -eq $x) { return @() }
    if ($x -is [System.Array]) { return @($x) }
    return @($x)
}
function Assert {
    param([bool]$Cond, [string]$Msg)
    if (-not $Cond) { throw "ASSERT FAILED: $Msg" }
    Write-Output "  OK $Msg"
}
function Get-Order {
    param([string]$Id) Call-Api GET "/api/orders/$Id"
}
function Confirm-Order {
    param([string]$Id) Call-Api PUT "/api/orders/$Id/status" @{ status = 'VendorConfirmed' }
}
function Cancel-Order {
    param([string]$Id) Call-Api PUT "/api/orders/$Id/status" @{ status = 'Cancelled' }
}
function Create-Order {
    param([string]$Client)
    Call-Api POST '/api/orders' @{
        clientName            = $Client
        clientWhatsAppNumber  = '+33699887766'
        vendorWhatsAppNumber  = $VendorPhone
        description           = "E2E batch validation - $Client"
        amount                = 2500
    }
}
function Get-OffersList {
    param([string]$OrderId)
    $resp = Invoke-WebRequest -Uri "$Base/api/orders/$OrderId/offers" -Headers (Get-H) -UseBasicParsing
    if (-not $resp.Content) { return @() }
    $arr = $resp.Content | ConvertFrom-Json
    if ($null -eq $arr) { return @() }
    if ($arr -is [System.Array]) { return @($arr) }
    # Sécurité : wrapper éventuel {value:[...], Count}
    if ($arr.PSObject.Properties.Name -contains 'value') { return @($arr.value) }
    return @($arr)
}
function Get-PendingOffers {
    param([string]$OrderId)
    $offers = Get-OffersList $OrderId
    return @($offers | Where-Object { $_.status -eq 'Pending' })
}
function Wait-Offers {
    param([string]$OrderId, [int]$MaxSeconds = 45, [string]$Label)
    $deadline = (Get-Date).AddSeconds($MaxSeconds)
    while ((Get-Date) -lt $deadline) {
        $pending = Get-PendingOffers $OrderId
        if ($pending.Count -gt 0) { return $pending }
        Start-Sleep -Seconds 3
    }
    throw "Timeout: aucune offre pending pour $Label (order $OrderId) apres $MaxSeconds s."
}
function Accept-Offer {
    param([string]$OfferId)
    $code = ($OfferId -replace '-', '').Substring(0, 8).ToUpperInvariant()
    $payload = @{ data = @{ message = @{ text = "ACCEPTE $code" } } }
    Call-Api POST '/api/webhook/whatsapp' $payload
}

Load-State
Write-Output "== E2E batches - Phase $Phase =="
switch ($Phase) {
    1 {
        Write-Output '== Phase 1 : preparation + groupage + acceptation de lot =='
        $login = Call-Api POST '/api/auth/login' @{ username = 'admin'; password = $AdminPassword }
        Assert ($null -ne $login.token) 'login admin'
        $script:state.token = $login.token

        Call-Api POST "/api/vendors/$VendorId/credits/topup" @{ credits = 40 } | Out-Null
        Write-Output '  OK topup 40 credits'

        $i = 0
        foreach ($rid in $RiderIds) {
            Call-Api PUT "/api/riders/$rid/availability" @{ isAvailable = $true } | Out-Null
            Call-Api POST '/api/riders/location' @{
                riderUserId = $rid
                latitude    = $VendorLat + (0.0002 * $i)
                longitude   = $VendorLon + (0.0002 * $i)
            } | Out-Null
            $i++
        }
        Write-Output "  OK $($RiderIds.Count) livreurs en ligne avec position fraiche"

        $o1 = Create-Order 'E2E-C1'
        Confirm-Order $o1.id | Out-Null
        $o1 = Get-Order $o1.id
        Assert ($null -ne $o1.batchId) 'O1 rattachee a un lot'
        $o2 = Create-Order 'E2E-C2'
        Confirm-Order $o2.id | Out-Null
        $o2 = Get-Order $o2.id
        Assert ($null -ne $o2.batchId) 'O2 rattachee a un lot'
        Assert ($o1.batchId -eq $o2.batchId) 'O1 et O2 dans le MEME lot'
        $batch1 = $o1.batchId
        Write-Output "  Lot commun B1 : $batch1"

        $pending1 = Wait-Offers $o1.id 40 'lot B1'
        Assert ($pending1.Count -ge 1) "broadcast worker du lot (offres: $($pending1.Count))"

        $o3 = Create-Order 'E2E-C3'
        Confirm-Order $o3.id | Out-Null
        $o3 = Get-Order $o3.id
        Assert ($o3.batchId -ne $batch1) 'late-join bloque : O3 dans un NOUVEAU lot'
        Assert ((Get-PendingOffers $o3.id).Count -eq 0) 'aucune offre sur le nouveau lot'
        Write-Output "  Lot O3 B2 (nouveau) : $($o3.batchId)"

        $offer = $pending1[0]
        Accept-Offer $offer.id
        Start-Sleep -Seconds 3
        $o1b = Get-Order $o1.id
        $o2b = Get-Order $o2.id
        Assert ($o1b.status -eq 'RiderAssigned') 'O1 assignee (RiderAssigned)'
        Assert ($o2b.status -eq 'RiderAssigned') 'O2 assignee (RiderAssigned)'
        Assert ($null -ne $o1b.riderUserId) 'proprietaire livreur pose (RiderUserId) - fix #4'
        Assert ($o1b.riderUserId -eq $o2b.riderUserId) 'meme livreur sur les deux commandes'
        $final = Get-OffersList $o1.id
        Assert ((@($final | Where-Object { $_.status -eq 'Accepted' })).Count -ge 1) 'offre acceptee'
        Assert ((@($final | Where-Object { $_.status -eq 'Pending' })).Count -eq 0) 'autres offres expirees'

        $script:state.o1 = $o1.id
        $script:state.o2 = $o2.id
        $script:state.o3 = $o3.id
        $script:state.batch1 = $batch1
        Save-State
        Write-Output '== Phase 1 OK =='
    }

    2 {
        Write-Output '== Phase 2 : diffusion par fenetre ecoulee + elargissement lot =='
        $o3 = Get-Order $script:state.o3
        Write-Output "  O3 batch: $($o3.batchId) - attente diffusion fenetre (max 100 s)"
        $pending = Wait-Offers $o3.id 100 'lot fenetre B2'
        Assert ($pending.Count -ge 1) 'worker diffuse le lot quand la fenetre est ecoulee'

        $r = Call-Api POST "/api/orders/$($o3.id)/broadcast" $null
        Assert ($null -ne $r) 'broadcast vague 2 => 200 (fix #2 : plus de 409)'
        Write-Output "  OK Vague d'elargissement OK (offersCreated=$($r.offersCreated))"
        Save-State
        Write-Output '== Phase 2 OK =='
    }
    3 {
        Write-Output '== Phase 3 : annulation partielle puis totale d''un lot =='
        $o4 = Create-Order 'E2E-C4'
        Confirm-Order $o4.id | Out-Null
        $o5 = Create-Order 'E2E-C5'
        Confirm-Order $o5.id | Out-Null
        $o4 = Get-Order $o4.id
        $o5 = Get-Order $o5.id
        Assert ($o4.batchId -eq $o5.batchId) 'O4 et O5 groupees (lot B3)'
        $batch3 = $o4.batchId
        Write-Output "  Lot B3 : $batch3"
        $pending3 = Wait-Offers $o4.id 45 'lot B3'
        Assert ($pending3.Count -ge 1) 'B3 diffuse'

        Cancel-Order $o4.id | Out-Null
        $o4b = Get-Order $o4.id
        Assert ($o4b.status -eq 'Cancelled') 'O4 annulee'
        $stillPending = Get-PendingOffers $o5.id
        Assert ($stillPending.Count -ge 1) 'les offres du lot restent valides pour O5'

        Accept-Offer $stillPending[0].id
        Start-Sleep -Seconds 3
        $o5b = Get-Order $o5.id
        $o4c = Get-Order $o4.id
        Assert ($o5b.status -eq 'RiderAssigned') 'O5 assignee apres acceptation du lot'
        Assert ($null -ne $o5b.riderUserId) 'rider pose sur O5'
        Assert ($o4c.status -eq 'Cancelled') 'O4 reste annulee (non assignee)'

        $o6 = Create-Order 'E2E-C6'
        Confirm-Order $o6.id | Out-Null
        $o7 = Create-Order 'E2E-C7'
        Confirm-Order $o7.id | Out-Null
        $o6 = Get-Order $o6.id
        $o7 = Get-Order $o7.id
        Assert ($o6.batchId -eq $o7.batchId) 'O6 et O7 groupees (lot B4)'
        $batch4 = $o6.batchId
        $null = Wait-Offers $o6.id 45 'lot B4'
        Cancel-Order $o6.id | Out-Null
        Cancel-Order $o7.id | Out-Null
        Start-Sleep -Seconds 4
        $o7b = Get-Order $o7.id
        Assert ($o7b.status -eq 'Cancelled') 'O7 annulee'
        $b4Offers = Get-OffersList $o7.id
        Assert ((@($b4Offers | Where-Object { $_.status -eq 'Pending' })).Count -eq 0) 'aucune offre pending apres annulation totale'

        $o8 = Create-Order 'E2E-C8'
        Confirm-Order $o8.id | Out-Null
        $o8 = Get-Order $o8.id
        Assert ($null -ne $o8.batchId) 'O8 dans un lot'
        Assert ($o8.batchId -ne $batch4) 'O8 dans un NOUVEAU lot (B4 non reutilise)'
        Write-Output '== Phase 3 OK =='
    }
    4 {
        Write-Output '== Phase 4 : nettoyage des donnees E2E =='
        $conn = $env:WAZAP_CONNECTION_STRING
        if (-not $conn) {
            $secrets = dotnet user-secrets list --project 'c:\Dev\Wazap\WazapSln\src\Wazap.API' 2>&1
            $cs = $secrets | Where-Object { $_ -like 'ConnectionStrings:DefaultConnection=*' }
            if ($cs) { $conn = ($cs -split '=', 2)[1] }
        }
        Assert ($null -ne $conn) 'connection string disponible'
        $env:WAZAP_CONNECTION_STRING = $conn
        Push-Location 'c:\Dev\Wazap\tools\PurgeTestData'
        dotnet run --project . -- --confirm 2>&1 | Select-Object -Last 12
        Pop-Location
        Remove-Item $StateFile -ErrorAction SilentlyContinue
        Write-Output '== Phase 4 OK =='
    }
}

