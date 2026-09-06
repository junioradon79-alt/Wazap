# WAZAP — Intégrations partenaires (API v1 & webhooks sortants)

## 1. Accès

### Clé API (lecture)
Endpoint de l'API publique v1 (lecture seule, aucune donnée personnelle) :
```
GET https://<hôte>/api/v1/{overview|zones|vendors|orders|packs}
```
En-tête obligatoire : `X-Api-Key: <votre-clé>` · Rate limit : 240 requêtes/min (configurable).
Sans clé : 401. Clé non configurée côté serveur : 503.

Génération d'une clé : `dotnet run --project tools/GenerateApiKey` puis renseigner
`PublicApi__Keys__0` (variable d'environnement prod).

### Exemples
```bash
curl -H "X-Api-Key: $KEY" https://<hôte>/api/v1/overview
curl -H "X-Api-Key: $KEY" "https://<hôte>/api/v1/orders?zone=Marcory&from=2026-08-01&status=Delivered&limit=50"
curl -H "X-Api-Key: $KEY" https://<hôte>/api/v1/packs
```

## 2. Webhooks sortants (événements)

WAZAP notifie vos serveurs par HTTP POST dès qu'un événement se produit. Livraison **fiable**
(file outbox) : retries avec backoff exponentiel, en-tête `Retry-After` honoré.

### S'abonner (admin)
```
POST /api/admin/webhooks        # { "name": "...", "url": "https://.../hook", "secret": "...", "events": ["order.created","order.status_changed"] }
GET/PUT/DELETE /api/admin/webhooks[/{id}]   ; PUT /api/admin/webhooks/{id}/enabled {"enabled":true}
```
Événements disponibles : `order.created`, `order.status_changed` (majuscules insensibles).

### Payload reçu
```json
{
  "event": "order.status_changed",
  "occurredAt": "2026-09-06T13:00:00Z",
  "data": { "orderId": "…", "from": "RiderAssigned", "to": "Delivered", "at": "…" }
}
```

### En-têtes
- `X-Wazap-Delivery` : identifiant unique de la livraison (pour idempotence côté receveur).
- `X-Wazap-Event` : type d'événement.
- `X-Wazap-Timestamp` : horodatage Unix de l'événement.
- `X-Wazap-Signature` : `sha256=<HMAC-SHA256 hex du corps brut>` calculé avec votre `secret`.

### Vérification de signature (C#)
```csharp
using System.Security.Cryptography;
using System.Text;

static bool Verify(string body, string secret, string signature)
{
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var expected = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    return string.Equals(expected, signature, StringComparison.Ordinal);
}
```

### Comportement d'échec
- 2xx → acquitté. 3xx/4xx hors liste → non suivi (réessai) selon code.
- **Réponse 400/401/403/404/405/410** → échec **permanent** : l'événement est marqué `Failed`
  (visible dans `/health/details` → `outbox.failed`) et une alerte est émise.
- **429/5xx/408/425** → nouvelle tentative (délai : `Retry-After` si fourni, sinon backoff), max 5.
- Répondez le plus vite possible (`200` dès réception), traitez en arrière-plan ; utilisez
  `X-Wazap-Delivery` pour dédupliquer.
