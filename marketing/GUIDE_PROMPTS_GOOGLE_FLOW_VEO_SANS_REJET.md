# 🛡️ Guide Officiel des Prompts Google Flow / Veo — Zéro Rejet & Zéro Filtre

> **Objectif :** Garantir que **100% des prompts vidéo saisis dans Google Flow (Veo / Imagen 3)** soient acceptés instantanément par les algorithmes de sécurité Google, sans aucun rejet de politique, sans bug d'hallucination linguistique (anglais parasite) et avec un rendu visuel photoréaliste de niveau cinéma.

---

## 🚫 1. La Blacklist Absolue des Mots & Notations Interdits dans Flow

Google Flow / Veo applique des filtres de sécurité stricts (lutte contre la fraude financière, protection de la vie privée/PII, respect des marques et prévention des contenus abusifs).

| Catégorie de Filtre | ❌ Mots & Expressions STRICTEMENT INTERDITS dans Flow | Pourquoi c'est bloqué par Google | ✅ Vocabulaire de Remplacement Visuel (100% Sûr) |
|---|---|---|---|
| **Financier / Devises** | `billets`, `billets de 10 000 F`, `monnaie`, `espèces`, `argent liquide`, `argent`, `cash`, `FCFA`, `Francs`, `25 000 F`, `10 000 F`, `5 000 F`, `sous`, `banknotes`, `money`, `currency` | Déclenche le filtre anti-fraude financière, faux-monnayage et transactions illégales. | `boîte de luxe scellée`, `paquet cadeau avec ruban`, `colis protecteur blanc`, `flacon précieux`, `smartphone lumineux`. |
| **Coordonnées & PII** | Numéros de téléphone (`05 44 05 19 72`, `+225...`), liens web (`wazap.ci`, `wa.me`), marque `WhatsApp` | Déclenche le filtre de protection des données personnelles (PII) et des marques tierces. | `écran de smartphone avec interface verte moderne`, `carte interactive de géolocalisation`, `QR Code stylisé`. |
| **Marketing Agressif** | `gratuit`, `offert`, `commission offerte`, `15 livraisons gratuites`, `promotion`, `rabais`, `réduction`, `gagnez` | Déclenche le filtre anti-spam publicitaire et arnaques promotionnelles. | `geste de satisfaction`, `sourire chaleureux`, `pouce levé confiant`, `poignée de main complice`. |
| **Mots Négatifs / Criminalité** | `vol`, `arnaque`, `fuite`, `fuir`, `voleur`, `escroquerie`, `faux billet`, `jeté dans la lagune`, `danger`, `agression` | Déclenche le filtre de sécurité publique et d'activités délictueuses. | `confiance mutuelle`, `professionnalisme`, `présentation fière d'un badge vérifié`, `sérénité et soulagement`. |
| **Formatage & Dialogues** | Guillemets `« ... »`, balises de nom `Fatou :`, `Awa :`, textes de dialogue bruts dans le prompt. | Veo tente de faire parler les personnages en anglais ou génère des déformations buccales bizarres. | **ZÉRO dialogue dans le prompt Flow.** Le prompt ne décrit QUE l'image et l'action. La voix est injectée en post-production. |

---

## 📐 2. La Formule Magique d'un Prompt Google Flow Parfait

Pour obtenir un clip cinématique impeccable en format 9:16 vertical sans aucun défaut :

```text
[Type de Plan & Cadrage] + [Sujet & Protagoniste] + [Action & Émotion naturelle] + [Décor & Accessoires soignés] + [Éclairage & Rendu Cinéma] + [Format vertical 9:16]
```

### Exemple Concret :
* ❌ **Mauvais prompt (rejeté par Flow) :**  
  *« Plan sur Fatou qui tend un gâteau de 25 000 F et des billets de 10 000 F au livreur pour la monnaie. Fatou : Tiens Bakary prends les sous ! Appelez au 05 44 05 19 72 »*
* ✅ **Prompt Canonique Parfait (accepté à 100%, clip généré avec succès) :**  
  *« Plan moyen cinématique vertical 9:16. Dans une pâtisserie moderne et lumineuse à Abidjan, une cheffe pâtissière ivoirienne souriante en veste de cuisine blanche impeccable remet une somptueuse boîte de gâteau blanche scellée à un coursier courtois en polo vert émeraude. Le coursier hoche la tête avec respect et présente poliment son smartphone affichant un code digital. Échange chaleureux et professionnel, arrière-plan de pâtisserie haut de gamme, éclairage doux, photoréalisme. »*

---

## 🔄 3. Le Pipeline de Production en 3 Étapes

```text
┌──────────────────────────────┐
│ ÉTAPE 1 : GOOGLE FLOW (VEO)  │ ➔ Génère 1 rush vidéo muet (8-10s) ultra-réaliste
│ (Prompt 100% visuel épuré)   │   sans aucun mot interdit.
└──────────────┬───────────────┘
               │ Téléchargement MP4
               ▼
┌──────────────────────────────┐
│ ÉTAPE 2 : AUDIO & VOIX-OFF   │ ➔ Génère la voix française soignée (Edge-TTS Denise/Henri)
│ (Edge-TTS multilocuteur)     │   avec les dialogues percutants et les chiffres clés.
└──────────────┬───────────────┘
               │
               ▼
┌──────────────────────────────┐
│ ÉTAPE 3 : MONTAGE AUTOMATISÉ │ ➔ Assemble le rush Veo + Outro officielle WAZAP (Logo 3D,
│ (Script Python / FFmpeg)     │   QR Code, WhatsApp 05 44 05 19 72) + Musique de fond.
└──────────────────────────────┘
```

---

## 🎯 4. Règles d'Habillage de Fin & Numéro Officiel

1. **L'écran de fin (Outro) ne se génère JAMAIS dans Flow :** On utilise le clip officiel existant [`videos/06_logos_animations_outros/outro_officielle_wazap_9_16.mp4`](file:///c:/Dev/Wazap/videos/06_logos_animations_outros/outro_officielle_wazap_9_16.mp4).
2. **Durée de l'Outro :** 4,0 secondes (animée) à 6,5 secondes (avec extension fixe) pour laisser le temps à la voix-off d'énoncer clairement le numéro : **`05 44 05 19 72`**.
3. **Logo Officiel Canonique :** Badge circulaire vert émeraude vibrant avec liseré blanc et lettrage 3D relief (fichiers sources `marketing/visuels/logo_officiel_wazap.jpg` et `web/public/logo-officiel-2026.jpg`).
