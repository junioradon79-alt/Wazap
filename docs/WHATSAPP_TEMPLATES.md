# Guide & Spécifications des Modèles de Messages WhatsApp (Meta & YCloud)

Ce document répertorie l'ensemble des **modèles de messages WhatsApp (*Message Templates*) officiels** configurés dans le backend WAZAP (`WhatsAppOptions.cs` et `WhatsAppOrchestrationService.cs`).

Ces modèles sont à soumettre sur **YCloud** (ou dans le **WhatsApp Manager** de Meta) dès que le numéro officiel `+225 07 87 11 95 20` est relié via l'Embedded Signup.

---

## Règles d'Or pour l'Approbation Meta (0 Rejet)
1. **Catégorie `UTILITY`** : À l'exception du code de sécurité qui relève de `AUTHENTICATION` (ou `UTILITY`), tous les modèles logistiques doivent être classés en **UTILITY**. Le taux d'approbation est supérieur à 99 % et le coût par message est le plus bas.
2. **Langue** : Français (`fr`).
3. **Variables** : Utiliser impérativement le format `{{1}}`, `{{2}}` sans sauter de numéro.
4. **Liens URL** : Toujours utiliser l'URL complète avec domaine (ex: `https://wazap-api.onrender.com/app/suivi/{{1}}` ou `https://wazap.ci/app/suivi/{{1}}`), jamais de raccourcisseurs d'URL externes (bit.ly, tinyurl...) qui sont systématiquement bloqués par Meta.

---

## Répertoire Complet des Modèles WAZAP

### 1. `order_confirm` (Notification Vendeur — Nouvelle commande)
* **Destinataire** : Commerçant / Marchand
* **Déclencheur** : Le client a passé une commande (via le bot client ou catalogue).
* **Catégorie** : `UTILITY`
* **Langue** : `Français (fr)`
* **Variables** :
  * `{{1}}` : Nom du client (ex: *Mme Koné*)
  * `{{2}}` : Description de la commande / articles (ex: *Robe fleurie taille M*)
  * `{{3}}` : Montant total en FCFA (ex: *15000*)
* **En-tête (Texte)** : `🛎️ Nouvelle commande`
* **Corps du message (Body)** :
```text
Bonjour, vous avez reçu une nouvelle commande de {{1}} : {{2}} pour un montant de {{3}} FCFA.

Répondez Confirmer pour lancer la recherche d'un coursier, ou Refuser.
```
* **Pied de page (Footer)** : `WAZAP · Livraisons Abidjan`
* **Boutons (Quick Reply)** :
  * Bouton 1 : `Confirmer`
  * Bouton 2 : `Refuser`

---

### 2. `order_received` (Confirmation Client — Prise en compte)
* **Destinataire** : Client acheteur
* **Déclencheur** : Le marchand a confirmé la commande.
* **Catégorie** : `UTILITY`
* **Langue** : `Français (fr)`
* **Variables** :
  * `{{1}}` : Code court de commande (ex: *A8B39F21*)
  * `{{2}}` : Nom de la boutique / vendeur (ex: *Boutique Chic & Glam*)
  * `{{3}}` : Délai estimé de préparation/livraison (ex: *15-30 minutes*)
* **En-tête (Texte)** : `✅ Commande prise en compte`
* **Corps du message (Body)** :
```text
Bonjour ! {{2}} a bien validé votre commande #{{1}}.
Délai estimé : {{3}}.
Votre livreur vous contactera dès le départ de la course.
```
* **Pied de page (Footer)** : `WAZAP · Livraisons Abidjan`

---

### 3. `rider_offer_v2` (Proposition de Course — Livreur)
* **Destinataire** : Livreurs certifiés à proximité
* **Déclencheur** : Recherche de livreur lancée par le système.
* **Catégorie** : `UTILITY`
* **Langue** : `Français (fr)`
* **Variables** :
  * `{{1}}` : Code unique de l'offre (ex: *OFFRE-492*)
* **En-tête (Texte)** : `🛵 Nouvelle course disponible`
* **Corps du message (Body)** :
```text
Une course est disponible à proximité de votre position.

Pour prendre la course, répondez simplement ACCEPTE {{1}}.
Premier livreur à répondre = course attribuée !
```
* **Pied de page (Footer)** : `WAZAP Livreur`
* **Bouton (Quick Reply)** :
  * Bouton 1 : `ACCEPTE {{1}}`

---

### 4. `rider_batch_offer` (Proposition Tournée Groupée — Livreur)
* **Destinataire** : Livreurs certifiés à proximité
* **Déclencheur** : Plusieurs commandes à collecter chez le même marchand.
* **Catégorie** : `UTILITY`
* **Langue** : `Français (fr)`
* **Variables** :
  * `{{1}}` : Nombre de commandes du lot (ex: *3*)
  * `{{2}}` : Code de l'offre groupée (ex: *LOT-781*)
* **En-tête (Texte)** : `📦 Tournée groupée disponible`
* **Corps du message (Body)** :
```text
Livraison groupée : {{1}} commandes à récupérer chez le même vendeur.

Répondez ACCEPTE {{2}} pour prendre la totalité de la tournée.
```
* **Pied de page (Footer)** : `WAZAP Livreur`

---

### 5. `rider_assigned_client` (Notification Client — Livreur en route)
* **Destinataire** : Client acheteur
* **Déclencheur** : Un coursier a accepté la livraison.
* **Catégorie** : `UTILITY`
* **Langue** : `Français (fr)`
* **Variables** :
  * `{{1}}` : Code court de commande (ex: *A8B39F21*)
  * `{{2}}` : Prénom/Nom du coursier (ex: *Mamadou K.*)
* **En-tête (Texte)** : `🛵 Votre livreur est en route`
* **Corps du message (Body)** :
```text
Bonjour ! Votre livreur {{2}} a accepté la livraison de votre commande #{{1}}.

Il récupère votre colis et arrive vers vous. Gardez votre téléphone à portée de main !
```
* **Pied de page (Footer)** : `WAZAP · Livraisons Abidjan`

---

### 6. `rider_assigned_vendor` (Notification Vendeur — Coursier en approche)
* **Destinataire** : Commerçant / Marchand
* **Déclencheur** : Un coursier a accepté la livraison.
* **Catégorie** : `UTILITY`
* **Langue** : `Français (fr)`
* **Variables** :
  * `{{1}}` : Nom du livreur (ex: *Mamadou K.*)
  * `{{2}}` : Nom du client (ex: *Mme Koné*)
  * `{{3}}` : Code court de commande (ex: *A8B39F21*)
* **En-tête (Texte)** : `🏍️ Coursier en approche`
* **Corps du message (Body)** :
```text
Le livreur {{1}} a accepté la commande #{{3}} pour votre client {{2}}.

Il arrive à votre point de collecte. Merci de préparer le colis scellé.
```
* **Pied de page (Footer)** : `WAZAP Marchand`

---

### 7. `delivery_code` (Garantie Colis Sûr — Code Secret 4 chiffres)
* **Destinataire** : Client acheteur
* **Déclencheur** : Attribué dès la création de la course.
* **Catégorie** : `AUTHENTICATION` (ou `UTILITY`)
* **Langue** : `Français (fr)`
* **Variables** :
  * `{{1}}` : Code secret à 4 chiffres (ex: *7492*)
* **En-tête (Texte)** : `🔒 Code de remise Colis Sûr`
* **Corps du message (Body)** :
```text
Votre code secret de livraison Colis Sûr est : {{1}}.

⚠️ Ne donnez ce code au livreur qu'au moment où vous tenez votre colis en mains propres. C'est votre garantie de réception !
```
* **Pied de page (Footer)** : `WAZAP · Garantie Colis Sûr`

---

### 8. `client_tracking_link` (Lien PWA de Suivi en Direct)
* **Destinataire** : Client acheteur
* **Déclencheur** : Dès confirmation par le vendeur pour localisation précise.
* **Catégorie** : `UTILITY`
* **Langue** : `Français (fr)`
* **Variables** :
  * `{{1}}` : Nom de la boutique (ex: *Boutique Chic & Glam*)
  * `{{2}}` : Code commande court (ex: *A8B39F21*)
  * `{{3}}` : URL de suivi en direct (ex: *https://wazap-api.onrender.com/app/suivi/A8B39F21*)
* **En-tête (Texte)** : `📍 Suivi de votre livraison`
* **Corps du message (Body)** :
```text
{{1}} a préparé votre commande #{{2}} !

Confirmez votre adresse et suivez votre coursier en direct sur la carte :
{{3}}
```
* **Pied de page (Footer)** : `WAZAP · Suivi en direct`
* **Bouton (URL)** :
  * Libellé : `📍 Suivre mon colis`
  * URL dynamique : `{{3}}`

---

### 9. `credit_purchase` (Notification Marchand — Achat de pack)
* **Destinataire** : Marchand
* **Déclencheur** : Paiement Mobile Money validé pour l'achat d'un pack de courses.
* **Catégorie** : `UTILITY`
* **Langue** : `Français (fr)`
* **Variables** :
  * `{{1}}` : Nom du pack (ex: *Pack Moyen - 80 courses*)
  * `{{2}}` : Nouveau solde de courses (ex: *80*)
* **En-tête (Texte)** : `🎉 Pack de crédits activé`
* **Corps du message (Body)** :
```text
Félicitations ! Vous avez acheté le pack {{1}}.

Vous disposez maintenant de {{2}} courses de livraison disponibles sur WAZAP.
Bons envois à Abidjan !
```
* **Pied de page (Footer)** : `WAZAP Marchand`

---

### 10. `low_credit` (Alerte Marchand — Crédits faibles)
* **Destinataire** : Marchand
* **Déclencheur** : Solde $\le 5$ courses restantes.
* **Catégorie** : `UTILITY`
* **Langue** : `Français (fr)`
* **Variables** :
  * `{{1}}` : Nombre de courses restantes (ex: *3*)
* **En-tête (Texte)** : `⚠️ Solde de crédits bientôt épuisé`
* **Corps du message (Body)** :
```text
Attention : il ne vous reste que {{1}} courses de livraison sur votre compte WAZAP.

Rechargez dès maintenant pour continuer à livrer vos clients sans aucune interruption.
```
* **Pied de page (Footer)** : `WAZAP Marchand`

---

### 11. `no_credit` (Alerte Marchand — Compte à zéro)
* **Destinataire** : Marchand
* **Déclencheur** : Solde $= 0$ course restante.
* **Catégorie** : `UTILITY`
* **Langue** : `Français (fr)`
* **Variables** : Aucune
* **En-tête (Texte)** : `🚫 Plus de crédits disponibles`
* **Corps du message (Body)** :
```text
Vous n'avez plus de crédits de livraison sur votre compte WAZAP.

Achetez un nouveau pack depuis votre espace marchand pour continuer à expédier vos colis.
```
* **Pied de page (Footer)** : `WAZAP Marchand`

---

### 12. Séquence d'Onboarding Vendeur (`vendor_onboarding_day1`, `day3`, `day7`)
* **Catégorie** : `UTILITY`
* **`vendor_onboarding_day1`** :
  * `{{1}}` : Nom du commerçant.
  * *« Bienvenue sur WAZAP {{1}} ! Vos 15 courses offertes sont prêtes. Pour envoyer votre premier colis, écrivez simplement LIVRAISON ici. »*
* **`vendor_onboarding_day3`** :
  * `{{1}}` : Nom, `{{2}}` : Nombre de livraisons effectuées.
  * *« Bonjour {{1}}, déjà {{2}} colis livrés avec WAZAP ! Besoin d'aide pour vos prochaines commandes ? Notre support est à votre écoute. »*
* **`vendor_onboarding_day7`** :
  * `{{1}}` : Nom, `{{2}}` : Code de parrainage, `{{3}}` : Crédits restants.
  * *« Bonjour {{1}} ! Partagez votre code parrainage {{2}} à d'autres boutiques et recevez 10 courses gratuites à chaque nouvelle inscription ! »*

---

## Procédure de Soumission sur YCloud

Dès la finalisation de l'Embedded Signup dans la semaine :
1. Connectez-vous sur votre tableau de bord **[YCloud.com](https://ycloud.com)**.
2. Allez dans le menu **WhatsApp** $\rightarrow$ **Templates** $\rightarrow$ **Create Template**.
3. Sélectionnez le WABA **WAZAP CI**, entrez le nom exact (ex: `order_confirm`), choisissez la catégorie `UTILITY` et la langue `French`.
4. Copiez-collez l'en-tête, le corps et le footer indiqués ci-dessus.
5. Cliquez sur **Submit** : Meta valide généralement les templates de catégorie UTILITY en **moins de 5 à 15 minutes**.
