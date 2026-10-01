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

* **Numéro WhatsApp Business Officiel Actif :** **`+225 05 44 05 19 72`**
  *(Ligne mobile WhatsApp Business active sur le terrain / acquisition directe).*
* **Format des liens d'action :** `https://wa.me/2250544051972?text=...` avec message pré-rempli adapté au persona (Commerçant ou Livreur).

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

3. **Routine Obligatoire de Publication (Textes de Description & Hashtags Clé en Main) :**
   * **Pour TOUTE vidéo produite ou scénario rédigé, il est STRICTEMENT OBLIGATOIRE de fournir systématiquement le texte de description (caption) prêt à poster** :
     - **Accroche (Hook) :** Question ou constat percutant dès la première ligne.
     - **Corps du texte :** Court, dynamique, axé sur les bénéfices concrets (0% commission, sécurité QR code, livreur en 3 min, 50 smartphones neufs tous les 3 mois).
     - **Call To Action (CTA) direct :** Raccourcis officiels WhatsApp **`05 44 05 19 72`** (Livreurs ➔ tape **DISPO** / Commerçants ➔ tape **COLIS**) ou liens courts `tinyurl.com/wazap-livreurs` / `tinyurl.com/wazap-commercants`.
     - **Grappe de Hashtags ciblés Abidjan :** Hashtags géolocalisés et sectoriels systématiques (#Wazap, #LivreurAbidjan, #CommerceAbidjan, #Team225, communes d'Abidjan).
     - **1er commentaire épinglé suggéré :** Question d'engagement pour stimuler l'algorithme TikTok.

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

---

## 7. QR Code Universel WAZAP & Paiement Multi-Opérateurs (Règle Canonique Inviolable)

* **Universalité Totale :**
  * Le QR code officiel WAZAP est **STRICTEMENT UNIVERSEL**.
  * **INTERDICTION FORMELLE ET DÉFINITIVE de le qualifier de « QR Wave » ou de réduire le mode de règlement à Wave seul.**
  * Le QR Code Universel WAZAP est scannable par tout smartphone (appareil photo natif, scanner universel ou application Mobile Money) et redirige vers l'interface de paiement fractionné GeniusPay/WAZAP.
  * Il prend en charge équitablement **TOUS les opérateurs de Côte d'Ivoire** :
    1. **Wave**
    2. **Orange Money**
    3. **MTN MoMo**
    4. **Moov Money**
    5. **Cartes Bancaires (Visa / Mastercard)**
* **Décision Stratégique Fondatrice (Zéro Exception Cash) :**
  * **WAZAP se concentre à 100% sur les 95% de clients qui acceptent le règlement exclusivement digital à la livraison.**
  * **AUCUNE exception de gestion d'espèces sur la marchandise n'est admise.**
  * Le livreur ne transporte ni n'encaisse d'espèces pour le commerçant.
  * Si un client refuse catégoriquement le scan du QR Code Universel, la course n'est pas éligible au réseau WAZAP. Cette fermeté absolue est le bouclier n°1 garantissant la confiance aveugle des commerçants.
* **Formulation Canonique Obligatoire (Clients, Vendeurs, Marketing) :**
  * *« Règlement à la livraison exclusivement par Scan du QR Code Universel WAZAP (compatible Wave, Orange Money, MTN, Moov, Carte bancaire). Aucun argent liquide accepté sur la marchandise. »*
* **Bénéfice Fondateur :** Le commerçant est crédité en temps réel sur son propre compte, le livreur perçoit ses frais de course sans délai, zéro manipulation d'espèces sur les produits, élimination intégrale du risque de vol ou de fuite de livreur.

---

## 8. Mode Opérationnel d'Acquisition Terrain & Cockpit WhatsApp Business (Règle Canonique)

* **Cockpit Semi-Automatique Actif sur le Smartphone (`+225 05 44 05 19 72`) :**
  * L'acquisition terrain et l'accueil en direct des livreurs et commerçants s'opèrent sur l'application mobile **WhatsApp Business** du smartphone officiel.
  * **Message d'accueil (STRICTEMENT ACTIVÉ - ON 🟢) :** Accueille instantanément tout nouvel arrivant (Livreur ➔ tape DISPO / Commerçant ➔ tape COLIS).
  * **Message d'absence (STRICTEMENT DÉSACTIVÉ - OFF ⚪) :** Interdiction d'activer le message d'absence qui parasite les échanges et fait fuir les candidats en pleine journée.
  * **Réponses Rapides 1-Clic (`/` dans la barre de saisie) :** Traitement immédiat des prospects :
    * **`/dispo`** : Envoie instantanément le parcours d'inscription livreur avec les 4 liens cliquables `wa.me` par commune (Zone Sud, Cocody, Yopougon, Abobo) + demande de photo CNI.
    * **`/tarifs`** : Grille officielle Grand Abidjan (1 000 F net même commune, 1 500 F voisine, 2 000 F pont/longue distance, 0% commission).
    * **`/course`** : Alerte de course prête à être acceptée par un livreur en 1 tap.
    * **`/vendeur`** : Accueil commerçant avec les 15 courses offertes (Pack Digital Boutique).
  * **Étiquettes de suivi couleur :** `🟢 Livreur DISPO`, `🟡 Enrôlement Livreur`, `🔵 Commerçant Partenaire`, `🟠 Course en cours`.
* **Terminologie Officielle Coursiers :** Terme officiel unique **« LIVREUR »** (remplace définitivement « Motard » sur tous les visuels, affiches, textes et communications).
* **Alignement Technique Backend :** Le serveur intègre `YCloudWebhookParser.cs` et `RiderRecruitmentService.cs` pour assurer la bascule et le relais API automatisé en production.

---

## 9. Déploiement Continu Systématique en Production (Règle Canonique Inviolable)

* **Principe Absolu :**
  * À chaque tâche, correction, ajout de fonctionnalité, page web, visuel ou texte de marketing, **les modifications DOIVENT ÊTRE SYSTÉMATIQUEMENT testées, committées et poussées en production via `git push origin main`**.
  * **INTERDICTION FORMELLE de laisser des modifications ou des commits accumulés uniquement en local sans les pousser en production.** Tout travail validé doit être immédiatement disponible en ligne pour l'utilisateur et les utilisateurs terrain.
* **Workflow d'Exécution Obligatoire à Chaque Mise à Jour :**
  1. **Validation Qualité :** Tests .NET (`dotnet test`) et tests Vitest (`npm test`) validés à 100% (0 échec).
  2. **Build de Production :** `npm run build` exécuté et bundle synchronisé dans `src/Wazap.API/wwwroot/app`.
  3. **Documentation :** Mise à jour synthétique de `MEMOIRE.md`.
  4. **Publication & Déploiement :** `git add -A`, `git commit` avec message clair et `git push origin main`.
  5. **Vérification en Ligne :** Vérification de l'aboutissement du pipeline GitHub Actions (`Deploy prod`) et du statut HTTP 200 sur le serveur de production.

---

## 10. Règle Canonique Inviolable : ZÉRO SAISIE TEXTE / 1-TAP ABSOLU (L'Essence Fondatrice WAZAP)

* **Principe Fondateur & Inviolable :**
  * **IL EST STRICTEMENT INTERDIT D'AMENER LES USAGERS (LIVREURS, COMMERÇANTS OU CLIENTS) À TAPER DU TEXTE LIBRE AU CLAVIER DU MIEUX POSSIBLE.**
  * C'est la force absolue et différentiatrice de WAZAP sur le terrain à Abidjan face à une cible terrain (coursiers indépendants, commerçants de quartier) qui n'a pas forcément un niveau scolaire élevé et pour qui toute saisie au clavier est une friction bloquante.
* **Mise en Application Systématique :**
  1. **Liens d'Action 1-Clic Pré-remplis (`wa.me`) :** Tout appel à l'action WhatsApp doit être un lien bleu direct `https://wa.me/2250544051972?text=...` (ex : `DISPO`, `INDISPO`, `ACCEPTE`, `COLIS`, `DASHBOARD`, `1 Cocody`, etc.). L'utilisateur touche le lien, le texte est déjà inséré dans sa barre d'envoi, il n'a qu'à appuyer sur la flèche verte.
  2. **Zéro Formulaire pour l'Identité :** Le livreur envoie simplement la photo de sa pièce (CNI, Permis de Conduire ou Passeport). L'OCR Google Cloud Vision extrait automatiquement le nom complet, le numéro et la date d'expiration sans aucune saisie manuelle.
  3. **Zéro Saisie pour le Règlement :** Le client scanne le QR Code Universel WAZAP en 1 tap avec son application Mobile Money habituelle (Wave, OM, MTN, Moov, Carte). Zéro saisie de numéro de compte, zéro manipulation d'espèces.
  4. **Boutons 1-Clic dans le Dashboard Admin :** L'administrateur dispose de boutons directs pour envoyer en un tap les accès et liens pré-remplis aux livreurs et commerçants sans composer de message manuel.
---

## 11. Harnais d'Ingénierie & Mémoire ECC (Everything Claude Code)

* **Emplacement du Framework Mutualisé :** `C:\Dev\everything-claude-code` (accessible pour WAZAP et tous les projets).
* **Découplage de la Mémoire :**
  * **Invariants stricts fail-closed :** [`.ecc/memory/project/invariants.md`](file:///c:/Dev/Wazap/.ecc/memory/project/invariants.md).
  * **Handoff actif de session :** [`.ecc/memory/project/active_sprint.md`](file:///c:/Dev/Wazap/.ecc/memory/project/active_sprint.md).
  * **Mémoire active du projet :** [`MEMOIRE.md`](file:///c:/Dev/Wazap/MEMOIRE.md) (synthétique, < 500 lignes, chantiers P0/P1 et sessions récentes).
  * **Archives historiques :** [`docs/HISTORIQUE_SESSIONS.md`](file:///c:/Dev/Wazap/docs/HISTORIQUE_SESSIONS.md) (sessions 1 à 111 et audits).
* **Mobilisation des Compétences & Sous-Agents :** L'agent peut s'appuyer sur les règles modulaires (`rules/common`, `rules/csharp`, `rules/react`) et les compétences d'ingénierie d'ECC (`skills/unified-memory`, `skills/verification-loop`, `skills/strategic-compact`, `skills/security-review`).

