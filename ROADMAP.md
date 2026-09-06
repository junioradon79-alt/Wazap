# 🗺️ WAZAP — Feuille de route & scaling (03/09/2026)

> Document de synthèse : reste à faire + propositions de scaling.
> Sources : WAZAP_SESSION_NOTES.md, README.md, DEPLOYMENT.md, prospection/*, MARKETING_STRATEGY.md.

## État actuel (résumé)
- **Produit** : livraison à la demande WhatsApp (vendeur→livreur→client), tournées groupées, parcours acheteur PWA avec suivi, packs de crédits prépayés (GeniusPay LIVE), parrainage, trial 15 commandes, auth renforcée (refresh/2FA/reset).
- **Tests** : 117/117 · **Prod** : Healthy (SmarterASP self-contained) · **Base dev** : PostgreSQL local `wazapdev` (séparée) · **CI** GitHub Actions ✅.
- **Tests réels validés** : flux livreur complet (ACCEPTE→RECU→LIVRE), groupage multi-clients (diffusion différée 30 s).

---

## A. Actions utilisateur (dashboards externes) — me prévenir pour activer
1. **Approbation Meta des 15 templates** (tous `Submitted`) → dès `Approved`, j'active dans appsettings + déploie (5 min) :
   - `order_received`, `order_confirm`, `rider_offer`, `rider_batch_offer` (+ `_btn` bouton), `rider_assigned_client`, `rider_assigned_vendor`
   - crédits : `credit_purchase`, `low_credit`, `no_credit` ; prospection : `prospect_approach/followup/offer` ; recrutement : `rider_recruit`, `rider_company`
2. **Clé API Google Places** (`AIza…`, carte bancaire requise) → collecte complète 13 zones × 33 secteurs.
3. **Vidéo démo 30 s** hébergée (URL publique) → variable {{3}} des templates prospect + campagne.
4. **Nom de domaine** propre (remplacer le jtempurl.com).

## B. À relancer (service externe / timing)
1. **Collecte Overpass complète** (33 secteurs, 13 communes) — Overpass public saturé depuis le 03/09.
   Outil **durci le 06/09** : sonde de disponibilité des miroirs (ordre dynamique, sortie rapide si tout
   est down), option `--timeout=<s>` (défaut 120), réponses « busy/timeout » d'Overpass traitées comme
   de vrais échecs réessayables (plus de zone marquée « terminée » sans données). Relancer quand la
   charge baisse :
   `dotnet run --project tools\ProspectCollectorOsm -- --out-dir=prospection\out_osm`
2. **Campagne WhatsApp prospects** (72 mobiles qualifiés `Prospects_campagne_mobiles_20260902.csv`) — dès approbation de `prospect_approach` :
   `$env:WHATCHIMP_API_TOKEN=… ; dotnet run --project tools\WhatsAppCampaign -- prospection\Prospects_campagne_mobiles_20260902.csv --zone=Marcory`
3. **Purge des comptes de test** en base prod (`test_reel_utilisateur`, `test_vendeur_cocody`) après la fin des essais réels (CleanupTestVendors).

## C. Chantiers code recommandés (par ordre d'impact)
1. **Fallback timeout 5 min** : ✅ FAIT (03/09, commit 29c28ab) — au timeout sans livreur, la commande est annulée (aucun crédit débité) et le vendeur est notifié avec invitation à renvoyer LIVRAISON.
2. **Conversion numéros 8→10 chiffres** : ✅ **table VALIDÉE le 06/09** (plan officiel ARTCI 2021 :
   communiqué artci.ci 11/08/2020 + plan NNP + recoupement wa_id réels) — Orange→07 (15 préfixes),
   MTN→05 (11), Moov/ex-Atlantique→01 (5), fixes 2x/3x et préfixes fermés exclus (tests exhaustifs).
   **Toujours désactivée** (`Enabled=false`). Reste avant activation : (1) ~~brancher la conversion~~
   ✅ **point d'appel branché (06/09)** : `WhatChimpService.PrepareRecipient` (tous les envois
   templates + texte convertis au vol) + inscription `AuthService.RegisterAsync` (stockage direct au
   format courant) — inopérant tant que `Enabled=false` ; (2) **test réel WhatsApp** vers un numéro
   converti. Approche conservée en attendant : matching SameSubscriber (8 derniers) + auto-réparation
   via `wa_id` au 1er échange.
3. **Suivi GPS temps réel** : ✅ FAIT (03/09, commit 4fa00bd) — `GET /api/client/orders/{id}/rider-location` + carte Google Maps live dans la page `/app/suivi/:id`.
4. **Sécuriser les endpoints livreurs** : ✅ DÉJÀ FAIT — `RidersController` exige JWT (Rider gère son compte, Admin tout) avec contrôle d'appartenance.
5. **Dashboard KPI marketing** : ✅ FAIT (03/09, commit 88db0ab) — `GET /api/dashboard/summary` étendu : vendeurs totaux/nouveaux(30j)/actifs(30j), livreurs, commandes semaine/30j, commandes par zone.
6. **Tests réels S3/S4** : ⏳ nécessite téléphone utilisateur (S3 parcours acheteur lien complet, S4 cas négatifs) — protocole prêt.


## D. Scaling technique
- **CI/CD → prod automatisé** : ✅ **OPÉRATIONNEL (06/09)** — workflow `.github/workflows/deploy.yml` + `scripts/cd-deploy.sh`
  (publish self-contained win-x64 → **migrations prod auto** → upload FTP différentiel → health check). Secrets
  `SMARTERASP_*` + `SMARTERASP_DB_CONNECTION` configurés. **Déclenchement automatique sur push `main`** (src/**)
  ou manuel (`workflow_dispatch`, options `full_deploy`/`apply_migrations`).
- **Upload DIFFÉRENTIEL** : ✅ **FAIT (06/09)** — manifest SHA-256 `.deploy-manifest.sha256` conservé sur le serveur ;
  ne transfère que les fichiers nouveaux/modifiés + nettoie les périmés ; `app_offline` seulement si des binaires
  changent. Option `--full` (ou input `full_deploy`) pour forcer un déploiement complet. Seed effectué (395 fichiers),
  déploiement courant : ~1-2 min.
- **Hébergement** : passer de SmarterASP à un **PaaS managé** (Azure App Service / Render / Railway) + PostgreSQL
  managé. ✅ **Préparation faite (06/09)** : `Dockerfile` multi-stage, `.dockerignore`, `docker-compose.dev.yml`,
  guide `docs/PAAS_DEPLOYMENT.md` (env vars, migrations, health). Reste : décision de migration effective
  (maturer d'abord sur SmarterASP pour valider le marché).
- **Docs partenaires & outils** : ✅ `docs/INTEGRATIONS.md` (API v1 : endpoints, exemples curl, clé ;
  webhooks : abonnement, payload, en-têtes, **vérification HMAC C#**, gestion d'échec) + outil
  `tools/GenerateApiKey` (génère `PublicApi__Keys__0`).
- **Monitoring / observabilité** : ✅ **FAIT (06/09)** — `GET /health/details` (JSON : base, outbox,
  workers, uptime) + `GET /metrics` (Prometheus texte 0.0.4, sans dépendance) + logs JSON en prod.
  **Alertes** : `MonitoringAlertService` (log `ALERTE [type]` + webhook optionnel `Monitoring:WebhookUrl`,
  anti-rebond) — déclencheurs : échec définitif outbox (`outbox.failed`) et **worker bloqué/mort**
  (`worker.stale`, watchdog `WorkerLagPolicy` 1×/min).
  `/health` minimal conservé (compatibilité scripts de déploiement). Option : collecteur externe
  (Sentry/App Insights) sur les logs JSON / `/metrics`.
- **Multi-instances** : déjà compatible outbox `SKIP LOCKED` + workers → prêt à horizontaliser ; ajouter un bus de messages si le volume explose.
- **Performance données** : ✅ **index composites ajoutés (06/09, migration 11 `AddRetentionAndIndexes`)** —
  Users(Role/Role+IsAvailable), Orders(Vendor/Rider+Status, Status+DeliveredAt), DeliveryOffers(RiderUserId+Status),
  DeliveryBatches(Status+CreatedAt), RefreshTokens(ExpiresAtUtc). **Rétention** : `RetentionWorker` opt-in
  (`Retention:Enabled=false`) — commandes livrées/lots vides/outbox envoyée (90/90/30 j). Reste : partitionnement
  si >1M lignes, activation rétention après validation.
- **API publique & webhooks** : ✅ **v1 lecture seule FAIT (06/09)** — `GET /api/v1/{overview,zones,vendors,orders,packs}`
  protégée par **clé API** (`X-Api-Key`, middleware dédié) + rate limit `publicapi` (240/min, configurable).
  Sans données personnelles. ✅ **Webhooks sortants FAIT (06/09)** : abonnés (`WebhookSubscribers`, CRUD admin
  `/api/admin/webhooks`) + événements `order.created` / `order.status_changed` émis automatiquement à chaque
  `SaveChanges` (aucun point d'appel à maintenir) → livraison HTTP fiable via l'**outbox** (retries, signature
  HMAC `X-Wazap-Signature`, rejets 4xx permanents marqués Failed + alerte). Migration 12 `AddWebhookSubscribers`.
  Reste (option) : versioning des endpoints d'écriture, événements complémentaires.

## E. Scaling business (30/60/90 — objectifs 10× révisés, cf. MARKETING_STRATEGY §10)
| Phase | Vendeurs actifs | Livreurs | Commandes/sem | Zones |
|---|---|---|---|---|
| J0–30 Démarrage | **150** | **200** | **2 000** | Cocody, Marcory, Yopougon, Adjamé, Treichville |
| J30–60 Accélération | **600** | **700** | **10 000** | + Koumassi, Port-Bouët, Abobo |
| J60–90 Scale | **1 500** | **1 500** | **25 000** | + Bingerville, Anyama, Songon |

- **Entonnoir** : démo→inscription 35 % · inscrit→actif 60 % · 12-15 cmd/sem/vendeur actif.
- **Moyens** : 5 commerciaux J0 → 25 à J60 · budget ads géolocalisées · recrutement livreurs ANTICIPÉ par zone.
- **Conditions** : templates Meta approuvés · capacité livreurs (offre/demande) · délai < 45 min, taux réussite > 90 % · Mobile Money client · KPI quotidiens par zone.


## F. Vision moyen terme (différenciation)
1. **Paiement Mobile Money du client** (Orange Money / MTN MoMo) via GeniusPay → encaisser la course, pas seulement les packs.
2. **Optimisation de tournées** : regroupement intelligent des commandes par trajet (au-delà du simple même-vendeur).
3. **Réputation** : notes clients sur les livreurs + historique → qualité de matching.
4. **Multi-villes** : Bouaké, Yamoussoukro, San-Pédro (config zones/communes par ville).
5. **App mobile livreur** (PWA installable) : notifications push, GPS en arrière-plan, statut vocal.
6. **IA prédictive** : prévision de demande par zone/horaire → prix, stock livreurs, temps de livraison estimé.
