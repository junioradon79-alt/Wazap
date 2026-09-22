# Directives Projet WAZAP & Charte Permanente

## 1. Charte Graphique & Logo Officiel WAZAP (Référence Canonique)

* **Logo Officiel & Avatar Réseaux :**
  * Fichiers sources canoniques : `marketing/visuels/logo_officiel_wazap.jpg` et `web/public/logo-officiel-2026.jpg`
  * **Spécifications visuelles :** Badge circulaire fond vert émeraude vibrant (#00A86B à #075E54), lettrage WAZAP blanc 3D moderne en relief, icône stylisée d'un éclair de vitesse et d'un motard coursier.
  * **Application obligatoire :** Ce logo doit être systématiquement utilisé pour toutes les productions ultérieures :
    * Avatars des réseaux sociaux (TikTok `@wazap_ci`, Facebook, Instagram, WhatsApp Business).
    * Supports physiques & goodies (Chevalets de comptoir acryliques, QR Codes Universels, flyers A5, scellés Colis Sûr).
    * Interfaces Web et dashboards.
    * Incrustations de fin et filigranes dans les vidéos promotionnelles.

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
