# 💳 GeniusPay — Guide de mise en production

> L'intégration GeniusPay est **prête** dans le code. Ce guide détaille les étapes pour
> passer du mock (dev) au paiement réel.

## 1. Architecture du flux

```
POST /api/packs/buy ──► CreditTransaction (Pending)
        │                    │
        ▼                    ▼
  GeniusPayPaymentService ──► POST /api/v1/merchant/payments
  (initiation, checkout)        │
        │                        ▼
        ▼                 data.checkout_url (page de paiement hébergée)
  PaymentLink renvoyé au client
        │
        ▼ client paie sur geniuspay.ci
  Webhook POST /api/webhook/geniuspay (payment.success / payment.failed)
        │  ── signature HMAC-SHA256 vérifiée + anti-rejeu 5 min + montant vérifié
        ▼
  PackService.CompletePurchaseAsync → transaction Completed + crédits + WhatsApp
```

**Sécurité intégrée** : signature HMAC (`timestamp.payload` + `whsec_…`), comparaison à
temps constant, anti-rejeu 5 min, **vérification du montant**, **idempotence** (webhook
dupliqué ou réconciliation ne re-créditent jamais deux fois).

**Résilience** : si un webhook est perdu, `PaymentReconciliationWorker` interroge
`GET /payments/{reference}` toutes les `GeniusPay:ReconciliationMinutes` (5 min) et
complète/échoit les transactions `Pending` orphelines.

## 2. Étapes

### 2.1 Créer le compte GeniusPay
1. Aller sur **https://geniuspay.ci** → « Obtenir ma clé API » / « Créer mon compte ».
2. Choisir le profil (Startup ou Enterprise).
3. Créer une application → **Paramètres → API**.
4. Commencer en **mode sandbox** (transactions simulées).

> ✅ **Clés sandbox configurées et initiations réelles validées le 01/09/2026**
> (checkout URL `https://geniuspay.ci/checkout/SANDBOX_…` + retrieve du statut OK).

### 2.2 Configurer les secrets (user-secrets)
```powershell
cd c:\Dev\Wazap\WazapSln\src\Wazap.API
dotnet user-secrets set "GeniusPay:ApiKey" "pk_sandbox_xxxxxxxx"
dotnet user-secrets set "GeniusPay:ApiSecret" "sk_sandbox_xxxxxxxx"
dotnet user-secrets set "GeniusPay:WebhookSecret" "whsec_xxxxxxxx"
```

### 2.3 Configurer appsettings.json
```json
"GeniusPay": {
  "Enabled": true,
  "BaseUrl": "https://geniuspay.ci/api/v1/merchant",
  "ApiKey": "",          // user-secrets
  "ApiSecret": "",       // user-secrets
  "WebhookSecret": "",   // user-secrets
  "SuccessUrl": "https://VOTRE-DOMAINE/packs?success=1",
  "ErrorUrl": "https://VOTRE-DOMAINE/packs?error=1",
  "ReconciliationMinutes": 5
}
```

### 2.4 Configurer le webhook dans le dashboard GeniusPay
- URL : `https://VOTRE-DOMAINE/api/webhook/geniuspay`
- Événements : `payment.success`, `payment.failed`
- Le secret webhook (`whsec_…`) doit correspondre à `GeniusPay:WebhookSecret`.

### 2.5 Vérifier
```powershell
# L'API doit être accessible publiquement (tunnel en dev) pour recevoir le webhook.
# Achat : POST /api/packs/buy → response.paymentLink = page de paiement.
# Après paiement sandbox : la transaction passe Completed + crédits ajoutés.
```

> ✅ **E2E validé le 01/09/2026** : webhook signé envoyé via tunnel public → HTTP 200,
> transaction `Pending → Completed`, crédits crédités (pack « Petit » : 0 → 35).
>
> ℹ️ **ngrok** est bloqué par Windows Defender sur cette machine (faux positif « logiciel
> indésirable », quarantaine). Le tunnel de test utilisé est **cloudflared**
> (`tools\cloudflared.exe`) : `cloudflared tunnel --url http://localhost:5297`
> → URL `https://XXXX.trycloudflare.com` (change à chaque redémarrage).

## 3. Passage en production
1. Tester en sandbox de bout en bout.
2. Activer le mode live dans le dashboard GeniusPay.
3. Remplacer les clés sandbox par les clés live (`pk_live_…`, `sk_live_…`).
4. Vérifier le webhook en live.

## 4. Rollback / dev
- `GeniusPay.Enabled = false` → retour au `MockPaymentService` (succès immédiat, 2 s).
- `Payments.SimulateAsync = true` → le mock simule le flux asynchrone (lien de checkout
  fictif) pour tester le webhook sans GeniusPay.
