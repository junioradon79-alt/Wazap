# 🟢 Campagne WhatsApp WAZAP — Kit complet & Statuts Quotidiens

> **Mise à jour : 01/10/2026** · Numéro WhatsApp Business Officiel Unique : **`+225 05 44 05 19 72`** (Passerelle YCloud)  
> **Lien 1-Clic d'action Vendeur :** [`https://wa.me/2250544051972?text=COLIS`](https://wa.me/2250544051972?text=COLIS)  
> **Lien 1-Clic d'action Livreur :** [`https://wa.me/2250544051972?text=DISPO`](https://wa.me/2250544051972?text=DISPO)  
> **Objectif :** Convertir les commerçants d'Abidjan (15 courses offertes, sécurité 0 cash) & recruter des livreurs vérifiés CNI.

## 1. Statuts WhatsApp Officiels (Visuels prêts dans `marketing/whatsapp/statuts/`)
Cadence recommandée : **5-7 statuts/semaine**, aux heures de pointe (08h-09h / 12h-13h / 18h-20h).

| Visuel HD | Rôle & Usage | Accroche prête à copier (Statut WhatsApp) |
|---|---|---|
| `statut_modele_01_fini_les_livreurs.png` | **🌟 MODÈLE 1 OFFICIEL** · Sécurité Absolue | « 🚨 WAZAP.CI : FINI LES LIVREURS QUI DISPARAISSENT AVEC VOTRE ARGENT ! Le client scanne le QR code, votre recette arrive en direct sur votre compte. Envoie COLIS au 05 44 05 19 72 ⚡ » |
| `statut_modele_02_fini_les_livreurs.png` | **🌟 MODÈLE 2 OFFICIEL** · Encaissement Garanti | « 🛡️ Zéro cash sur la marchandise = Zéro vol possible ! Virement direct Wave, OM, MTN. Vos 15 premières livraisons offertes. Tape COLIS au 05 44 05 19 72 📲 » |
| `statut_offre_commercants.png` | Offre Pack Boutique · 15 courses offertes | « La livraison de TON quartier, 100% WhatsApp. 15 livraisons offertes sans commission pour tester. Tape COLIS au 05 44 05 19 72 🎁 » |
| `statut_recrutement_livreurs.png` | Recrutement Livreurs · Dignité Motard | « Tu livres à Abidjan ? 0% commission, 1000 F net minimum dès le 1er mètre + défi smartphone Redmi 15C. Tape DISPO au 05 44 05 19 72 🛵 » |
| `statut_parrainage.png` | Parrainage +5 crédits | « Chaque commerçant parrainé = +5 courses offertes pour toi 🎁 Partage le lien WAZAP ! » |

Conseils statuts : toujours **1 action claire** (1-tap `wa.me`), afficher le QR Code Universel en grand, répondre aux réactions (👀/🔥) en privé sous 15 min.

## 2. Séquences de messages WhatsApp Business (1-Tap direct)
Fenêtres d'envoi : **8h30-11h30 / 14h30-17h30** · via réponses rapides WhatsApp Business (`/vendeur`, `/dispo`, `/tarifs`).

### Séquence A — Prospect commerçant (issu QR/lead/statut)
- **J0 (accueil instantané message de bienvenue)** :
  > « Bonjour 👋 Bienvenue chez WAZAP !
  > Pour activer vos **15 premières livraisons offertes sans commission** :
  > Envoyez simplement le nom de votre commerce et votre commune (ex: "Boutique Awa, Cocody").
  > Fini les coursiers qui disparaissent avec votre argent : le client scanne le QR Code Universel et vous encaissez en direct ! 🛡️ »
- **J+1 (activation course test)** :
  > « Bonjour ! Prêt à faire partir votre premier colis en 3 minutes chrono ?
  > Touchez ce lien direct pour demander un livreur : https://wa.me/2250544051972?text=COLIS »
- **J+3 (suivi satisfaction)** :
  > « Bonjour, comment s'est passée votre dernière livraison ? Notre support reste disponible 7j/7 pour sécuriser vos ventes. »

### Séquence B — Candidat Livreur (source statut/groupe)
- **J0 (réponse automatique via `/dispo`)** :
  > « Bonjour champion ! Bienvenue dans la flotte officielle WAZAP 🛵
  > ✅ 0% commission (100% du tarif pour toi)
  > ✅ 1 000 F net minimum garanti par course
  > ✅ 0 cash marchandise à transporter
  > 📸 Envoie la photo de ta CNI, Permis ou Passeport pour être certifié en 2 minutes ! »
- **J+3** : « Avez-vous reçu des courses ? Dispo ce week-end ? » · **J+7** : « Astuce : soyez DISPO aux heures de pointe (12h-14h / 18h-21h) pour maximiser vos courses. »

## 3. Diffusions & groupes
- **Diffusion (listes opt-in)** : seulement aux contacts qui ont déjà écrit (fenêtre 24 h) ; message court + image statut ; jamais en masse sans opt-in.
- **Groupes commerçants/coursiers** : rejoindre 5-10 groupes, y **apporter de la valeur** (conseils, offre), 1 message max/jour, jamais de spam ; proposition en DM plutôt qu'en groupe.
- **STOP/opt-out** : si un contact répond « STOP » ou « non merci » → ne plus relancer (marquer Écarté dans `/app/leads`).

## 4. Modèles Meta (quand approuvés) — prospect_approach / prospect_followup / prospect_offer
1. `prospect_approach` (1er contact) : « Bonjour {{1}}, ici {{2}} de WAZAP… » → variable 3 = lien de la page/vidéo.
2. `prospect_followup` (J+3) : « Avez-vous bien reçu mon message ? … »
3. `prospect_offer` (offre) : « WAZAP vous offre vos 15 premières commandes… Puis-je vous ajouter ? »
→ Dès statut `Approved`, activation des noms dans l'app (config) et déploiement (5 min), puis envoi automatique via la campagne d'acquisition.

## 5. Suivi & attribution
- QR/liens trackés : `src=whatsapp-statut`, `whatsapp-parrainage`, `recrutement-rider` → visibles dans `/app/leads`.
- Toute conversion (lead → vendeur actif) : statut `Converted` + code parrainage communiqué.
- KPI hebdo : messages envoyés < 100/j → réponses ≥ 25 % → **leads → 8-10 inscriptions**.

## 6. Répartition type (semaine)
| Jour | Action |
|---|---|
| Lun | Statut offre commerçants + relance séquence A (J+3/J+7) |
| Mar | Recrutement livreurs (statut + groupes) + suivi B |
| Mer | Statut parrainage + nouveaux leads J0 |
| Jeu | Statut offre + relances |
| Ven | Offre de fin de semaine + bilan leads convertis |
