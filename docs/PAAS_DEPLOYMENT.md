# WAZAP — déploiement PaaS (Render / Railway / Azure App Service)
# Statut : PRÉPARATION. La production actuelle reste SmarterASP (self-contained + FTP différentiel).
# Ce guide décrit la migration vers un PaaS managé quand le marché est validé.

## 1. Pourquoi
- Build/déploiement simples (image OCI `Dockerfile` fourni, runtime managé).
- PostgreSQL managé (backups auto, scalabilité) au lieu de SmarterASP.
- Logs JSON déjà émis en production (`AddJsonConsole`) → collectables tels quels.
- Health prêt : `/health` (liveness DB) et `/health/details` (base, outbox, workers) + `/metrics` (Prometheus).

## 2. Choix possibles
| Plateforme | Points forts | À noter |
|---|---|---|
| Render (Web Service) | Simple, auto-deploy sur push git | PostgreSQL via Render ou externe |
| Railway | Build Docker natif, réseaux privés | — |
| Azure App Service | Écosystème Microsoft, intégration AAD | plan P nécessite carte |

Recommandation : **Render ou Railway** avec un **PostgreSQL managé** (ou garder `db_acdd27_wazap` tant que possible).

## 3. Variables d'environnement à fournir (au minimum)
Toutes les valeurs = celles du `web.config` SmarterASP actuel (voir DEPLOYMENT.md).
- `ConnectionStrings__DefaultConnection` : chaîne PostgreSQL (format Npgsql).
- `WhatChimp__ApiToken`, `WhatChimp__PhoneNumberId` (+ `WebhookToken` si présent).
- `Jwt__Key` (≥ 64 hex), `SeedAdmin__Username`, `SeedAdmin__Password`.
- `GeniusPay__ApiKey`, `GeniusPay__ApiSecret`, `GeniusPay__WebhookSecret`,
  `GeniusPay__SuccessUrl`, `GeniusPay__ErrorUrl` (et `GeniusPay__Enabled=true`).
- Optionnelles : `PublicApi__Keys__0` (clé API v1), `Monitoring__WebhookUrl`,
  `Retention__Enabled` (false par défaut), `Outbox__PollingIntervalSeconds`, `Outbox__MaxRetries`.

## 4. Migrations
Les migrations ne sont PAS appliquées au démarrage (stratégie explicite) :
- **Avant** chaque déploiement : `dotnet ef database update --project src/Wazap.Infrastructure --startup-project src/Wazap.API --connection "$CONNECTION"` (le workflow GitHub SmarterASP le fait déjà automatiquement).
- Sur PaaS : prévoir une étape de release/migration ou un job one-shot.

## 5. Build & run local de l'image (validation)
```bash
docker build -t wazap-api .
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__DefaultConnection="Host=localhost;..." \
  wazap-api
curl http://localhost:8080/health        # Healthy
curl http://localhost:8080/health/details
```

## 6. Dev local (PostgreSQL + API) — docker-compose.dev.yml
```bash
docker compose -f docker-compose.dev.yml up
```
