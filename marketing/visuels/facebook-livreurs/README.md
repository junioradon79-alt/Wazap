# Série Facebook — Recrutement livreurs WAZAP

**1 visuel / jour × 7 jours**, format teasing, carré **1080×1080** (export **2160×2160**).
Diffusion : **page WAZAP** + **groupes spécialisés** (livraison, emploi Abidjan, motards…).

Les 4 éléments les plus visibles sur chaque visuel : **QR code**, **smartphone**, **moto**, **numéro WhatsApp**.

---

## Les 7 visuels

| Jour | Fichier | Angle | Message clé |
|---|---|---|---|
| J1 | `generated/fb_livreurs_j01.png` | **Teasing** | « Quelque chose arrive pour les livreurs » (aucune révélation) |
| J2 | `generated/fb_livreurs_j02.png` | **Révélation** 🎁 | « Un smartphone. Offert. Vraiment. » (systématique) |
| J3 | `generated/fb_livreurs_j03.png` | **Bonus** 🏍️ | « Et une MOTO à gagner » (tirage trimestriel) |
| J4 | `generated/fb_livreurs_j04.png` | **Comment gagner** ✅ | Les 3 conditions (certifié + 250 livraisons + 5 filleuls) |
| J5 | `generated/fb_livreurs_j05.png` | **La moto** 🎟️ | « 1000 livraisons = ta moto ? » (plus tu livres, plus de tickets) |
| J6 | `generated/fb_livreurs_j06.png` | **Preuve sociale** 📣 | « Les inscriptions pleuvent déjà » |
| J7 | `generated/fb_livreurs_j07.png` | **Dernier rappel** 🔥 | « Il ne manque plus que TOI » (QR + numéro) |

---

## Légendes prêtes à copier (Facebook)

**J1**
> 🛵 Abidjan, on a quelque chose pour toi.
> Une offre que personne n'a encore osée pour les livreurs 👀
> 1 indice : ça se passe dans ton quartier.
> Suis la page. Réponse demain. 📲 https://junioradon79gm-001-site1.jtempurl.com/devenir-livreur
> #WAZAP #Livreur #Abidjan #EmploiAbidjan #Motard

**J2**
> 🎁 Un smartphone. OFFERT. Vraiment.
> Pas de tirage, pas de hasard : **3 conditions** et le téléphone est à toi.
> Et ce n'est pas tout… 🏍️ (reste connecté)
> Inscris-toi 👉 https://junioradon79gm-001-site1.jtempurl.com/devenir-livreur
> #WAZAP #Livreur #Abidjan #Smartphone #Recrutement

**J3**
> 🏍️ Et une MOTO à gagner.
> Chaque trimestre, WAZAP tire au sort une moto neuve parmi les livreurs 🎟️
> Plus tu livres, plus tu as de tickets.
> Livreur WAZAP = ticket pour la moto 👉 +225 05 75 80 38 01 (« je veux livrer »)
> #WAZAP #Moto #Abidjan #Livreur #Tirage

**J4**
> ✅ 3 conditions. C'est tout.
> 1️⃣ Devenir livreur **certifié**
> 2️⃣ Réaliser **250 livraisons** WAZAP
> 3️⃣ Parrainer **5 livreurs** actifs
> → Ton smartphone, **automatiquement** 📱
> (Et ta place dans le tirage moto 🏍️)
> Scan le QR ou écris « je veux livrer » au +225 05 75 80 38 01
> #WAZAP #Livreur #Abidjan #Recrutement

**J5**
> 🎟️ Tirage trimestriel WAZAP : **1000 livraisons = 1 ticket garanti** pour la moto.
> Les meilleurs livreurs partent avec une longueur d'avance 🏍️🔥
> Plus tu livres, plus tu gagnes.
> Inscris-toi 👉 https://junioradon79gm-001-site1.jtempurl.com/devenir-livreur
> #WAZAP #Moto #Livreur #Abidjan

**J6**
> 📣 Les inscriptions pleuvent déjà.
> Des livreurs de tout Abidjan rejoignent WAZAP cette semaine.
> Le smartphone 🎁, la moto 🏍️ — et ton quartier qui t'attend 🛵
> Inscription en 2 min, gratuit, 100 % WhatsApp.
> #WAZAP #Livreur #Abidjan #Recrutement

**J7**
> 🔥 Il ne manque plus que TOI.
> 1. Scanne le QR code 📲
> 2. Écris « je veux livrer » sur WhatsApp
> 3. Commence à gagner dès aujourd'hui
> 🎁 Smartphone · 🏍️ Moto au trimestre · 🛵 Payé par course
> +225 05 75 80 38 01
> #WAZAP #Livreur #Abidjan #DernierRappel

---

## Régénérer les visuels

```powershell
cd marketing\visuels\facebook-livreurs
.\gen-visuels.ps1                 # les 7 jours (PNG 2160x2160)
.\gen-visuels.ps1 -Debut 1 -Fin 3 # seulement J1..J3
.\gen-visuels.ps1 -Scale 1        # PNG strictement 1080x1080
```

Le gabarit `post.html` se prévisualise dans un navigateur :
`post.html?j=3` (jour) · paramètres URL `t` (titre), `s` (sous-titre), `b` (badge),
`p` (pastilles séparées par `,`), `f` (focus : `phone` | `moto` | `both`), `c` (CTA).

---

## Photos réelles (smartphone + moto)

Les visuels utilisent désormais de **vraies photos** :
- `assets/phone.jpg` → **`assets/phone.png`** (smartphone premium + logo WAZAP, fond détouré transparent)
- `assets/moto.jpg`  → **`assets/moto.png`**  (scooter **Apsonic**, fond détouré transparent)

Le détourage est fait par **`cutout.cs`** (app mono-fichier .NET 10 + **SkiaSharp**) : les
sources contiennent un **damier de transparence aplati** (le fond « transparent » a été
incrusté dans le JPG). L'outil retire ce damier (détection par **motif périodique** +
**remplissage depuis les bords**) et produit un **PNG à canal alpha**.

```powershell
cd marketing\visuels\facebook-livreurs
dotnet run cutout.cs -- assets\phone.jpg assets\phone.png
dotnet run cutout.cs -- assets\moto.jpg  assets\moto.png
.\gen-visuels.ps1                 # puis régénérer les 7 visuels
```

> Les **SVG** (`phone.svg`, `moto.svg`) sont conservés en **secours** : dans `post.html`,
> une photo est utilisée si le PNG existe, sinon le SVG prend le relais.

---

## Règles de l'offre (rappel)

- **Smartphone** (entrée/milieu de gamme, type **Redmi 15C**) : **systématique** dès les 3 conditions atteintes.
- **Moto** (type **Apsonic**) : **tirage trimestriel**, **1000 livraisons** = 1 ticket garanti.
- Seuils paramétrables côté application : section `RiderProgram` de `appsettings.json`
  (`DeliveriesTarget`, `ReferralsTarget`, `MinFilleulDeliveries`, `RewardLabel`).
- **Anti-fraude** : pas de faux comptes, pas d'auto-parrainage ; un filleul n'est « validé » qu'après une activité minimale.
