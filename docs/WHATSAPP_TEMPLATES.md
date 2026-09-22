# 📱 Spécifications des Modèles WhatsApp YCloud & Meta
**Version Révisée — Alignement 100% Code Backend & Nouveaux Dashboards (Vendeur & Livreur)**

Ce document est la référence canonique des modèles WhatsApp configurés dans le backend WAZAP (`WhatsAppOrchestrationService.cs` et `YCloudWhatsAppSender.cs`).

* **Fournisseur WhatsApp Officiel :** **`YCloud`** (Meta Tier-1 BSP).
* **Numéro WhatsApp Officiel :** **`+225 07 87 11 95 20`** (WABA WAZAP CI).
* **Conformité stricte :** Les variables déclarées ci-dessous correspondent au caractère près aux paramètres injectés par `WhatsAppOrchestrationService.cs`.

---

## 🛍️ PARTIE 1 : Modèles Opérationnels Espace Marchand & Clients (`UTILITY`)

### 1. `order_confirm` (Notification Marchand — Nouvelle commande reçue)
* **Déclencheur :** Un client passe commande via le catalogue vendeur ou le bot WhatsApp.
* **Destinataire :** Commerçant / Vendeur
* **Catégorie :** `UTILITY` | **Langue :** `fr`
* **Variables envoyées par le code (3 variables) :**
  * `{{1}}` : Nom du client (ex: *Mme Koné*)
  * `{{2}}` : Description des articles (ex: *Robe fleurie taille M*)
  * `{{3}}` : Montant total de la commande en FCFA (ex: *15000*)
* **En-tête (Texte) :** `🛎️ Nouvelle commande reçue`
* **Corps du message (Body) :**
```text
Bonjour ! Vous avez reçu une commande de {{1}} : {{2}} pour un montant de {{3}} FCFA.

Rendez-vous sur votre Espace Marchand pour la valider ou répondez Confirmer pour lancer la recherche d'un coursier.
```
* **Pied de page (Footer) :** `WAZAP · Espace Marchand`
* **Boutons (Quick Reply) :**
  - Bouton 1 : `Confirmer`
  - Bouton 2 : `Refuser`

---

### 2. `order_received` (Confirmation Client — Prise en charge boutique)
* **Déclencheur :** Le commerçant valide la commande.
* **Destinataire :** Client acheteur
* **Catégorie :** `UTILITY` | **Langue :** `fr`
* **Variables envoyées par le code (3 variables) :**
  * `{{1}}` : Code commande court (ex: *A8B39F21*)
  * `{{2}}` : Nom de la boutique (ex: *Chic & Glam*)
  * `{{3}}` : Délai estimé (ex: *15-30 minutes*)
* **En-tête (Texte) :** `✅ Commande confirmée`
* **Corps du message (Body) :**
```text
Bonjour ! {{2}} prépare votre commande #{{1}} (délai estimé : {{3}}).

Un coursier certifié WAZAP sera assigné dès que le colis sera scellé.
💳 Paiement à la livraison par QR Code Universel (Wave, Orange, MTN, Moov) ou espèces.
```
* **Pied de page (Footer) :** `WAZAP · Livraison Sécurisée`

---

### 3. `client_tracking_link` (Lien PWA Suivi GPS en Direct)
* **Déclencheur :** Commande confirmée par le vendeur.
* **Destinataire :** Client acheteur
* **Catégorie :** `UTILITY` | **Langue :** `fr`
* **Variables envoyées par le code (1 variable) :**
  * `{{1}}` : URL de suivi direct (ex: *https://wazap-api.onrender.com/app/suivi/A8B39F21*)
* **En-tête (Texte) :** `📍 Suivi de votre livraison`
* **Corps du message (Body) :**
```text
Votre commande est en préparation !

Suivez votre coursier en direct sur la carte et confirmez votre repère de livraison :
{{1}}
```
* **Pied de page (Footer) :** `WAZAP · Suivi GPS en direct`
* **Bouton (URL) :**
  - Libellé : `📍 Suivre mon colis`
  - URL : `{{1}}`

---

### 4. `delivery_code` (Code Secret Colis Sûr — Preuve de Remise)
* **Déclencheur :** Dès acceptation de la course par le livreur.
* **Destinataire :** Client acheteur
* **Catégorie :** `AUTHENTICATION` (ou `UTILITY`) | **Langue :** `fr`
* **Variables envoyées par le code (1 variable) :**
  * `{{1}}` : Code secret à 4 chiffres (ex: *7492*)
* **En-tête (Texte) :** `🔒 Code Secret Colis Sûr`
* **Corps du message (Body) :**
```text
Votre code secret de remise Colis Sûr est : {{1}}.

⚠️ Ne donnez ce code au coursier qu'une fois votre colis vérifié et en main propre. Vous pouvez également scanner le QR Code du livreur.
```
* **Pied de page (Footer) :** `WAZAP · Garantie Colis Sûr`

---

### 5. `order_delivered` (Notification Fin de Course)
* **Déclencheur :** Validation de livraison (scan QR ou code PIN).
* **Destinataire :** Client acheteur
* **Catégorie :** `UTILITY` | **Langue :** `fr`
* **Variables envoyées par le code (1 variable) :**
  * `{{1}}` : Code commande court (ex: *A8B39F21*)
* **En-tête (Texte) :** `🎉 Colis livré avec succès !`
* **Corps du message (Body) :**
```text
Votre commande #{{1}} a bien été livrée !

Merci d'avoir choisi WAZAP pour vos livraisons express à Abidjan. À très bientôt !
```
* **Pied de page (Footer) :** `WAZAP · Livraisons Abidjan`

---

## 🛵 PARTIE 2 : Modèles Dispatch Coursiers & Dashboard Livreur

### 6. `rider_offer_v2` (Proposition de Course — Notification Dispatch)
* **Déclencheur :** Colis prêt à collecter dans le rayon du coursier.
* **Destinataire :** Livreurs connectés (`DISPO`)
* **Catégorie :** `UTILITY` | **Langue :** `fr`
* **Variables envoyées par le code (1 variable) :**
  * `{{1}}` : Code unique de l'offre (ex: *C482*)
* **En-tête (Texte) :** `🛵 Nouvelle course disponible`
* **Corps du message (Body) :**
```text
Nouvelle course WAZAP disponible à proximité !
💰 Frais de livraison : 1 000 à 2 000 FCFA nets (0 commission retenue).

Pour remporter la course, répondez immédiatement ACCEPTE {{1}}. Premier à répondre = course attribuée !
```
* **Pied de page (Footer) :** `WAZAP Livreur · Réponse rapide requise`
* **Boutons (Quick Reply) :**
  - Bouton 1 : `ACCEPTE {{1}}`
  - Bouton 2 : `REFUSE {{1}}`

---

### 7. `rider_batch_offer` (Tournée Groupée Multi-Colis)
* **Déclencheur :** Plusieurs commandes chez le même marchand.
* **Destinataire :** Livreurs certifiés
* **Catégorie :** `UTILITY` | **Langue :** `fr`
* **Variables envoyées par le code (2 variables) :**
  * `{{1}}` : Nombre de colis à collecter (ex: *3*)
  * `{{2}}` : Code offre groupée (ex: *LOT-91*)
* **En-tête (Texte) :** `📦 Tournée groupée disponible`
* **Corps du message (Body) :**
```text
Tournée groupée : {{1}} commandes à récupérer chez le même vendeur !
Gains cumulés sans commission, 1 seul point de collecte.

Répondez ACCEPTE {{2}} pour remporter la totalité de la tournée.
```
* **Pied de page (Footer) :** `WAZAP Livreur`
* **Bouton (Quick Reply) :**
  - Bouton 1 : `ACCEPTE {{2}}`

---

### 8. `rider_assigned_client` (Notification Client — Coursier en approche)
* **Déclencheur :** Course acceptée par un livreur.
* **Destinataire :** Client acheteur
* **Catégorie :** `UTILITY` | **Langue :** `fr`
* **Variables envoyées par le code (2 variables) :**
  * `{{1}}` : Code commande court (ex: *A8B39F21*)
  * `{{2}}` : Nom du livreur (ex: *Mamadou K.*)
* **En-tête (Texte) :** `🛵 Livreur en route`
* **Corps du message (Body) :**
```text
Bonjour ! Votre livreur certifié {{2}} a accepté votre commande #{{1}}.

Il récupère votre colis scellé et arrive vers vous. Gardez votre téléphone disponible !
```
* **Pied de page (Footer) :** `WAZAP · Garantie Colis Sûr`

---

### 9. `rider_assigned_vendor` (Notification Marchand — Coursier en approche boutique)
* **Déclencheur :** Course acceptée par un livreur.
* **Destinataire :** Commerçant / Marchand
* **Catégorie :** `UTILITY` | **Langue :** `fr`
* **Variables envoyées par le code (3 variables) :**
  * `{{1}}` : Nom du livreur (ex: *Mamadou K.*)
  * `{{2}}` : Nom du client destinataire (ex: *Mme Koné*)
  * `{{3}}` : Code commande court (ex: *A8B39F21*)
* **En-tête (Texte) :** `🏍️ Coursier en approche`
* **Corps du message (Body) :**
```text
Le coursier {{1}} a accepté la commande #{{3}} pour votre client {{2}}.

Il arrive d'ici 3 minutes devant votre boutique. Préparez le colis avec son scellé Colis Sûr.
```
* **Pied de page (Footer) :** `WAZAP · Espace Marchand`

---

## 💳 PARTIE 3 : Portefeuille Marchand & Packs de Crédits

### 10. `credit_purchase` (Validation Achat Pack de Livraisons)
* **Variables (2 variables) :** `{{1}}` : Nom du pack, `{{2}}` : Nouveau solde de courses.
```text
Félicitations ! Votre pack {{1}} est actif.
Vous disposez maintenant de {{2}} courses de livraison sur WAZAP.
Gérez vos envois en direct depuis votre Espace Marchand !
```

### 11. `low_credit` (Alerte Solde $\le 5$ courses)
* **Variables (1 variable) :** `{{1}}` : Courses restantes.
```text
Attention : il ne vous reste que {{1}} courses de livraison sur votre compte WAZAP.
Rechargez dès maintenant depuis votre Espace Marchand pour continuer à livrer sans interruption.
```

### 12. `no_credit` (Alerte Compte Épuisé)
* **Variables :** Aucune.
```text
Vous n'avez plus de crédits de livraison sur votre compte WAZAP.
Rechargez en 1 clic via Mobile Money sur votre Espace Marchand pour continuer à expédier vos commandes.
```

---

## 🚀 PARTIE 4 : Séquence d'Onboarding Vendeur (Nurturing)

* **`vendor_onboarding_day1` (J+1)** :  
  *Variables : `{{1}}` (Nom boutique)*  
  *« Bienvenue sur WAZAP {{1}} ! Vos 15 livraisons offertes et votre Pack Digital Boutique (Mini-Boutique WhatsApp + Assurance Colis Sûr) sont activés. Pour expédier votre 1er colis, écrivez simplement LIVRAISON ici. »*
* **`vendor_onboarding_day3` (J+3)** :  
  *Variables : `{{1}}` (Nom), `{{2}}` (Nombre de courses effectuées)*  
  *« Bravo {{1}} ! Déjà {{2}} livraisons sécurisées avec WAZAP. Vos clientes peuvent régler à la porte par QR Code Universel sans stress de monnaie, avec l'Assurance Colis Sûr incluse ! »*
* **`vendor_onboarding_day7` (J+7)** :  
  *Variables : `{{1}}` (Nom), `{{2}}` (Code parrainage)*  
  *« Bonjour {{1}} ! Partagez votre code {{2}} à d'autres commerçants d'Abidjan : vous gagnez 5 courses offertes à chaque nouvelle boutique inscrite ! »*

---

## 🎯 PARTIE 5 : Prospection Sortante (Campagne 1 000 Commerçants & Livreurs)

* **`prospect_approach_v2` (Marketing Commerçants Google Maps)** :  
  *Variables : `{{1}}` (Boutique), `{{2}}` (Commercial), `{{3}}` (Lien vidéo démo)*  
  *« Bonjour {{1}} ! Marre des livreurs fantômes qui coupent leur téléphone à Abidjan ? WAZAP livre vos colis en 30 min avec coursiers certifiés. 🎁 15 Livraisons offertes + Pack Digital Boutique & Assurance Colis Sûr offerts ! Regardez la démo : {{3}} »*
* **`rider_recruit_v2` (Recrutement Livreurs)** :  
  *Variables : `{{1}}` (Prénom), `{{2}}` (Lien WhatsApp)*  
  *« Salut {{1}} ! Tu as une moto à Abidjan ? Gagne 1 000 à 2 000 FCFA nets par course (0 commission) et tente de remporter un Smartphone Xiaomi Redmi 15C neuf offert aux 50 premiers livreurs ! Rejoins-nous : {{2}} »*
