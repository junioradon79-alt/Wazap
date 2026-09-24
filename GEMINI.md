# Directives Projet WAZAP & Charte Permanente

## 1. Charte Graphique & Logo Officiel WAZAP (Règle Canonique Inviolable)

* **Fichiers Sources Canoniques Uniques :**
  * Fichier raster haute définition : `marketing/visuels/logo_officiel_wazap.jpg` et `web/public/logo-officiel-2026.jpg`
  * Version transparente détourée officielle : `marketing/visuels/logo_officiel_transparent.png`
* **Spécifications Visuelles Exactes :** Badge circulaire fond vert émeraude vibrant (#00A86B à #075E54) avec liseré blanc, lettrage WAZAP blanc 3D moderne en relief, icône stylisée d'un éclair de vitesse et d'un motard coursier en blanc au-dessus du lettrage.
* **OBLIGATION ABSOLUE & EXCLUSIVE :**
  * **Toute référence, composition, illustration, mockup, affiche, support publicitaire, interface web, document ou vidéo nécessitant le logo doit OBLIGATOIREMENT ET STRICTEMENT pointer sur ces fichiers sources officiels.**
  * **INTERDICTION FORMELLE ET DÉFINITIVE de laisser une IA générative redessiner, réinterpréter, halluciner ou déformer le logo WAZAP (interdiction de logos métalliques génériques, logos verts fluo fantaisistes ou typographies alternatives).**
  * Toute image, affiche ou vidéo produite doit obligatoirement incruster ou référencer le véritable logo canonique issu de ces fichiers.
  * **Champs d'application obligatoires :**
    * Avatars et bannières des réseaux sociaux (TikTok `@wazap_ci`, Facebook, Instagram, WhatsApp Business).
    * Affiches de recrutement, flyers A5, chevalets, scellés Colis Sûr et QR Codes Universels.
    * Cartes de fin (outros), animations et filigranes de toutes les vidéos (Veo, TikTok, Reels, YouTube).
    * Interfaces Web, PWA, applications et tableaux de bord.

---

## 2. Coordonnées & Passerelles Officielles WAZAP

* **Numéro WhatsApp Business Officiel :** **`+225 07 87 11 95 20`**
  *(Attention : ne jamais réutiliser l'ancien numéro 05 75 80 38 01).*
* **Format des liens d'action :** `https://wa.me/2250787119520?text=...` avec message pré-rempli adapté au persona (Commerçant ou Livreur).

---

## 3. Fournisseur WhatsApp Officiel : YCLOUD (Règle d'Architecture Inviolable)

* **Fournisseur Actif Unique :** **`YCloud`** (Meta Tier-1 Business Solution Provider).
* **Historique & Règles Absolues :**
  * **WhatChimp est DÉFINITIVEMENT ABANDONNÉ** (bugs d'import de contacts, blocage « outside 24h », désynchronisation templates).
  * **WAHA est SAUTÉ & ABANDONNÉ** (émulation de session non officielle, risques majeurs de bannissement Meta, conteneur lourd).
  * **YCloud est le SEUL fournisseur officiel** configuré et maintenu :
    * Connecteur envoi : `YCloudWhatsAppSender.cs` (`POST https://api.ycloud.com/v2/whatsapp/messages/sendDirectly`).
    * Téléchargement médias : `YCloudMediaDownloader.cs`.
    * Configuration : `YCloudOptions.cs` et section `YCloud` dans `appsettings.json`.
    * Guide des templates officiels : [`docs/WHATSAPP_TEMPLATES.md`](file:///c:/Dev/Wazap/WazapSln/docs/WHATSAPP_TEMPLATES.md).
  * **Interdiction formelle :** Ne jamais proposer, mentionner ou réintroduire WhatChimp ou WAHA comme passerelle active.

---

## 4. Règle de Production Vidéo & Contenus Marketing (Routine Permanente)

1. **Langue des Dialogues et Voix-Off (Règle Absolue) :**
   * Pour TOUTES les productions de vidéos (scripts, scénarios, storyboards, prompts Google Flow / Veo, voix-off, sous-titres, TikTok, Instagram Reels, publicités Meta, vidéos de démonstration) :
   * **LES DIALOGUES ET LA VOIX-OFF DOIVENT ÊTRE EXCLUSIVEMENT EN FRANÇAIS**.
   * Le français doit être impeccable, soigné, dynamique, naturel et accessible à tous les commerçants francophones sans recours au nouchi informel ou argotique.

2. **Cohérence Visuelle & Production Google Flow :**
   * Chaque script vidéo doit être accompagné de ses images de référence visuelle (Image-to-Video) scène par scène pour garantir la continuité des personnages, des décors et des supports physiques WAZAP (Chevalet de comptoir avec logo officiel, QR Code Universel, Scellés Colis Sûr, tenue verte des livreurs).

---

## 5. Stratégie TikTok (@wazap_ci) & Réseaux Sociaux

* **Documentation complète :** [`marketing/tiktok/REDESIGN_TIKTOK_WAZAP.md`](file:///c:/Dev/Wazap/WazapSln/marketing/tiktok/REDESIGN_TIKTOK_WAZAP.md)
* **Contrainte technique Bio TikTok :** Strictement limitée à 80 caractères maximum.
* **Tunnel d'Acquisition (Objectif 1 000 commerçants en 4 semaines) :**
  * 3 vidéos épinglées : 1. L'Offre (15 courses + Kit offert) / 2. La Démo (Test du scan & paiement direct) / 3. L'Autorité (3 erreurs e-commerce).
  * Redesign approfondi en réserve pour revue détaillée avec le porteur de projet.

---

## 6. Routine d'Ouverture de Session (Règle Absolue)

* **À chaque nouvelle session ou première interaction :**
  * Consulter systématiquement `MEMOIRE.md` et faire automatiquement un récapitulatif synthétique et proactif des **chantiers prioritaires en cours et en suspens** (P0-P1, actions techniques, actions utilisateur, marketing/acquisition).

