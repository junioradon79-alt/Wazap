# WAZAP — État du projet & progression

> Document de référence pour ne pas perdre la progression.
> Stack : ASP.NET Core 10 (Web API) + EF Core 10 + PostgreSQL + FluentValidation + JWT + xUnit.

## 1. Architecture (Clean Architecture pragmatique)

- **Wazap.Domain** : entités + enums + invariants métier (aucune dépendance).
- **Wazap.Application** : DTOs, ports (abstractions), services purs, validateurs, helpers, exceptions.
- **Wazap.Infrastructure** : `ApplicationDbContext`, migrations, `WhatChimpService`, `PasswordHasher`.
- **Wazap.API** : contrôleurs, services applicatifs, middleware, health check, DI.
- **tests/Wazap.UnitTests** : xUnit.

## 2. Entités & enums

| Entité | Champs clés |
|---|---|
| `Order` | Id, ClientName, ClientWhatsAppNumber, VendorWhatsAppNumber, RiderWhatsAppNumber, **VendorUserId**, **RiderUserId**, Description, Amount, CreatedAt, timestamps de statut |
| `OutboxMessage` | Id, Type, Payload (JSON), Status, RetryCount, CreatedAt, AvailableAt, ProcessedAt, LastError |
| `User` | Id, Username, PasswordHash, Role, **PhoneNumber**, CreatedAt |

- `OrderStatus` : PendingVendorConfirmation(1) → VendorConfirmed(2) → AwaitingRiderAcceptance(3) → RiderAssigned(4) → ReadyForPickup(5) → PickedUp(6) → InTransit(7) → Delivered(8) / Cancelled(9)
- `OutboxStatus` : Pending(1), Sent(2), Failed(3)
- `UserRole` : Admin(1), Vendor(2), Rider(3), Client(4)

## 3. Migrations (dossier `src/Wazap.Infrastructure/Migrations`)

1. `InitialCreate`
2. `AddOrderIndexes` (index Status + CreatedAt)
3. `AddOutbox` (table OutboxMessages + index ; supprime Contacts)
4. `AddUsers` (table Users + index unique Username)
5. `AddOrderUserLinks` (VendorUserId, RiderUserId)
6. `AddUserPhoneNumber` (PhoneNumber + index)

Appliquer : `dotnet ef database update --project src\Wazap.Infrastructure --startup-project src\Wazap.API`

## 4. Configuration (user-secrets — dev)

Clés stockées via `dotnet user-secrets set` :

- `ConnectionStrings:DefaultConnection`
- `WhatChimp:ApiToken`
- `WhatChimp:WebhookToken`
- `Jwt:Key`
- `SeedAdmin:Username` = `admin`
- `SeedAdmin:Password` = `Admin@Wazap2026` (à changer)

`appsettings.json` contient le non-secret : `WhatChimp:PhoneNumberId`, `WhatChimp:BaseUrl`, `Jwt:Issuer`, `Jwt:Audience`, `Outbox:MaxRetries`, `Outbox:PollingIntervalSeconds`.

## 5. Endpoints & autorisation

| Endpoint | Accès |
|---|---|
| `POST /api/auth/register` | Admin + rate limit « auth » |
| `POST /api/auth/login` | anonyme + rate limit « auth » |
| `POST /api/orders` | Admin, Vendor |
| `GET /api/orders` | Admin, Vendor |
| `GET /api/orders/{id}` | authentifié |
| `PUT /api/orders/{id}/status` | Admin, Vendor, Rider + autorisation ressource |
| `POST /api/webhook/whatsapp` | anonyme (token) + rate limit « webhook » |
| `GET /health` | anonyme |

Autorisation par ressource (`OrderService.EnsureCanUpdate`) : Admin = tout ; Vendor = ses commandes (`VendorUserId`), claim à la 1re confirmation ; Rider = ses courses (`RiderUserId`), claim à l'acceptation.

## 6. Outbox durable

`CreateOrder` écrit l'`Order` + l'`OutboxMessage` dans la même transaction. `OutboxBackgroundWorker` poll (5 s, lot de 10), retry exponentiel (5 s × 2^retry, max 300 s), 5 tentatives puis `Failed`. Sémantique at-least-once.

## 7. Sécurité / robustesse déjà en place

- Gestion globale des erreurs (`GlobalExceptionHandler`) → ProblemDetails (400/401/403/404/409/500).
- Webhook idempotent (gardes par état).
- Numéros normalisés E.164 (`PhoneNumberNormalizer`, code pays par défaut `33`).
- Hashage mots de passe PBKDF2 (100 000 itérations, sel, comparaison temps constant).
- Rate limiting webhook (100/min) + auth (10/min).
- Secrets hors du code ; migrations hors démarrage ; health check DB.

## 8. Tests

`dotnet test` → **22 tests** (Order, OutboxMessage, User, PhoneNumberNormalizer).

## 9. Lancer le projet

```powershell
dotnet ef database update --project src\Wazap.Infrastructure --startup-project src\Wazap.API
dotnet run --project src\Wazap.API
# POST /api/auth/login { "username":"admin", "password":"Admin@Wazap2026" }
```

## 10. Limites connues / prochaines étapes

- 403 sans corps (ajouter ProblemDetails 403).
- Swagger : `AddSecurityRequirement` non ajouté (API `Microsoft.OpenApi` v2).
- Pas de refresh token, verrouillage de compte, 2FA, reset mot de passe.
- `OrderService` encore dans la couche API (dépend du DbContext) → extraire via repository/`IApplicationDbContext`.
- Outbox multi-instances → `SKIP LOCKED` requis.
- Pas de CI/CD ni tests d'intégration.

## 11. CI/CD & scripts d'automatisation

- `.github/workflows/ci.yml` : pipeline GitHub Actions (restore → build → test → publish → artifact).
- `azure-pipelines.yml` : équivalent Azure DevOps.
- `scripts/deploy.ps1` : publication + upload FTP vers SmarterASP.NET (identifiants via variables d'environnement).
- `scripts/test-whatchimp.ps1` : test d'envoi WhatsApp + infos webhook (lit les user-secrets).
- `.gitignore` : exclut bin/obj/publish/secrets.

> Aucun dépôt Git n'est encore initialisé : exécuter `git init` puis pousser vers GitHub/Azure DevOps pour activer la CI.

