# [SPRINT ACTIF & HANDOFF DE SESSION WAZAP]
<!-- ecc.memory.v1 scope:project type:handoff status:active updated:2026-10-02 -->

## 1. État Actuel du Système
- **Backend .NET :** 880 tests (874 réussis localement + 6 sur PostgreSQL réel en CI) - 0 échec.
- **Frontend Vitest :** 57/57 tests réussis (100%).
- **Production :** En ligne sur `https://junioradon79gm-001-site1.jtempurl.com` (HTTP 200 Healthy).
- **Passerelle Android WAZAP Gateway :** v1.1 déployée avec succès sur le smartphone `TECNO CM6` via USB (`Success`), service d'écoute lié et actif (`com.whatsapp.w4b` uniquement).
- **Architecture :** .NET 8 / EF Core / PostgreSQL (SmarterASP) + React 18 / Vite / TypeScript + YCloud API + WAZAP Gateway Android.

## 2. Chantiers Prioritaires en Cours (P0 - P1)
- **P0 [DNS / Bloqué Externe] :** Domaine officiel `wazap.ci` chez WiniHost en attente de mise à jour des serveurs NS Cloudflare (`bonnie` & `nicolas`). Fallback actif opérationnel.
- **P1 [Terrain & Produit] :** Tester en conditions réelles avec les recrues terrain le nouveau Cockpit Livreur (`RiderDashboardPage`), l'accès direct WhatsApp (`DASHBOARD`, `SOLDE`) et l'auto-login sans mot de passe `?u=...`.
- **P1 [Acquisition WhatsApp] :** Smartphone Business `+225 05 44 05 19 72` actif avec passerelle v1.1 étanche et réponses rapides (`/dispo`, `/tarifs`, `/course`, `/vendeur`).
- **P1 [TikTok] :** Calendrier éditorial @wazap_ci en cours.

## 3. Livrables Récents
- **Session 120 :** Espace Marchand & Livreur dans WhatsApp : commandes textuelles directes `DASHBOARD` et `SOLDE` pour les vendeurs (`VendorTextCommands`), URLs d'accès 1-clic directes dans les notifications WhatsApp de certification livreur et validation boutique, 847 tests réussis.
- **Session 121 (Incident P0 & Résolution) :** Neutralisation immédiate de l'interférence avec WhatsApp Personnel. Isolation étanche de la passerelle Android WAZAP Gateway (`ci.wazap.gateway`) restreinte strictement à `com.whatsapp.w4b` (WhatsApp Business). Blacklist locale et serveur du numéro personnel du propriétaire (`+225 07 08 32 33 66`) et de la ligne officielle (`05 44 05 19 72`). Suppression de l'auto-réponse universelle intrusive sur messages ordinaires. Compilation de l'APK v1.1 signée et déploiement avec succès sur le smartphone `TECNO CM6` via ADB USB (`Success`, service actif et lié). Ajout de 8 tests unitaires de sécurité (861 tests passants).
- **Session 122 :** Test de bout en bout du Cockpit Livreur (`RiderDashboardPage.tsx` & `RidersController`). Création d'une suite complète de 6 tests d'intégration backend (`RidersControllerDashboardTests.cs` : chargement dashboard, bascule dispo 1-tap, sélecteur de commune 1-tap, étanchéité EnsureOwnership, accès admin). Validation des 6 tests Vitest frontend (100% verts). Bundle de production compilé et synchronisé. Vérification HTTP 200 sur le serveur live. 867 tests .NET (861 locaux + 6 PG réels CI) au vert.
- **Session 123 :** Résolution définitive de la boucle de recrutement des candidats livreurs sur WhatsApp. `CaptureZone` enrichi avec support des chiffres seuls (1 à 6), emojis chiffres (1️⃣-6️⃣), préfixes courants (#1, Choix 1, Option 1, Zone 1) et sous-quartiers d'Abidjan (Angré, Riviera, Maroc, Biétry, etc.). `BuildAskMessage` explicite désormais la réponse directe par chiffre (1, 2, 3, 4 ou 5) en plus des liens cliquables pour éliminer le problème des liens `wa.me` réflexifs sous Android WhatsApp. Possibilité de corriger sa commune dynamiquement. 880 tests .NET (874 locaux + 6 PG en CI) et 57 tests Vitest 100% verts.
- **Session 124 :** Élimination totale de la surcharge cognitive et de la saisie texte : suppression définitive de l'envoi d'identifiants/mots de passe et de liens de connexion web. Remplacement par des liens d'action 1-clic bleus natifs `wa.me` (`DISPO`, `INDISPO`, `SOLDE`, `PROGRAMME`, `LIVRAISON`, `PRODUITS`, `TARIFS`). Support des commandes ultra-courtes à 1 chiffre (`1`, `2`, `3`, `4`) et emojis (`🟢`, `🔴`, `💰`, `📱`). Refonte des tableaux de bord WhatsApp pour livreurs et commerçants. 880 tests .NET (874 locaux + 6 PG en CI) et 57 tests Vitest 100% verts.

