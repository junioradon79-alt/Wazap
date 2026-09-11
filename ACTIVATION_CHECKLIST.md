# 🚀 WAZAP — Checklist d'activation en production

> **Date** : 11/09/2026 · **Build** : 0 erreur / 0 warning · **Tests** : 422/422 ✓
> **Code livré** : toutes les fonctionnalités ci-dessous sont **développées, testées et déployées**.
> **Reste à faire** : uniquement des **actions utilisateur** (dashboards externes + config web.config distant).

---

## 📋 Récapitulatif des priorités

| # | Chantier | Type | Impact | Statut |
|---|---|---|---|---|
| 1 | Templates Meta | Meta | Acquisition (Marketing) | ✅ **TOUS approuvés & activés (09/09)** : 9 Utility + 5 Marketing `*_v2` + rider_offer_v2 + onboarding `vendor_onboarding_day1/3/7` (worker ON) | 🔴 `delivery_code` bloqué création Meta (test discriminant ci-dessous) · 🟠 **vidéo démo : infra prête** (page `demo-video.html` + script `08`) |
| 2 | Paiement client Mobile Money | Config prod | Encaissement en ligne | **ACTIVÉ** (08/09) ✓ |
| 3 | Rétention/purge scans CNI | Config prod | RGPD | **DÉJÀ ACTIVÉ** ✓ |
| 4 | Certification livreurs | Mixte | Confiance Colis Sûr | Code livré, à certifier |
| 5 | Tests réels E2E | Validation | Tout le produit | Protocole prêt |
| 6 | Catalogue produits + bot de commande client | Config prod | Conversion (commande directe) | **Code livré + testé** · migration 27 appliquée par la CI au push |

---

## 1. 📣 Templates Marketing (5) — ✅ TOUS APPROUVÉS (09/09), noms `*_v2`

> Les 5 templates Marketing ont été **resoumis et approuvés par Meta** sous de nouveaux noms `*_v2`
> (avec `rider_offer_v2` pour l'offre de course). Ils sont **branchés dans les défauts
> `WhatsAppOptions` + `appsettings.json`** → déployé par CI (aucun web.config distant requis).

| Template approuvé | Variables envoyées par le code |
|---|---|
| `prospect_approach_v2` | 3 (nom, commercial, lien) |
| `prospect_followup_v2` | 2 (nom, commercial) |
| `prospect_offer_v2` | 1 (nom) |
| `rider_recruit_v2` | 2 (prénom, lien) |
| `rider_company_v2` | 2 (entreprise, lien) |
| `rider_offer_v2` | 1 (code d'offre) — remplace `rider_offer` |

**Config déjà en place** (défauts `WhatsAppOptions` + `appsettings.json`). Aucune action requise.


---

## 2. 💳 Paiement client Mobile Money — CONFIG PROD

### Code livré (commit `ec68e53`, session 08/09)

- **Entité** : `OrderPayment` (montant, commission, `VendorPayoutDue`, lien, statut)
- **Service** : `ClientPaymentService` — initiation idempotente, complétion, double-encaissement détecté
- **Webhook** : `GeniusPayWebhookController` → routage automatique packs OU commandes
- **Réconciliation** : `PaymentReconciliationWorker` étendu aux paiements commande
- **Front** : `SuiviPage.tsx` — carte « Payer par Mobile Money »
- **API** : `POST /api/client/orders/{id}/pay` (rate-limité, idempotente)
- **DB** : migration 24 `AddClientPayments` (table `OrderPayments`)

### Activation — Config web.config distant

Ajouter dans `/wazap2/web.config` :

```xml
<!-- Paiement client Mobile Money (ACTIVATION) -->
<environmentVariable name="ClientPayments__Enabled" value="true" />
<environmentVariable name="ClientPayments__CommissionPercent" value="2.0" />
<environmentVariable name="ClientPayments__RequirePaymentBeforeDispatch" value="false" />
```

### Comportement après activation

| Option | `false` (défaut) | `true` |
|---|---|---|
| `Enabled` | Pas de paiement en ligne | Lien GeniusPay disponible |
| `RequirePaymentBeforeDispatch` | Cash accepté, diffusion normale | Diffusion bloquée tant que impayé |

> 💡 **Stratégie recommandée** : activer d'abord `Enabled=true` seul (zone pilote),
> puis activer `RequirePaymentBeforeDispatch=true` une fois le flux éprouvé.

---

## 3. 🧹 Rétention/purge scans CNI — DÉJÀ ACTIVÉ ✓

### État actuel

- `Retention__Enabled = true` → **configuré et actif** (DEPLOYMENT.md §UPDATE 2026-09-07)
- `RiderScans__EncryptionKey` → **configuré** (chiffrement AES-GCM opérationnel)
- `RetentionWorker` tourne toutes les 24 h :
  - Commandes livrées : purge après 90 j
  - Lots vides : purge après 90 j
  - Outbox envoyé : purge après 30 j
  - Scans CNI : purge après 90 j suivant la décision de certification

### Vérification

```bash
GET https://junioradon79gm-001-site1.jtempurl.com/health/details
```

→ Vérifier `retention.enabled = true` et `compliance.status = "ok"`.

> ✅ **Aucune action requise** — ce chantier est terminé.

---

## 4. 🛡️ Certification livreurs + sécurité — MIXTE

### Code livré

- **Entité** : `RiderIdentity` (FullName, IdNumber, Motorcycle, IdScanUrl, statuts)
- **Scan** : upload admin OU auto via WhatsApp (webhook média, stockage chiffré)
- **🤖 Bot de recrutement (08/09, commit `d73a2e5`)** : un candidat écrit « je veux livrer » sur
  WhatsApp → le bot collecte nom + quartier + photo CNI → **compte livreur créé automatiquement**
  (identifiants envoyés au candidat) avec scan chiffré + consentement tracé → l'équipe est
  alertée et certifie en 1 clic. **Aucune action requise** (actif dès déploiement).
- **Vérification** : admin valide/rejette → notifications WhatsApp
- **Blacklist** : livreur exclu ne reçoit plus d'offres
- **Option** : `RiderSecurity:RequireCertifiedRiders` (défaut **false**)

### Action utilisateur — Certifier les livreurs

1. **Interface admin** : `/app/riders` → liste des livreurs (les candidatures du bot arrivent
   ici avec la mention « whatsapp » comme méthode de consentement)
2. Pour chaque livreur :
   - Vérifier le dossier (pièce d'identité + scan)
   - Cliquer **« Vérifier »** → statut `Verified`
   - Ou **« Exclure »** en cas de problème
3. **Alternative** : le livreur envoie sa pièce via WhatsApp (photo CNI) → stockage auto → admin vérifie

### Activation — Config web.config distant

Une fois le pool de livreurs certifié suffisant :

```xml
<environmentVariable name="RiderSecurity__RequireCertifiedRiders" value="true" />
```

> ⚠️ **Attention** : à `true`, seuls les livreurs certifiés reçoivent des offres.
> Si aucun livreur n'est certifié dans une zone → aucune offre possible.
> Activer progressivement (zone pilote d'abord).

### Options complémentaires (à activer quand décidé)

```xml
<!-- Durcissement preuve de livraison (code client obligatoire) -->
<environmentVariable name="DeliveryProof__RequireClientCode" value="true" />

<!-- Pondération matching par réputation (défaut false) -->
<environmentVariable name="RiderReputation__PreferHigherRatedRiders" value="true" />

<!-- Score minimum pour recevoir des offres (défaut 0) -->
<environmentVariable name="RiderReputation__MinimumAverageScore" value="3.0" />
```

---

## 5. 🧪 Tests réels E2E — VALIDATION

### Protocole complet

Le protocole pas-à-pas est dans : `prospection/PROTOCOLE_TEST_REEL.md`

### Scénarios à tester (par ordre)

| Scénario | Description | Dépendance |
|---|---|---|
| **S1** | Livraison à la demande (flux vendeur principal) | Webhook WhatChimp actif |
| **S2** | Tournée multi-clients (1 livreur, plusieurs clients) | S1 OK |
| **S3** | Parcours acheteur PWA (lien → coordonnées → auto-dispatch) | S1 OK |
| **S4** | Cas négatifs (sans zone, 0 crédit, mauvais code) | S1 OK |

### Prérequis

- [ ] **Webhook WhatChimp actif** : `https://junioradon79gm-001-site1.jtempurl.com/api/webhook/whatsapp`
- [ ] **Fenêtre 24 h** : chaque numéro test doit d'abord écrire au numéro WAZAP
- [ ] **Comptes de test** : créer via `POST /api/auth/register` (rôle Vendor/Rider, numéros réels)
- [ ] **Templates Meta** : en attente d'approbation (envois texte seulement hors fenêtre 24 h)

### Nettoyage après test

```bash
# Purger les données de test (commandes, offres, lots, transactions)
dotnet run --project tools/PurgeTestData -- --confirm

# Supprimer les comptes de test (test_%)
dotnet run --project tools/CleanupTestVendors
```

### Grille de résultats

| # | Vérification | Attendu | OK ? |
|---|---|---|---|
| S1 | `LIVRAISON` → crédits inchangés | ✅ 15→15 | ☐ |
| S1 | `ACCEPTE` → crédits 15→14 + liens Maps | ✅ | ☐ |
| S1 | `RECU` / `LIVRE` → statuts + notif client | ✅ | ☐ |
| S2 | Lot groupé → liste multi-clients | ✅ | ☐ |
| S2 | `LIVRE` sans code refusé (multi) | ✅ | ☐ |
| S3 | Lien reçu après confirmation vendeur | ✅ | ☐ |
| S3 | Coordonnées → diffusion auto + notif vendeur | ✅ | ☐ |
| S3 | Page suivi → « Livré ✓ » | ✅ | ☐ |
| S4 | Cas négatifs → messages clairs | ✅ | ☐ |

---

## 6. 🛒 Catalogue produits + bot de commande client — CODE LIVRÉ

### Ce que ça apporte

Un **client final** (numéro inconnu) qui écrit « COMMANDE » sur WhatsApp est guidé par un bot
conversationnel : **article → commerce → (menu du catalogue) → adresse** → une **vraie commande**
est créée au compte du vendeur, qui la confirme comme d'habitude (la diffusion aux livreurs suit
la confirmation).

### Code livré

- **Entités** : `VendorProduct` (catalogue du vendeur), `OrderLine` (ligne de commande, copie
  nom/emoji/prix pour l'historique), `ClientOrderDraft` (+ `ClientOrderDraftStage`) — le brouillon
  de conversation (un seul actif par numéro, expire après `ClientOrderBot:ExpirationHours`).
- **Bot** : `ClientOrderBotService` — routage **livreur → commande client → prospects** ; panier
  par numéro (« 1 » ou « 1 2 ») quand le commerce a un catalogue, sinon mode **texte libre** ;
  `ANNULER` ; anti-boucle (3 réponses inattendues → abandon).
- **Catalogue** : `VendorProductService` + endpoints REST `GET/POST/PUT/DELETE
  /api/vendors/{id}/products[/{productId}]` (rôle Admin,Vendor, restreint au propriétaire).
- **Commandes WhatsApp vendeur** : `PRODUITS` (liste), `PRODUIT <nom> | <prix> [| <emoji>]`
  (ajout), `SUPPRIMER PRODUIT <n°>` (retrait) — ajoutées au menu `AIDE`.
- **Front** : page `/app/catalogue` (vendeur : son catalogue ; admin : sélection du vendeur).
- **Commandes mode catalogue** : `POST /api/orders` accepte des `lines` (montant recalculé).
- **DB** : migration 27 `AddVendorCatalogAndClientOrderDrafts` (`VendorProducts`, `OrderLines`,
  `ClientOrderDrafts`). **Appliquée automatiquement par la CI** au push sur `main`
  (`deploy.yml` → job `apply_migrations` → `dotnet ef database update --connection $PROD_DB`,
  secret `SMARTERASP_DB_CONNECTION`). Chaîne complète validée le 11/09 sur PostgreSQL 17.
- **Tests** : 31 nouveaux (bot, catalogue, service, bout-en-bout webhook) + 5 tests de régression
  S5 — **427/427 ✓**.
- ⚠️ **Enregistrement DI obligatoire** : `ClientOrderBotService` doit être déclaré dans
  `Program.cs` (`AddScoped`) — un oubli fait échouer la construction de `WebhookWhatsAppController`
  et **tout** le webhook WhatsApp répond `409` (`Unable to resolve service`). Défaut détecté en
  prod et corrigé le 11/09 (scénario S5, commit `8aa12a5`).
- ✅ **Angle mort fermé (11/09)** : `DiControllerResolutionTests` démarre le **vrai `Program.cs`**
  (`WebApplicationFactory<Program>`, base InMemory, workers retirés) et construit **chaque
  contrôleur** avec l'`IControllerActivator` de MVC — la mécanique exacte d'une requête HTTP. Un
  service injecté mais non enregistré fait échouer la CI, en nommant le contrôleur fautif
  (vérifié en retirant `AddScoped<ClientOrderBotService>()` → échec ciblé). **428/428 ✓**.

### Activation (aucune action externe)

Aucune dépendance à un dashboard externe. La config par défaut suffit :

```xml
<!-- Optionnel : désactiver le bot ou changer la fenêtre de conversation -->
<environmentVariable name="ClientOrderBot__Enabled" value="true" />
<environmentVariable name="ClientOrderBot__ExpirationHours" value="24" />
```

⚠️ **Protection de l'historique** : un produit déjà présent dans une commande ne peut plus être
supprimé (`409`), il faut le modifier — la ligne de commande garde une copie du nom et du prix.

---

## 📌 Prochaines étapes après activation

### P2 — Acquisition (dès templates approuvés)
- Campagne 72 mobiles via `tools/WhatsAppCampaign`
- Vidéo démo 30 s + nom de domaine propre
- Purge comptes de test prod
- Versement Colis Sûr (GeniusPay disbursement : action utilisateur)

### P3 — Technique & dette
- Consentement livreur tracé (RGPD)
- Webhooks sortants complémentaires
- Versioning API v1 endpoints d'écriture

### P4 — Vision moyen terme
- Optimisation de tournées
- Multi-villes (Bouaké, Yamoussoukro, San-Pédro)
- PWA livreur (push, GPS arrière-plan)
- IA prévision de demande

---

> **Mise à jour** : ce document est synchronisé avec `AUDIT_20260907.md` §3,
> `ROADMAP.md` (màj 08/09) et `WAZAP_SESSION_NOTES.md` §85-89.


### Configuration GeniusPay (déjà en place)

- Clés LIVE : configurées (voir `DEPLOYMENT.md`)
- URL webhook : `https://junioradon79gm-001-site1.jtempurl.com/api/webhook/geniuspay`
> ✅ **Templates Meta (09/09)** : 9 Utility + 5 Marketing `*_v2` + `rider_offer_v2` **approuvés**
> et branchés (défauts `WhatsAppOptions` + `appsettings.json`, déployé CI) · onboarding
> `vendor_onboarding_day1/3/7` **ACTIVÉ** (worker ON).
> 🔴 **`delivery_code` BLOQUÉ création Meta (09/09)** : « Ce compte WhatsApp Business n'a pas
> l'autorisation de créer un modèle de message » — numéro sain (Connecté, qualité ÉLEVÉE) →
> causes : permissions / limite quotidienne / restriction auth — **test discriminant à la reprise**.
> Corps prêt : `prospection/TEMPLATE_DELIVERY_CODE.md` (auth, « Copier le code », 1 variable).
