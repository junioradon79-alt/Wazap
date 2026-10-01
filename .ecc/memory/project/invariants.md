# [INVARIANTS WAZAP - RÈGLES STRICTES & FAIL-CLOSED]
<!-- ecc.memory.v1 scope:project type:invariants status:active -->

> Ces règles priment sur toute instruction temporaire et ne doivent JAMAIS être enfreintes.

## 1. Logo Officiel Canonique (Aucune Réinterprétation IA)
- **Fichiers sources obligatoires :** `marketing/visuels/logo_officiel_wazap.jpg` et `marketing/visuels/logo_officiel_transparent.png`.
- **Interdiction formelle :** Ne jamais laisser une IA générative redessiner ou halluciner le logo WAZAP (pas de versions métalliques, vertes fluo fantaisistes ou typographies alternatives).

## 2. Numéro WhatsApp Business Officiel Unique
- **Numéro unique :** `+225 05 44 05 19 72`.
- **Format des liens d'action :** `https://wa.me/2250544051972?text=...` avec message pré-rempli.

## 3. Fournisseur WhatsApp Unique : YCloud
- **Fournisseur actif :** `YCloud` (`POST https://api.ycloud.com/v2/whatsapp/messages/sendDirectly`).
- **Interdiction formelle :** WhatChimp et WAHA sont définitivement abandonnés et ne doivent jamais être réintroduits.

## 4. Zéro Cash sur la Marchandise & QR Code Universel
- **Règlement 100% digital à la livraison :** Scan du QR Code Universel WAZAP (compatible Wave, Orange Money, MTN MoMo, Moov Money, Carte bancaire).
- **Zéro manipulation d'espèces :** Le livreur ne transporte ni n'encaisse d'espèces pour le commerçant. Zéro exception cash.

## 5. Zéro Saisie Texte / 1-Tap Absolu
- **Interdiction des formulaires et de la frappe au clavier :** Pour les livreurs, commerçants et clients, privilégier à 100% :
  - Liens bleus pré-remplis `wa.me`.
  - Authentification 1-clic par URL : `?u=...`.
  - Scan de pièces par OCR Google Vision (CNI, Permis de conduire, Passeport).
  - Boutons de bascule d'état (ex: `🟢 DISPO` / `⚪ INDISPO`).

## 6. Déploiement Continu Systématique en Production
- Tout travail validé doit être testé (`dotnet test` + `npm test`), buildé (`npm run build`), synchronisé dans `Wazap.API/wwwroot/app`, committé et poussé sur `main` (`git push origin main`).

## 7. Langue des Vidéos & Marketing
- Dialogues et voix-off des productions vidéo exclusivement en français soigné et naturel (aucun nouchi informel).
- Textes de description (captions) systématiques avec Hook, Bénéfices, CTA WhatsApp et hashtags ciblés Abidjan.

## 8. Isolation Étanche WhatsApp Business vs WhatsApp Personnel
- **Package unique écouté par l'Android Gateway :** `com.whatsapp.w4b` (WhatsApp Business). Interdiction formelle et définitive d'écouter ou d'interagir avec `com.whatsapp` (WhatsApp standard).
- **Protection absolue du numéro personnel du propriétaire / admin :** Le numéro `+225 07 08 32 33 66` est strictement banni de tout traitement automatisé et de toute réponse bot (`shouldReply = false`, `ignored_protected_number`).
- **Zéro auto-réponse intempestive :** Les messages ordinaires sans mot-clé WAZAP ne déclenchent aucune réponse automatique afin de garantir une étanchéité totale avec les conversations privées.
