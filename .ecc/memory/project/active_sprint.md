# [SPRINT ACTIF & HANDOFF DE SESSION WAZAP]
<!-- ecc.memory.v1 scope:project type:handoff status:active updated:2026-10-01 -->

## 1. État Actuel du Système
- **Backend .NET :** 861 tests (855 réussis localement + 6 sur PostgreSQL réel en CI) - 0 échec.
- **Frontend Vitest :** 57/57 tests réussis (100%).
- **Production :** En ligne sur `https://junioradon79gm-001-site1.jtempurl.com` (HTTP 200 Healthy, Commit `95f86a4`).
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
