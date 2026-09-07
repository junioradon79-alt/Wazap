# 🗺️ WAZAP — Feuille de route & scaling — MISE À JOUR 06/09/2026 (session acquisition + confiance)

> Récapitulatif de la session du 06/09 (le document d'origine, daté 03/09, est conservé en dessous
> comme historique). Sources : WAZAP_SESSION_NOTES.md (§77 et suiv.), README, DEPLOYMENT.

## ✅ Livré cette session (tout poussé sur main + déploiement auto, tests OK)
1. `774b28a` — **Automatisation prospects WhatsApp** (numéros inconnus) : leads + qualification en 2 messages + alerte équipe.
2. `17a15af` — **Conversion Lead → compte vendeur** : `POST /api/admin/leads/{id}/convert` (+ bouton `/app/leads`, trial, mot de passe temporaire, bienvenue WhatsApp).
3. `0f22b6a` — **« CONVERTIR » depuis l'alerte WhatsApp équipe** + **espace vendeur** (`/app`, stats, parrainage, historique).
4. `41ee803` — **Garantie Colis Sûr v1 — Certification livreurs** : `RiderIdentity` (statuts), vérif admin, blacklist (plus d'offres), option `RiderSecurity:RequireCertifiedRiders`.
5. `d2797be` — **Certification v2 — scan de la pièce d'identité** (upload admin, moto décrite sans plaque obligatoire).
6. `3485922` — **Notifications WhatsApp aux décisions de certification** + **onboarding vendeur J+1/J+3/J+7** (worker opt-in).
7. `a173243` — **Parrainage au converti WhatsApp** (+5 crédits au parrain, capture code `WA-XXXX`) + **profil livreur visible au vendeur** (« certifié · N livraisons »).
8. `6b8bf0b` — **Garantie Colis Sûr étape 2 — SINISTRES** : commande `SINISTRE <code>`, suspension pendant enquête, page admin `/app/claims`, indemnisation (crédits) + exclusion.

## ✅ Livré le 07/09 (session « preuve de remise »)
9. **Suivi des filleuls** (chantier 1) : octroi +5 au parrain tracé en `CreditTransaction`
   (réf. `REF-<code>-<filleul>`, unique par couple → anti-abus) à l'inscription comme à la
   conversion d'un lead ; `GET /api/vendors/dashboard` expose filleuls + crédits gagnés, affichés dans `/app`.
10. **Dashboard leads enrichi** (chantier 7) : filtres source / code parrainage / recherche libre
    (commerce, contact, numéro) sur la liste et l'export, colonne `code_parrainage` au CSV.
11. **RGPD — scans CNI chiffrés au repos** (chantier 9, partiel) : AES-GCM (en-tête `WZSCN1`),
    clé `RiderScans:EncryptionKey` (hex 64 ou base64). Sans clé → comportement historique ;
    les scans déjà en clair restent lisibles. Lecture admin déchiffrée à la volée.
12. **Preuves de livraison** (chantier 3, volet code client) : code à 4 chiffres généré à
    l'assignation du livreur, envoyé au client dans un message dédié (template
    `TemplateDeliveryCode`, texte tant que Meta n'a pas approuvé), restitué par le livreur via
    **`LIVRE <code> CODE <4 chiffres>`**. Vérification à temps constant, **5 tentatives** puis
    blocage (recours : clôture par le vendeur/admin). Option `DeliveryProof:RequireClientCode`
    (défaut **false** : le code est envoyé et vérifié s'il est fourni, sans rompre le flux en place ;
    à **true**, `LIVRE` sans code et `LIVRE TOUT` sont refusés, et un livreur ne peut plus non plus
    clôturer via l'API). Migration 19 `AddDeliveryProof`.

## ⏭️ Chantiers restants à couvrir
### Code (par impact)
1. **Garantie Colis Sûr étape 3** : versement FCFA sortant (Orange Money via GeniusPay), plafond/franchise configurables, caution livreur, conditions affichées sur `/app/vente`.
2. **Preuves de livraison — volet photo** : photo du colis au retrait (dépend du webhook média ci-dessous).
3. **Notes / réputation livreur** (étoiles) affichées au vendeur avant remise du colis.
4. **Webhook média WhatChimp** (photos CNI en auto) — sinon rester sur l'upload admin.
5. **Onboarding vendeur activable** dès templates Meta approuvés + relance des inactifs.
6. **Tests** : ✅ services couverts (LeadConversion, ColisSur, RiderService, AuthService, preuve de livraison) — reste l'**E2E webhook**.
7. **Sécurité/RGPD CNI** : ✅ chiffrement au repos — reste **durées de conservation** (les scans ne sont pas purgés par `RetentionWorker`) et **consentement**.
8. Suite ROADMAP historique : Mobile Money client, multi-villes, PWA livreur, réputation, IA prévision.

### Actions utilisateur (déblocages)
- **Meta** : statut des 15 templates → dès `Approved`, renseigner les noms dans le web.config distant.
- **Créer/valider les 3 templates onboarding vendeur** (d1/d3/d7) puis `VendorOnboarding:Enabled=true`.
- **Certifier les livreurs actuels** (scan CNI) puis `RiderSecurity:RequireCertifiedRiders=true`.
- Tests réels : prospect inconnu → `CONVERTIR` → `LIVRAISON` → `SINISTRE` (bout-en-bout).
- Collecte Overpass complète + campagne 72 mobiles + purge comptes de test prod.

---

# 🗺️ WAZAP — Feuille de route & scaling (03/09/2026)

> Document de synthèse : reste à faire + propositions de scaling.
> Sources : WAZAP_SESSION_NOTES.md, README.md, DEPLOYMENT.md, prospection/*, MARKETING_STRATEGY.md.

## État actuel (résumé)
- **Produit** : livraison à la demande WhatsApp (vendeur→livreur→client), tournées groupées, parcours acheteur PWA avec suivi, packs de crédits prépayés (GeniusPay LIVE), parrainage, trial 15 commandes, auth renforcée (refresh/2FA/reset).
- **Acquisition (leads)** : page de vente publique `/app/vente` (offre réelle « 15 commandes offertes », CTA WhatsApp, zones, formulaire) + capture `Lead` (migration 13), gestion admin + export CSV.
- **Automatisation prospects WhatsApp** : tout numéro **inconnu** écrivant au 225 05 75 80 38 01 est traité par le bot (webhook) — détection commerçant/livreur/parrainage, réponse contextuelle de qualification, **Lead créé/qualifié automatiquement** dans `/app/leads` (+ alerte optionnelle `Prospect:TeamPhone`).
- **Tests** : 117/117 · **Prod** : Healthy (SmarterASP self-contained) · **Base dev** : PostgreSQL local `wazapdev` (séparée) · **CI** GitHub Actions ✅.
- **Tests réels validés** : flux livreur complet (ACCEPTE→RECU→LIVRE), groupage multi-clients (diffusion différée 30 s).

---

## A. Actions utilisateur (dashboards externes) — me prévenir pour activer
1. **Templates Meta — 14 rejetés sur 15** (constat 07/09, remplace « tous Submitted »). Cause identifiée :
   **exemples de contenu variable manquants** (le seul approuvé, `no_credit`, est le seul sans variable).
   Corriger dans WhatsApp Manager puis resoumettre ; dès `Approved`, activer dans appsettings + déployer :
   - `order_received`, `order_confirm`, `rider_offer`, `rider_batch_offer` (+ `_btn` bouton), `rider_assigned_client`, `rider_assigned_vendor`
   - crédits : `credit_purchase`, `low_credit`, `no_credit` ; prospection : `prospect_approach/followup/offer` ; recrutement : `rider_recruit`, `rider_company`
2. **Clé API Google Places** (`AIza…`, carte bancaire requise) → collecte complète 13 zones × 33 secteurs.
3. **Vidéo démo 30 s** hébergée (URL publique) → variable {{3}} des templates prospect + campagne.
4. **Nom de domaine** propre (remplacer le jtempurl.com).

## B. À relancer (service externe / timing)
1. **Collecte Overpass complète** (33 secteurs, 13 communes) — Overpass public saturé depuis le 03/09.
   Outil **durci (06/09)** : sonde de disponibilité des miroirs (ordre dynamique, sortie rapide si tout
   est down), option `--timeout=<s>` (défaut 120), réponses « busy/timeout » d'Overpass traitées comme
   de vrais échecs réessayables. **Outils versionnés dans le repo** (`WazapSln/tools/*`). Relancer quand
   la charge baisse (depuis `WazapSln`) :
   `dotnet run --project tools\ProspectCollectorOsm -- --out-dir=c:\Dev\Wazap\prospection\out_osm`
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
