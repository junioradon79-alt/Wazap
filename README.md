# WAZAP â€” Ã‰tat du projet & progression

> Document de rÃ©fÃ©rence pour ne pas perdre la progression.
> Stack : ASP.NET Core 10 (Web API) + EF Core 10 + PostgreSQL + FluentValidation + JWT + xUnit.

## 1. Architecture (Clean Architecture pragmatique)

- **Wazap.Domain** : entitÃ©s + enums + invariants mÃ©tier (aucune dÃ©pendance).
- **Wazap.Application** : DTOs, ports (abstractions), services purs, validateurs, helpers, exceptions.
- **Wazap.Infrastructure** : `ApplicationDbContext`, migrations, `WhatChimpService`, `PasswordHasher`.
- **Wazap.API** : contrÃ´leurs, services applicatifs, middleware, health check, DI.
- **tests/Wazap.UnitTests** : xUnit.

## 2. EntitÃ©s & enums

| EntitÃ© | Champs clÃ©s |
|---|---|
| `Order` | Id, ClientName, ClientWhatsAppNumber, VendorWhatsAppNumber, RiderWhatsAppNumber, **VendorUserId**, **RiderUserId**, Description, Amount, CreatedAt, timestamps de statut |
| `OutboxMessage` | Id, Type, Payload (JSON), Status, RetryCount, CreatedAt, AvailableAt, ProcessedAt, LastError |
| `User` | Id, Username, PasswordHash, Role, **PhoneNumber**, CreatedAt, **gÃ©oloc** (Latitude/Longitude/LocationUpdatedAt/IsAvailable/LocationSharingEnabled), **Credits** (packs prÃ©payÃ©s) |
| `DeliveryOffer` | Id, OrderId, RiderUserId, BatchNumber, Status (Pending/Accepted/Declined/Expired), SentAt, RespondedAt |
| `CreditTransaction` | Id, VendorId (FK â†’ Users), Amount, CreditsPurchased, CreatedAt, TransactionReference, Status (Pending/Completed/Failed) |

- `OrderStatus` : PendingVendorConfirmation(1) â†’ VendorConfirmed(2) â†’ AwaitingRiderAcceptance(3) â†’ RiderAssigned(4) â†’ ReadyForPickup(5) â†’ PickedUp(6) â†’ InTransit(7) â†’ Delivered(8) / Cancelled(9)
- `OutboxStatus` : Pending(1), Sent(2), Failed(3)
- `UserRole` : Admin(1), Vendor(2), Rider(3), Client(4)
- `TransactionStatus` : Pending(0), Completed(1), Failed(2)

## 3. Migrations (dossier `src/Wazap.Infrastructure/Migrations`)

1. `InitialCreate` (20260831212425) â€” Orders, OutboxMessages, Users
2. `AddVendorAndCreditTransaction` (20260901161922) â€” `Users.Credits` + `CreditTransactions` (DDL idempotent)
3. `AddGeolocationAndDeliveryOffers` (20260901162922) â€” colonnes gÃ©oloc + `DeliveryOffers` (DDL idempotent)

Appliquer : `dotnet ef database update --project src\Wazap.Infrastructure --startup-project src\Wazap.API`

## 4. Configuration (user-secrets â€” dev)

ClÃ©s stockÃ©es via `dotnet user-secrets set` :

- `ConnectionStrings:DefaultConnection`
- `WhatChimp:ApiToken`
- `WhatChimp:WebhookToken`
- `Jwt:Key`
- `SeedAdmin:Username` = `admin`
- `SeedAdmin:Password` = `Admin@Wazap2026` (Ã  changer)

`appsettings.json` contient le non-secret : `WhatChimp:PhoneNumberId`, `WhatChimp:BaseUrl`, `Jwt:Issuer`, `Jwt:Audience`, `Outbox:MaxRetries`, `Outbox:PollingIntervalSeconds`, `Geo` (rayon/fraÃ®cheur/exclusivitÃ©/timeout/rÃ©tention), `Packs` (catalogue 5 packs).

## 5. Endpoints & autorisation

| Endpoint | AccÃ¨s |
|---|---|
| `POST /api/auth/register` | Admin + rate limit Â« auth Â» |
| `POST /api/auth/login` | anonyme + rate limit Â« auth Â» |
| `POST /api/orders` | Admin, Vendor |
| `GET /api/orders` | Admin, Vendor |
| `GET /api/orders/{id}` | authentifiÃ© |
| `PUT /api/orders/{id}/status` | Admin, Vendor, Rider + autorisation ressource |
| `POST /api/orders/{id}/broadcast` | Admin, Vendor â€” diffuse les offres aux livreurs |
| `GET /api/orders/{id}/offers` | Admin, Vendor â€” offres de la commande |
| `GET/PUT/POST /api/riders` | liste, location, availability, location-sharing |
| `GET/PUT/POST /api/vendors` | liste, adresse gÃ©ocodÃ©e, top-up crÃ©dits |
| `GET /api/packs` | anonyme â€” catalogue des packs prÃ©payÃ©s |
| `GET /api/dashboard/summary` | anonyme â€” mÃ©triques du tableau de bord |
| `POST /api/webhook/whatsapp` | anonyme (token) + rate limit Â« webhook Â» |
| `GET /` et `/share-location` | Blazor Server (dashboard + partage GPS) |
| `GET /health` | anonyme |

Autorisation par ressource (`OrderService.EnsureCanUpdate`) : Admin = tout ; Vendor = ses commandes (`VendorUserId`), claim Ã  la 1re confirmation ; Rider = ses courses (`RiderUserId`), claim Ã  l'acceptation.

## 6. Outbox durable

`CreateOrder` Ã©crit l'`Order` + l'`OutboxMessage` dans la mÃªme transaction. `OutboxBackgroundWorker` poll (5 s, lot de 10), retry exponentiel (5 s Ã— 2^retry, max 300 s), 5 tentatives puis `Failed`. SÃ©mantique at-least-once.

## 7. SÃ©curitÃ© / robustesse dÃ©jÃ  en place

- Gestion globale des erreurs (`GlobalExceptionHandler`) â†’ ProblemDetails (400/401/403/404/409/500).
- Webhook idempotent (gardes par Ã©tat).
- NumÃ©ros normalisÃ©s E.164 (`PhoneNumberNormalizer`, code pays par dÃ©faut `33`).
- Hashage mots de passe PBKDF2 (100 000 itÃ©rations, sel, comparaison temps constant).
- Rate limiting webhook (100/min) + auth (10/min).
- Secrets hors du code ; migrations hors dÃ©marrage ; health check DB.

## 8. Tests

`dotnet test` â†’ **46 tests** (Order, OutboxMessage, User, CreditTransaction, DeliveryOffer, GeoDistance, PhoneNumberNormalizer).

## 9. Lancer le projet

```powershell
dotnet ef database update --project src\Wazap.Infrastructure --startup-project src\Wazap.API
dotnet run --project src\Wazap.API
# POST /api/auth/login { "username":"admin", "password":"Admin@Wazap2026" }
```

## 10. Limites connues / prochaines Ã©tapes

- 403 sans corps (ajouter ProblemDetails 403).
- Swagger : `AddSecurityRequirement` non ajoutÃ© (API `Microsoft.OpenApi` v2).
- Pas de refresh token, verrouillage de compte, 2FA, reset mot de passe.
- `OrderService` encore dans la couche API (dÃ©pend du DbContext) â†’ extraire via repository/`IApplicationDbContext`.
- Outbox multi-instances â†’ `SKIP LOCKED` requis.
- Pas de CI/CD ni tests d'intÃ©gration.

## 11. CI/CD & scripts d'automatisation

- `.github/workflows/ci.yml` : pipeline GitHub Actions (restore â†’ build â†’ test â†’ publish â†’ artifact).
- `azure-pipelines.yml` : Ã©quivalent Azure DevOps.
- `scripts/deploy.ps1` : publication + upload FTP vers SmarterASP.NET (identifiants via variables d'environnement).
- `scripts/test-whatchimp.ps1` : test d'envoi WhatsApp + infos webhook (lit les user-secrets).
- `.gitignore` : exclut bin/obj/publish/secrets.

> DÃ©pÃ´t Git initialisÃ© sur `main` â€” pousser vers GitHub/Azure DevOps pour activer la CI.

## 12. Production (SmarterASP.NET)

- **URL** : https://junioradon79gm-001-site1.jtempurl.com/ (domaine en attente)
- **Provider** : PostgreSQL (SmarterASP.NET)
- **SchÃ©ma** : crÃ©Ã© (migration `InitialCreate`)
- **Admin** : seedÃ© automatiquement au dÃ©marrage
- **Secrets** : injectÃ©s via `web.config` (`<environmentVariables>`) sur le serveur
- **DÃ©tails/credentials sensibles** : voir le fichier local **`DEPLOYMENT.md`** (gitignorÃ©)

### DÃ©ploiement
```powershell
# Voir scripts/deploy.ps1 (publication + upload FTP)
# Appliquer la migration :
dotnet ef database update --project src\Wazap.Infrastructure --startup-project src\Wazap.API
```

