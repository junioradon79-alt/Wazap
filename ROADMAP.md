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
2. **Conversion numéros 8→10 chiffres** : ⏳ table reconstituée du **plan ARTCI 2021** (commit 6db9cbb, 06/09) :
   Orange→07, MTN→05, Moov/ex-Atlantique→01, lignes fixes 2x/3x exclues (pas de WhatsApp), échantillons réels
   en tests. **Toujours désactivée** (`IvoryCoastNumbering.Enabled=false`) — reste à valider sur le document
   officiel ARTCI puis test réel avant activation (aucun code à écrire). Approche actuelle conservée en
   attendant : matching SameSubscriber (8 derniers) + auto-réparation via `wa_id` au 1er échange.
3. **Suivi GPS temps réel** : ✅ FAIT (03/09, commit 4fa00bd) — `GET /api/client/orders/{id}/rider-location` + carte Google Maps live dans la page `/app/suivi/:id`.
4. **Sécuriser les endpoints livreurs** : ✅ DÉJÀ FAIT — `RidersController` exige JWT (Rider gère son compte, Admin tout) avec contrôle d'appartenance.
5. **Dashboard KPI marketing** : ✅ FAIT (03/09, commit 88db0ab) — `GET /api/dashboard/summary` étendu : vendeurs totaux/nouveaux(30j)/actifs(30j), livreurs, commandes semaine/30j, commandes par zone.
6. **Tests réels S3/S4** : ⏳ nécessite téléphone utilisateur (S3 parcours acheteur lien complet, S4 cas négatifs) — protocole prêt.


## D. Scaling technique
- **CI/CD → prod automatisé** : ✅ **OPÉRATIONNEL (06/09, run #3 success)** — workflow `.github/workflows/deploy.yml`
  + `scripts/cd-deploy.sh` (publish self-contained win-x64 → upload FTP avec `app_offline`, `web.config` distant
  préservé → health check). Secrets `SMARTERASP_*` configurés. Déclenchement manuel (workflow_dispatch).
- **Upload DIFFÉRENTIEL** : ✅ **FAIT (06/09)** — manifest SHA-256 `.deploy-manifest.sha256` conservé sur le serveur ;
  ne transfère que les fichiers nouveaux/modifiés + nettoie les périmés ; `app_offline` seulement si des binaires
  changent. Option `--full` (ou input `full_deploy`) pour forcer un déploiement complet. Seed effectué (395 fichiers),
  déploiement courant : ~1-2 min.
- **Hébergement** : passer de SmarterASP (self-contained, upload FTP lent) à **PaaS managé** (Azure App Service / Render / Railway) + PostgreSQL managé (scalable, backups auto). Maturer d'abord sur SmarterASP pour valider le marché.
- **Monitoring / alerting** : logs structurés + métriques (OpenTelemetry → Application Insights/Sentry) ; alertes sur échecs webhook et file d'attente.
- **Multi-instances** : déjà compatible outbox `SKIP LOCKED` + workers → prêt à horizontaliser ; ajouter un bus de messages si le volume explose.
- **Performance données** : index supplémentaires, archivage des commandes livrées (rétention), partitionnement si >1M lignes.
- **API publique** versionnée pour intégrations (agrégateurs, grossistes) + webhooks sortants.

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
