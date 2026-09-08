# 🚀 WAZAP — Checklist d'activation en production

> **Date** : 08/09/2026 · **Build** : 0 erreur / 0 warning · **Tests** : 362/362 ✓
> **Code livré** : toutes les fonctionnalités ci-dessous sont **développées, testées et déployées**.
> **Reste à faire** : uniquement des **actions utilisateur** (dashboards externes + config web.config distant).

---

## 📋 Récapitulatif des priorités

| # | Chantier | Type | Impact | Statut |
|---|---|---|---|---|
| 1 | Templates Meta : 9 Utility activés ✓ · 5 Marketing · 3 onboarding soumis | Utilisateur | Acquisition (Marketing) | **9 Utility actifs ✓ (08/09)** — restent 5 Marketing + approbation onboarding + delivery_code |
| 2 | Paiement client Mobile Money | Config prod | Encaissement en ligne | **ACTIVÉ** (08/09) ✓ |
| 3 | Rétention/purge scans CNI | Config prod | RGPD | **DÉJÀ ACTIVÉ** ✓ |
| 4 | Certification livreurs | Mixte | Confiance Colis Sûr | Code livré, à certifier |
| 5 | Tests réels E2E | Validation | Tout le produit | Protocole prêt |

---

## 1. 📣 Templates Marketing (5 restants) — ACTION UTILISATEUR

### Templates à corriger dans WhatsApp Manager

Les corps corrigés + exemples de variables sont dans :
`prospection/TEMPLATES_MARKETING_A_CORRIGER.md`

| Template | Variables | Problème corrigé |
|---|---|---|
| `prospect_approach` | 3 (nom, commercial, lien) | Corps finissait par `{{3}}` → texte ajouté après |
| `prospect_followup` | 2 (nom, commercial) | Exemples de variables manquants |
| `prospect_offer` | 1 (nom) | Numérotation à trous → corrigée |
| `rider_recruit` | 2 (prénom, lien) | Corps finissait par `{{2}}` → texte ajouté après |
| `rider_company` | 2 (entreprise, lien) | Corps finissait par `{{2}}` → texte ajouté après |

### Étapes dans WhatsApp Manager

1. **WhatsApp Manager → Modèles de messages** → ouvrir chaque template rejeté → **Modifier**
2. Coller le corps corrigé (depuis `TEMPLATES_MARKETING_A_CORRIGER.md`)
3. Section **« Exemples de contenu variable »** (*Sample variable content*) :
   - Saisir les valeurs du tableau pour chaque `{{n}}`
   - **Aucun champ ne doit rester vide**
4. **Soumettre** → vérification via API :
   ```
   template/list?apiToken=…&phone_number_id=735886129615120
   ```
   → champ `message[].status` : attendre `Approved`

### Après approbation — Config web.config distant

Ajouter dans `/wazap2/web.config` (section `<environmentVariables>`) :

```xml
<environmentVariable name="WhatChimp__TemplateProspectApproach" value="prospect_approach" />
<environmentVariable name="WhatChimp__TemplateProspectFollowup" value="prospect_followup" />
<environmentVariable name="WhatChimp__TemplateProspectOffer" value="prospect_offer" />
<environmentVariable name="WhatChimp__TemplateRiderRecruit" value="rider_recruit" />
<environmentVariable name="WhatChimp__TemplateRiderCompany" value="rider_company" />
```


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
> ✅ **Templates Utility actifs (08/09)** : `order_received`, `order_confirm`, `rider_offer`,
> `rider_batch_offer`, `rider_assigned_client`, `rider_assigned_vendor`, `credit_purchase`,
> `low_credit`, `no_credit` (défauts `WhatsAppOptions` + `appsettings.json`, déployé CI).
> ⚠️ Restent en attente Meta : `delivery_code`, les 5 templates **Marketing** (prospect +
> rider_recruit/company, voir `prospection/TEMPLATES_MARKETING_A_CORRIGER.md`) et les **3 onboarding**
> vendeur (J+1/J+3/J+7 — **soumis par l'utilisateur**, à corriger seulement en cas de rejet).
