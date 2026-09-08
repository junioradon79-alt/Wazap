# Scripts d'activation WAZAP

Scripts PowerShell pour activer les fonctionnalités en production (SmarterASP).

## Prérequis

- PowerShell 5.1+
- Accès FTP SmarterASP (voir `DEPLOYMENT.md`)
- Token Admin JWT (pour les scripts de certification)

## Utilisation

### 1. Activer le paiement client Mobile Money

```powershell
.\01-activate-client-payments.ps1
```

Modifie le `web.config` distant pour ajouter :
- `ClientPayments__Enabled=true`
- `ClientPayments__CommissionPercent=2.0`
- `ClientPayments__RequirePaymentBeforeDispatch=false`

### 2. Certifier les livreurs

```powershell
# Lister les certifications
.\02-certify-riders.ps1 -AdminToken "eyJ..." -ListOnly

# Certifier un livreur spécifique
.\02-certify-riders.ps1 -AdminToken "eyJ..." -RiderId "..." -FullName "Ibrahim" -IdNumber "CI123"

# Certifier tous les pending
.\02-certify-riders.ps1 -AdminToken "eyJ..." -AutoVerifyAll

# Mode interactif
.\02-certify-riders.ps1 -AdminToken "eyJ..."
```

### 3. Lancer les tests E2E

```powershell
.\03-run-e2e-tests.ps1 -VendorPhone "+2250708091011" -RiderPhone "+2250708091012"
```

Crée les comptes de test et affiche les instructions pour les scénarios S1-S4.

### 4. Activer la sécurité certification

```powershell
.\04-activate-rider-security.ps1
```

Ajoute `RiderSecurity__RequireCertifiedRiders=true` dans le `web.config` distant.

### 5. Nettoyer les données de test

```powershell
.\05-cleanup-test-data.ps1
```

Lance `PurgeTestData` et `CleanupTestVendors`.

### 6. Vérifier le déploiement

```powershell
.\06-verify-deployment.ps1
```

Vérifie les endpoints : `/health`, `/health/details`, `/metrics`, `/api/v1/overview`.

## Options communes

| Paramètre | Description | Défaut |
|---|---|
| `-FtpHost` | Hôte FTP SmarterASP | `ftp://WIN6054.site4now.net` |
| `-FtpUser` | Utilisateur FTP | `junioradon79gm-001` |
| `-FtpPass` | Mot de passe FTP | `$env:WAZAP_FTP_PASS` ou prompt (voir DEPLOYMENT.md) |
| `-BaseUrl` | URL de production | `https://junioradon79gm-001-site1.jtempurl.com` |
| `-DryRun` | Simulation sans modification | `false` |

### 7. Préparer une campagne prospects

```powershell
.\07-prepare-campaign.ps1 -CsvPath "prospection\Prospects_campagne_mobiles_20260902.csv" -Zone "Marcory"
```

Vérifie que le template `prospect_approach` est approuvé, puis lance la campagne via `tools/WhatsAppCampaign`.

## Ordre d'exécution recommandé

1. `06-verify-deployment.ps1` — Vérifier l'état initial
2. `01-activate-client-payments.ps1` — Activer le paiement
3. `02-certify-riders.ps1 -ListOnly` — Voir les livreurs à certifier
4. `02-certify-riders.ps1 -AutoVerifyAll` — Certifier les livreurs
5. `04-activate-rider-security.ps1` — Activer la sécurité
6. `03-run-e2e-tests.ps1` — Lancer les tests réels
7. `07-prepare-campaign.ps1` — Lancer la campagne prospects
8. `05-cleanup-test-data.ps1` — Nettoyer après tests
