# WAZAP — État du projet & progression

> Document de référence pour ne pas perdre la progression.
> Stack : ASP.NET Core 10 (Web API + Blazor Server) + EF Core 10 + PostgreSQL + FluentValidation + JWT + xUnit.

## 1. Architecture (Clean Architecture pragmatique)

- **Wazap.Domain** : entités + enums + invariants métier (aucune dépendance).
- **Wazap.Application** : DTOs, ports (`IApplicationDbContext`, `IWhatsAppSender`, `IPaymentService`…), **services d'application** (`OrderService`, `DeliveryOfferService`, `WhatsAppOrchestrationService`, validators, helpers, exceptions).
- **Wazap.Infrastructure** : `ApplicationDbContext` (implémente `IApplicationDbContext`), migrations, `WhatChimpService`, `MockPaymentService`, `PasswordHasher`.
- **Wazap.API** : contrôleurs, workers, middleware, health check, DI, Blazor (dashboard) — **sans logique métier**.
- **tests/Wazap.UnitTests** : xUnit.

## 2. Entités & enums

| Entité | Champs clés |
|---|---|
| `Order` | Id, ClientName, ClientWhatsAppNumber, VendorWhatsAppNumber, RiderWhatsAppNumber, **VendorUserId**, **RiderUserId**, **BatchId**, Description, Amount, CreatedAt, timestamps de statut |
| `OutboxMessage` | Id, Type, Payload (JSON), Status, RetryCount, CreatedAt, AvailableAt, ProcessedAt, LastError |
| `User` | Id, Username, PasswordHash, Role, **PhoneNumber**, CreatedAt, **géoloc** (Latitude/Longitude/LocationUpdatedAt/IsAvailable/LocationSharingEnabled), **Zone** (quartier déclaré, téléphones basiques), **Credits** (packs prépayés), **ReferralCode**/ReferredByUserId |
| `DeliveryOffer` | Id, OrderId (null pour un lot), **BatchId**, RiderUserId, BatchNumber, Status (Pending/Accepted/Declined/Expired), SentAt, RespondedAt |
| `DeliveryBatch` | Id, VendorUserId, Status (Open/Assigned/Cancelled), CreatedAt, AssignedAt, RiderUserId, RiderWhatsAppNumber — **livraisons groupées** (le même livreur prend plusieurs commandes d'un même vendeur) |
| `CreditTransaction` | Id, VendorId (FK → Users), **PackName**, Amount, CreditsPurchased, CreatedAt, TransactionReference, Status (Pending/Completed/Failed) |

- `OrderStatus` : PendingVendorConfirmation(1) → VendorConfirmed(2) → AwaitingRiderAcceptance(3) → RiderAssigned(4) → ReadyForPickup(5) → PickedUp(6) → InTransit(7) → Delivered(8) / Cancelled(9)
- `OutboxStatus` : Pending(1), Sent(2), Failed(3)
- `UserRole` : Admin(1), Vendor(2), Rider(3), Client(4)
- `TransactionStatus` : Pending(0), Completed(1), Failed(2)

## 3. Migrations (dossier `src/Wazap.Infrastructure/Migrations`)

1. `InitialCreate` (20260831212425) — Orders, OutboxMessages, Users
2. `AddVendorAndCreditTransaction` (20260901161922) — `Users.Credits` + `CreditTransactions` (DDL idempotent)
3. `AddGeolocationAndDeliveryOffers` (20260901162922) — colonnes géoloc + `DeliveryOffers` (DDL idempotent)
4. `AddPackNameToCreditTransactions` (20260901171612) — colonne `PackName` (DDL idempotent)
5. `AddZoneToUsers` (20260901175202) — colonne `Zone` (matching téléphones basiques, DDL idempotent)
6. `AddReferralToUsers` (20260901194805) — `Users.ReferralCode`/`ReferredByUserId` (parrainage)
7. `AddDeliveryBatches` (20260901215856) — table `DeliveryBatches` + `Orders.BatchId` + `DeliveryOffers.BatchId`/OrderId nullable (livraisons groupées, DDL idempotent)
8. `AddLoginSecurity` (20260902123854) — `Users.FailedLoginAttempts`/`LockedUntilUtc` (verrouillage anti force-brute, DDL idempotent)

Appliquer : `dotnet ef database update --project src\Wazap.Infrastructure --startup-project src\Wazap.API`

## 4. Configuration (user-secrets — dev)

Clés stockées via `dotnet user-secrets set` :

- `ConnectionStrings:DefaultConnection`
- `WhatChimp:ApiToken`
- `WhatChimp:WebhookToken`
- `Jwt:Key`
- `SeedAdmin:Username` = `admin`
- `SeedAdmin:Password` = `Admin@Wazap2026` (à changer)

`appsettings.json` contient le non-secret : `WhatChimp:PhoneNumberId`, `WhatChimp:BaseUrl`, `Jwt:Issuer`, `Jwt:Audience`, `Outbox:MaxRetries`, `Outbox:PollingIntervalSeconds`, `Geo` (rayon/fraîcheur/exclusivité/timeout/rétention), `Packs` (catalogue 6 packs : Mini 1000 F/6 · Découverte 2500/15 · Petit 5000/35 · Moyen 10000/80 · Grand 25000/220 · Pro 100000/1000), `GeniusPay` (BaseUrl/Enabled, clés en user-secrets), `Payments:SimulateAsync` (test flux asynchrone).

## 5. Endpoints & autorisation

| Endpoint | Accès |
|---|---|
| `POST /api/auth/register` | Admin + rate limit « auth » |
| `POST /api/auth/login` | anonyme + rate limit « auth » |
| `POST /api/orders` | Admin, Vendor — consomme 1 crédit (402 si insuffisant) |
| `GET /api/orders` | Admin, Vendor |
| `GET /api/orders/{id}` | authentifié |
| `PUT /api/orders/{id}/status` | Admin, Vendor, Rider + autorisation ressource |
| `POST /api/orders/{id}/broadcast` | Admin, Vendor — diffuse les offres aux livreurs |
| `GET /api/orders/{id}/offers` | Admin, Vendor — offres de la commande |
| `GET/PUT/POST /api/riders` | Admin (liste) · Rider/Admin (location, availability, location-sharing) avec contrôle ressource |
| `GET /` et `/share-location` | Blazor Server — dashboard **admin protégé (cookie)**, share-location livreur (login JWT) |
| `GET /login` · `POST /api/auth/ui/login` | connexion dashboard admin (cookie) |
| `GET /api/vendors` | Admin, Vendor (le vendor ne voit que sa fiche) |
| `PUT /api/vendors/{id}/address` | Admin, Vendor (propriétaire) — géocodage |
| `POST /api/vendors/{id}/credits/topup` | **Admin uniquement** — octroi de crédits |
| `GET /api/vendors/{id}/transactions` | Admin, Vendor (propriétaire) — historique d'achats |
| `GET /api/packs` | anonyme — catalogue des packs prépayés |
| `POST /api/packs/buy` | Admin, Vendor (le vendor n'achète que pour lui) — initiation, lien de checkout si asynchrone |
| `POST /api/webhook/geniuspay` | anonyme (signature HMAC-SHA256) — confirmation de paiement GeniusPay |

## 6. Outbox durable

`CreateOrder` écrit l'`Order` + l'`OutboxMessage` (+ décrément de crédit) dans la même transaction. `OutboxBackgroundWorker` réclame les messages avec **`FOR UPDATE SKIP LOCKED`** (sûr en multi-instances), poll 5 s, lot de 10, retry exponentiel, 5 tentatives puis `Failed`. Sémantique at-least-once.

## 7. Sécurité / robustesse déjà en place

- Gestion globale des erreurs (`GlobalExceptionHandler`) → ProblemDetails (400/401/**402**/403/404/409/**423**/500). Les **401/403** du middleware JWT renvoient aussi un corps ProblemDetails (plus de réponse vide).
- **Pay-per-use à l'acceptation** : le crédit n'est **débité que lorsqu'un livreur accepte** la course (1 crédit par commande, par lot au prorata) ; la création d'une commande/course est **gratuite** (402 « Crédits insuffisants » uniquement si le solde est insuffisant au moment de l'acceptation).
- **Matching livreurs à 2 niveaux** : GPS frais (Haversine) puis **ZONE déclarée** (téléphones basiques sans GPS) — commandes WhatsApp `ZONE <quartier>`, `DISPO`, `INDISPO`, `AIDE`. Une course est possible avec une **zone seule** (pas de GPS vendeur requis).
- **Livraison à la demande (vendeur)** : commande WhatsApp **`LIVRAISON <détail + adresse client>`** → commande confirmée → diffusion **immédiate** aux livreurs. Le **téléphone du client** peut être inclus (`… tel 0708091011`) pour les notifications automatiques.
- **Automatisations livreur** : `ACCEPTE <code>` (prendre) · `RECU` (colis récupéré → `InTransit`) · `LIVRE <code>` (livré → `Delivered`, **par client**) / `LIVRE TOUT`. **Tournée multi-clients** : le livreur reçoit la liste détaillée des livraisons (#code — client — adresse) et chaque client est notifié à sa livraison. `LIVRE` sans code est refusé si plusieurs courses sont en cours.
- **Livraisons groupées fiabilisées** (validation 02/09) : un lot déjà diffusé n'accepte plus de nouvelles commandes (late-join), les vagues d'élargissement re-fonctionnent par lot, l'acceptation d'un lot ignore les commandes annulées et pose `RiderUserId`, l'annulation de la dernière commande active clôt le lot et expire ses offres. Vérifié par E2E : `scripts/e2e-batch-validation.ps1`.
- **Parcours acheteur (PWA)** : à la confirmation d'une commande client, WAZAP envoie au client le lien `suivi.html?id=…` ; le client valide **position/adresse** → déclenchement **automatique** de la recherche des livreurs (`/api/client/orders/{id}/coordinates`, migration `AddBuyerTracking`). **Liens Google Maps** (retrait vendeur + livraison client) envoyés au livreur ; le **vendeur est notifié** dès la validation client.
- Webhook tolérant (camelCase/snake_case, boutons, live location, ACCEPTE code court).
- Numéros normalisés E.164 (`PhoneNumberNormalizer`, code pays par défaut `33`) **+ gestion numérotation ivoirienne** : matching `SameSubscriber` (8 derniers chiffres) pour ancienne (+225+8) vs nouvelle (+225+10) numérotation ; **auto-réparation** du numéro stocké depuis le `wa_id` reçu au webhook ; parseur webhook compatible format réel WhatChimp (`chat_id`/`user_message`).
- Hashage mots de passe PBKDF2 (100 000 itérations, sel, comparaison temps constant).
- Rate limiting webhook (100/min) + auth (10/min) + **client** (60/min, endpoints publics du parcours acheteur).
- Secrets hors du code ; migrations hors démarrage ; health check DB.
- Top-up crédits réservé Admin ; dashboard réservé Admin ; contrôle ressource vendeur.
- **Verrouillage anti force-brute** : après `Security:MaxFailedLoginAttempts` (5) échecs consécutifs, le compte est bloqué `Security:LockoutMinutes` (15 min) → **HTTP 423** avec corps ProblemDetails (migration `AddLoginSecurity`).
- **Webhook GeniusPay vérifié** : HMAC-SHA256 (`timestamp.payload` + `whsec_…`), anti-rejeu 5 min, **montant vérifié**, idempotent.
- **Réconciliation** : `PaymentReconciliationWorker` (5 min) complète les transactions Pending dont le webhook a été perdu.
- **Paiement packs** : GeniusPay (checkout hébergé) si activé, sinon mock (dev/test, option `Payments:SimulateAsync`). Guide : **`GENIUSPAY_SETUP.md`**.
- **Offre de découverte (Trial)** : `Trial:FreeCreditsOnRegistration` (15) commandes offertes à l'inscription de chaque nouveau vendeur (`AuthService.RegisterAsync`), tracée en `CreditTransaction` (réf `TRIAL-…`, montant 0).
- **Guide d'onboarding WhatsApp** : après tout enrôlement réussi (vendeur/livreur/autre), envoi best-effort d'un mode d'emploi en **≤ 3 étapes simples** adapté au rôle (`BuildOnboardingGuide`).

## 8. Tests

`dotnet test` → **98 tests** (Order, DeliveryBatch, DeliveryOffer, OutboxMessage, User, CreditTransaction, GeoDistance, MockPayment, WhatsAppOrchestration, PhoneNumberNormalizer, validators, GeniusPay).

## 9. Lancer le projet

```powershell
dotnet ef database update --project src\Wazap.Infrastructure --startup-project src\Wazap.API
dotnet run --project src\Wazap.API
# POST /api/auth/login { "username":"admin", "password":"Admin@Wazap2026" }
# UI : http://localhost:5297/ (dashboard) et /share-location
```

## 10. Limites connues / prochaines étapes

- 403 sans corps (ajouter ProblemDetails 403 si besoin).
- Swagger : `AddSecurityRequirement` non ajouté (API `Microsoft.OpenApi` v2).
- Pas de refresh token, 2FA ni reset de mot de passe oublié (le changement de mot de passe connecté existe via `/app/account`).
- ~~`OrderService` dans la couche API~~ → ✅ déplacé dans `Wazap.Application` avec `DeliveryOfferService` via le port **`IApplicationDbContext`** (02/09).
- Outbox multi-instances → `SKIP LOCKED` requis.
- Endpoints livreurs (location/availability) ouverts (flux appareil par Guid) — à sécuriser avec l'app mobile.
- Alertes WhatsApp crédits en **message texte** — templates approuvés requis en production.
- Approbation Meta des templates `order_received`/`order_confirm`/`rider_offer` en attente.

## 11. CI/CD & scripts d'automatisation

- `.github/workflows/ci.yml` : pipeline GitHub Actions (restore → build → test → publish → artifact).
- `azure-pipelines.yml` : équivalent Azure DevOps.
- `scripts/deploy.ps1` : publication + upload FTP vers SmarterASP.NET (identifiants via variables d'environnement).
- `scripts/test-whatchimp.ps1` : test d'envoi WhatsApp + infos webhook (lit les user-secrets).
- `.gitignore` : exclut bin/obj/publish/secrets.

> Dépôt Git initialisé sur `main` — pousser vers GitHub/Azure DevOps pour activer la CI.

## 12. Production (SmarterASP.NET)

- **URL** : https://junioradon79gm-001-site1.jtempurl.com/ (domaine en attente)
- **Provider** : PostgreSQL (SmarterASP.NET)
- **Schéma** : créé (7 migrations appliquées)
- **Admin** : seedé automatiquement au démarrage
- **Secrets** : injectés via `web.config` (`<environmentVariables>`) sur le serveur
- **Détails/credentials sensibles** : voir le fichier local **`DEPLOYMENT.md`** (gitignoré)

### Déploiement (⚠️ procédure du 02/09/2026)

La prod tourne en **self-contained win-x64** (le serveur n'a pas forcément le runtime .NET 10) :
```powershell
# 1. Publication self-contained
dotnet publish src\Wazap.API\Wazap.API.csproj -c Release -r win-x64 --self-contained true -o artifacts\publish-win64

# 2. Réécrire artifacts\publish-win64\web.config AVANT l'upload :
#    - processPath=".\Wazap.API.exe" arguments="" hostingModel="OutOfProcess"
#    - les env vars s'écrivent <environmentVariable name="X" value="Y" />  (⚠️ PAS <add> → 500 IIS)
#    - stdoutLogEnabled=false en production

# 3. Upload FTP : app_offline.htm → tous les fichiers → supprimer app_offline.htm
#    (voir DEPLOYMENT.md pour les identifiants et les valeurs des env vars)
```

### Appliquer les migrations
```powershell
dotnet ef database update --project src\Wazap.Infrastructure --startup-project src\Wazap.API
```

| `GET /api/dashboard/summary` | **Admin** — métriques du tableau de bord |
| `POST /api/webhook/whatsapp` | anonyme (token) + rate limit « webhook » |
| `GET /` et `/share-location` | Blazor Server (dashboard + partage GPS) |
| `GET /health` | anonyme |

Autorisation par ressource : Admin = tout ; Vendor = ses commandes (`VendorUserId`) et son compte vendeur ; Rider = ses courses (`RiderUserId`), claim à l'acceptation.
