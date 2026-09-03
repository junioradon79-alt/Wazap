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
1. **Collecte Overpass complète** (33 secteurs, 13 communes) — Overpass public était down ; code robuste prêt :
   `dotnet run --project tools\ProspectCollectorOsm -- --reset --out-dir=prospection\out_osm`
2. **Campagne WhatsApp prospects** (72 mobiles qualifiés `Prospects_campagne_mobiles_20260902.csv`) — dès approbation de `prospect_approach` :
   `$env:WHATCHIMP_API_TOKEN=… ; dotnet run --project tools\WhatsAppCampaign -- prospection\Prospects_campagne_mobiles_20260902.csv --zone=Marcory`
3. **Purge des comptes de test** en base prod (`test_reel_utilisateur`, `test_vendeur_cocody`) après la fin des essais réels (CleanupTestVendors).

## C. Chantiers code recommandés (par ordre d'impact)
1. **Fallback timeout 5 min** : actuellement un simple log quand aucun livreur n'accepte → notifier le vendeur (« aucun livreur trouvé, réessayez ») + proposer une relance.
2. **Conversion numéros 8→10 chiffres** (table de correspondance par opérateur CI) pour fiabiliser les notifications sortantes.
3. **Suivi GPS temps réel** : exposer la position du livreur au client pendant la course (le parcours acheteur poll le statut, pas la position).
4. **Sécuriser les endpoints livreurs** (location/availability) par JWT quand l'app mobile arrive.
5. **Dashboard KPI marketing** (vendeurs actifs, taux de conversion, commandes/zone/semaine — voir MARKETING_STRATEGY §7).
6. **Fin des tests réels** S3 (parcours acheteur complet lien→GPS) et S4 (cas négatifs) — protocole prêt.
7. Minor : Swagger `AddSecurityRequirement` (bloqué par Microsoft.OpenApi v2), nettoyage vieux assets serveur.

## D. Scaling technique
- **CI/CD → prod automatisé** : workflow GitHub Actions qui publie + upload FTP (actuellement manuel, upload différentiel).
- **Hébergement** : passer de SmarterASP (self-contained, upload FTP lent) à **PaaS managé** (Azure App Service / Render / Railway) + PostgreSQL managé (scalable, backups auto). Maturer d'abord sur SmarterASP pour valider le marché.
- **Monitoring / alerting** : logs structurés + métriques (OpenTelemetry → Application Insights/Sentry) ; alertes sur échecs webhook et file d'attente.
- **Multi-instances** : déjà compatible outbox `SKIP LOCKED` + workers → prêt à horizontaliser ; ajouter un bus de messages si le volume explose.
- **Performance données** : index supplémentaires, archivage des commandes livrées (rétention), partitionnement si >1M lignes.
- **API publique** versionnée pour intégrations (agrégateurs, grossistes) + webhooks sortants.

## E. Scaling business (30/60/90 — cf. MARKETING_STRATEGY)
| Phase | Actions | Cibles KPI |
|---|---|---|
| J0–30 | Zone pilote Cocody/Marcory · démarchage 20 vendeurs · 30 livreurs · pack découverte · collecte+relances prospects | 20 vendeurs · 50 commandes/sem |
| J30–60 | Parrainage amplifié · 2 zones + · témoignages · partenariats commerçants | 60 vendeurs · 300 cmd/sem |
| J60–90 | Campagnes Ads géolocalisées · programme livreur (prime) · analyse zones rentables | 120 vendeurs · 1 000 cmd/sem |
- **Levier produit** : templates approuvés (notifications pro + prospect) = condition pour industrialiser l'acquisition.
- **Recrutement livreurs** : templates `rider_recruit`/`rider_company` + lien d'inscription.

## F. Vision moyen terme (différenciation)
1. **Paiement Mobile Money du client** (Orange Money / MTN MoMo) via GeniusPay → encaisser la course, pas seulement les packs.
2. **Optimisation de tournées** : regroupement intelligent des commandes par trajet (au-delà du simple même-vendeur).
3. **Réputation** : notes clients sur les livreurs + historique → qualité de matching.
4. **Multi-villes** : Bouaké, Yamoussoukro, San-Pédro (config zones/communes par ville).
5. **App mobile livreur** (PWA installable) : notifications push, GPS en arrière-plan, statut vocal.
6. **IA prédictive** : prévision de demande par zone/horaire → prix, stock livreurs, temps de livraison estimé.
