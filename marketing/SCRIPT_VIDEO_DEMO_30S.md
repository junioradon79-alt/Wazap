# 🎬 Script vidéo démo WAZAP — 30 secondes

> **Usage** : vidéo de démonstration pour la campagne prospects (variable `{{3}}` des templates
> `prospect_*_v2`), la page `…/demo-video.html` et le partage WhatsApp.
> **Durée cible** : 30 s (≤ 35 s) · **Format** : 16:9 (YouTube/Site) + variante 9:16 (WhatsApp/TikTok)
> **Ton** : « ton pote qui livre » — clair, urbain, vert émeraude vibrant.
> **Couleurs officielles** : vert émeraude `#00A86B` / `#075E54` · **Logo officiel** : `marketing/visuels/logo_officiel_wazap.jpg`
> **Numéro officiel** : `+225 07 87 11 95 20` · **Lien final** : `https://junioradon79gm-001-site1.jtempurl.com/demo-video.html`

---

## 0. ⏱️ La vidéo en 30 s (résumé)

| Plan | Temps | Durée | Rôle |
|---|------|------|------|
| 1 | 0:00 – 0:04 | 4 s | Hook / problème |
| 2 | 0:04 – 0:08 | 4 s | Marque + promesse |
| 3 | 0:08 – 0:13 | 5 s | Étape 1 — commande WhatsApp |
| 4 | 0:13 – 0:18 | 5 s | Étape 2 — livreur certifié assigné |
| 5 | 0:18 – 0:23 | 5 s | Étape 3 — suivi live + scan QR Code Universel |
| 6 | 0:23 – 0:27 | 4 s | Confiance (Mobile Money + Assurance Colis Sûr) |
| 7 | 0:27 – 0:30 | 3 s | CTA final |
| — | +2 s | Logo / carte de fin |

> Total : **~32 s** avec la carte de fin — dans la limite des 35 s (script `08` coupe à 35 s).

---

## 1. 🎬 Découpage plan par plan

### Plan 1 — 0:00–0:04 · HOOK (4 s)
- **Visuel** : notification WhatsApp « une commande » + carte d'Abidjan, ambiance « refus de livraison ».
- **Texte à l'écran** : `❌ « Désolé, on ne livre pas ce soir »`
- **Voix off (option)** : « À Abidjan, faire livrer, c'est souvent le parcours du combattant. »
- **Son** : notification WhatsApp (pop) puis musique qui démarre.
- **Prompt IA (si génération)** :
  > `Close-up smartphone screen in Abidjan at dusk, a restaurant refuses a delivery, dark mood, single notification « désolé on ne livre pas » pops on a WhatsApp chat, cinematic, 1080p`

### Plan 2 — 0:04–0:08 · MARQUE + PROMESSE (4 s)
- **Visuel** : logo WAZAP officiel (badge circulaire vert émeraude, éclair stylisé, motard coursier et lettrage 3D blanc) qui apparaît avec dynamisme.
- **Texte à l'écran** : `WAZAP ⚡ — La livraison WhatsApp à Abidjan · 30 s pour comprendre`
- **Voix off** : « WAZAP règle ça en 3 étapes. »
- **Prompt IA** :
  > `Official circular emerald green WAZAP brand logo (#00A86B) with 3D white typography and speed lightning icon, bold white text « WAZAP · livraison Abidjan », confident, 1080p`

### Plan 3 — 0:08–0:13 · ÉTAPE 1 — Le client commande (5 s)
- **Visuel** : capture WhatsApp réelle : `1 poulet braisé + alloco 🍗` → adresse → bouton **« Commander ✓ »**.
- **Texte à l'écran** : `1️⃣ Le client commande sur WhatsApp`
- **Voix off** : « Un : votre client envoie sa commande sur votre WhatsApp. »
- **Réel à capturer** : capture de `demo` ou d'un échange WhatsApp de test (voir §3).
- **Prompt IA (repli)** :
  > `WhatsApp chat screenshot on phone, a customer orders grilled chicken and alloco in French with a delivery address, green "Commander" button highlighted, bright restaurant setting, 1080p`

### Plan 4 — 0:13–0:18 · ÉTAPE 2 — Livreur certifié assigné (5 s)
- **Visuel** : carte d'Abidjan, marqueur localisé « livreur certifié ✅ · à 1,2 km », tracé de course.
- **Texte à l'écran** : `2️⃣ Un livreur certifié proche prend la course`
- **Voix off** : « Deux : un livreur certifié, proche du resto, prend la course. »
- **Prompt IA** :
  > `Map of Abidjan with a green delivery route animated, a certified rider marker with check badge moving along the route from a restaurant to a client, clean flat UI, green accents, 1080p`

### Plan 5 — 0:18–0:23 · ÉTAPE 3 — Suivi + validation par Scan QR Code Universel (5 s)
- **Visuel** : écran de suivi client PWA (`SuiviPage.tsx`) : position GPS live, barre de statut
  « Le livreur arrive… », puis **Scan du QR Code Universel** sur le colis validé instantanément en vert.
- **Texte à l'écran** : `3️⃣ Suivi en direct · validation instantanée par Scan QR`
- **Voix off** : « Trois : le client suit la course en direct et scanne le QR Code du colis pour valider. »
- **Prompt IA** :
  > `Phone screen showing live GPS order tracking in Abidjan, customer camera scanning a physical parcel QR code sticker, instant green confirmation checkmark « Livraison Validée », modern flat design, 1080p`

### Plan 6 — 0:23–0:27 · CONFIANCE (4 s)
- **Visuel** : paiement Mobile Money (Wave/Orange/MTN) validé + badge « 🛡️ Assurance Colis Sûr » + ticket tombola 25 000 F.
- **Texte à l'écran** : `💳 Paiement Mobile Money · 🛡️ Assurance Colis Sûr`
- **Voix off** : « Paiement Mobile Money sécurisé et Assurance Colis Sûr incluse. »
- **Prompt IA** :
  > `Mobile money payment approved on phone in Abidjan, green checkmark, insurance shield badge « Assurance Colis Sûr », warm trustworthy mood, emerald green palette, 1080p`

### Plan 7 — 0:27–0:30 · CTA FINAL (3 s)
- **Visuel** : Logo officiel + Pack Digital Boutique offert + bouton « 🚀 Activer mon commerce ».
- **Texte à l'écran** :
  `🎁 15 courses offertes + Pack Digital Boutique`
  `👉 Activez votre commerce → wa.me/2250787119520`
- **Voix off** : « Vos 15 premières courses et votre Pack Digital Boutique sont offerts. Activez votre commerce maintenant. »
- **Prompt IA** :
  > `End card with official WAZAP logo, big emerald green button « Activer mon commerce », phone number +225 07 87 11 95 20 visible, bold, clean, 1080p`

### Carte de fin (+2 s)
- **Visuel** : logo WAZAP officiel + « Assurance Colis Sûr · Livreurs certifiés · Mobile Money » + **Lien WhatsApp** `wa.me/2250787119520`
- **Voix off** : — (musique qui se coupe) · **Son** : outro court.

---

## 2. 🎙️ Voix off complète (option — 1 seul enregistrement)

> **~24 s de lecture** → parfait pour 30 s avec les pauses.

> « À Abidjan, faire livrer, c'est souvent le parcours du combattant. WAZAP règle ça en 3 étapes.
> Un : votre client commande sur WhatsApp. Deux : un livreur certifié proche prend la course.
> Trois : le client suit en direct et scanne le QR Code du colis pour valider.
> Paiement Mobile Money sécurisé, Assurance Colis Sûr incluse. Vos 15 premières courses et le Pack Digital Boutique sont offerts.
> Activez votre commerce maintenant. »

---

## 3. 🎥 Ce qu'il faut CAPTURER (si démo écran réelle)

| Élément | Où le trouver | Source |
|---|---|---|
| Logo WAZAP Officiel | `marketing/visuels/logo_officiel_wazap.jpg` | Source officielle canonique |
| Chat WhatsApp « commande » | Envoyer un test réel sur une commande de démonstration | capture mobile |
| Suivi en direct | PWA `SuiviPage.tsx` | capture mobile |
| Carte / trajet | Vue livreur admin (liste des courses) | capture desktop |
| Scan QR Code | Caméra scannant le QR Code Universel | validation verte instantanée |

> ⚠️ **Ne jamais montrer de numéros de clients réels ni de noms privés.** Utiliser des données de test.

---

## 4. 🤖 Option B — Génération automatique (IA)

Outils compatibles avec le storyboard ci-dessus (prompts par plan en §1) :

| Outil | Usage | Coût |
|---|---|---|
| **Runway Gen-3 / Kling / Pika** | générer chaque plan en 5 s à partir des prompts | essai gratuit, puis abo |
| **Veo 3 (Google)** | plans 3-5 (écran) très réalistes | free preview |
| **CapCut** (gratuit) | montage : associer les plans + sous-titres automatiques + musique | — |

**Ordre conseillé** :
1. Générer les 8 plans (1 vidéo par plan, 6-8 s chacun).
2. Monter dans CapCut à la frise du §1.
3. Ajouter **sous-titres incrustés toujours lisibles** (mobile = son souvent coupé).
4. Exporter **H.264, 1080p, ≤ 35 s, < 5 Mo** de préférence (chargement WhatsApp mobile).

---

## 5. 📤 Mise en production (après tournage)

1. Récupérer le fichier exporté `demo.mp4` sur le PC.
2. Lancer :
   ```powershell
   powershell .\WazapSln\scripts\activation\08-host-demo-video.ps1 -Source "C:\...\demo.mp4"
   ```
   (ou sans `-Source` : auto-recherche stricte des fichiers nommés `demo*`/`wazap*` dans
   Downloads/Desktop — hors TikTok)
3. Vérifier le récap (taille < 8 Mo, durée ≥ 20 s) puis **commit + push** :
   ```powershell
   cd C:\Dev\Wazap\WazapSln
   git add src/Wazap.API/wwwroot/demo.mp4
   git commit -m "Héberge la vidéo démo 30 s"
   git push
   ```
4. Le CI déploie `demo.mp4` → la page `…/demo-video.html` l'affiche et la campagne `07`
   l'utilise comme variable `{{3}}` (défaut déjà câblé sur `…/demo-video.html`).

---

## 6. ✅ Check-list avant tournage

- [ ] Script à l'écran validé (problème → 3 étapes → confiance → 15 commandes offertes)
- [ ] Logo PNG à haute résolution à portée de main
- [ ] 2-3 captures WhatsApp/captures écran prêtes (éviter toute donnée réelle)
- [ ] Numéro +225 05 75 80 38 01 visible et lisible ≥ 1 s
- [ ] URL `…/demo-video.html` → raccourci propre (bit.ly) si place en CTA
- [ ] Format 16:9 + variante 9:16 (story) si temps
- [ ] Export H.264 ≤ 35 s · sous-titres incrustés · pas de son requis pour comprendre