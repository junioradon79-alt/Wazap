import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const rootDir = path.resolve(__dirname, '..', '..');

const items = [
  // COLLECTION 1 : FORMULES COMMERÇANTS & E-COMMERCE
  {
    collection: "🏪 Formules Commerçants & E-commerce",
    collectionId: "commercants",
    num: 1,
    image: "catalogue_visuels/01_pack_bienvenue_15courses.png",
    name: "🎁 Pack Bienvenue (15 Livraisons Offertes)",
    price: "0 FCFA",
    priceNum: 0,
    sku: "WA-PACK-TRIAL15",
    link: "https://wa.me/2250544051972?text=COLIS",
    linkText: "COLIS",
    description: `Testez WAZAP sans aucun engagement ! Vos 15 premières livraisons sont 100% offertes (zéro commission de mise en relation).

✅ Livreur assigné en 3 minutes chrono à Abidjan
✅ Règlement par QR Code Universel (Wave, Orange, MTN, Moov)
✅ Virement instantané sur votre propre compte à la livraison
✅ Zéro manipulation d'espèces sur vos produits = Zéro vol

Touchez le lien ci-dessous pour activer vos 15 courses offertes !`
  },
  {
    collection: "🏪 Formules Commerçants & E-commerce",
    collectionId: "commercants",
    num: 2,
    image: "catalogue_visuels/02_pack_petit_35courses.png",
    name: "📦 Pack Petit — 35 Livraisons",
    price: "5 000 FCFA",
    priceNum: 5000,
    sku: "WA-PACK-PETIT35",
    link: "https://wa.me/2250544051972?text=RECHARGE%20PETIT",
    linkText: "RECHARGE PETIT",
    description: `La formule idéale pour les boutiques en ligne et commerces de quartier (~142 F par mise en relation).

✅ 35 crédits de livraison express dans tout Abidjan
✅ Suivi en direct du livreur par GPS
✅ Sécurité Colis Sûr : votre recette encaissée en direct
✅ Valable sans date limite d'expiration

Touchez le lien ci-dessous pour commander votre recharge.`
  },
  {
    collection: "🏪 Formules Commerçants & E-commerce",
    collectionId: "commercants",
    num: 3,
    image: "catalogue_visuels/03_pack_moyen_80courses.png",
    name: "🔥 Pack Moyen — 80 Livraisons (Recommandé)",
    price: "10 000 FCFA",
    priceNum: 10000,
    sku: "WA-PACK-MOYEN80",
    link: "https://wa.me/2250544051972?text=RECHARGE%20MOYEN",
    linkText: "RECHARGE MOYEN",
    description: `Notre formule la plus populaire pour les vendeurs actifs (125 F seulement par mise en relation !).

✅ 80 crédits de livraison express Grand Abidjan
✅ Assignation ultra-rapide des coursiers certifiés
✅ Encaissement direct via QR Code Universel multi-opérateurs
✅ Gestion 100% sur WhatsApp, zéro saisie fastidieuse

Touchez le lien ci-dessous pour recharger votre compte.`
  },
  {
    collection: "🏪 Formules Commerçants & E-commerce",
    collectionId: "commercants",
    num: 4,
    image: "catalogue_visuels/04_pack_grand_220courses.png",
    name: "🚀 Pack Grand — 220 Livraisons",
    price: "25 000 FCFA",
    priceNum: 25000,
    sku: "WA-PACK-GRAND220",
    link: "https://wa.me/2250544051972?text=RECHARGE%20GRAND",
    linkText: "RECHARGE GRAND",
    description: `Le tarif préférentiel pour les boutiques à fort débit (~113 F par mise en relation).

✅ 220 livraisons sécurisées dans tout Abidjan
✅ Support prioritaire dédié 7j/7
✅ Mini-boutique WhatsApp activée pour vos articles
✅ Fini les livreurs qui fuient avec votre recette

Touchez le lien ci-dessous pour recharger votre compte.`
  },
  {
    collection: "🏪 Formules Commerçants & E-commerce",
    collectionId: "commercants",
    num: 5,
    image: "catalogue_visuels/05_pack_pro_1000courses.png",
    name: "👑 Pack Pro — 1 000 Livraisons",
    price: "100 000 FCFA",
    priceNum: 100000,
    sku: "WA-PACK-PRO1000",
    link: "https://wa.me/2250544051972?text=RECHARGE%20PRO",
    linkText: "RECHARGE PRO",
    description: `La solution logistique premium pour grossistes, marques, distributeurs et franchises (100 F par course).

✅ 1 000 crédits de livraison grand volume
✅ Accompagnement logistique dédié et rapports mensuels
✅ Flotte de livreurs prioritaires sur vos zones d'expédition
✅ Sécurité totale 0 cash sur la marchandise

Touchez le lien ci-dessous pour commander votre pack Pro.`
  },

  // COLLECTION 2 : ESPACE LIVREURS & RECRUTEMENT
  {
    collection: "🛵 Espace Livreurs & Recrutement",
    collectionId: "livreurs",
    num: 6,
    image: "catalogue_visuels/06_devenir_livreur_certifie.png",
    name: "🛵 Devenir Livreur Certifié (0% Commission)",
    price: "0 FCFA",
    priceNum: 0,
    sku: "WA-RIDER-JOIN",
    link: "https://wa.me/2250544051972?text=DISPO",
    linkText: "DISPO",
    description: `Rejoignez la flotte des livreurs indépendants d'Abidjan et gardez 100% de vos gains !

✅ 0% de commission prélevée sur vos courses
✅ 1 000 à 2 000 FCFA net direct dans votre poche par course
✅ Zéro transport d'argent liquide sur la marchandise = sécurité totale
✅ Inscription en 2 minutes par simple photo de CNI / Permis
✅ Éligible d'office au défi Smartphone Redmi 15C neuf !

Touchez le lien ci-dessous pour vous inscrire immédiatement.`
  },
  {
    collection: "🛵 Espace Livreurs & Recrutement",
    collectionId: "livreurs",
    num: 7,
    image: "catalogue_visuels/07_defi_smartphone_redmi15c.png",
    name: "📱 Défi Smartphone Redmi 15C Neuf",
    price: "0 FCFA",
    priceNum: 0,
    sku: "WA-RIDER-CHALLENGE",
    link: "https://wa.me/2250544051972?text=PROGRAMME",
    linkText: "PROGRAMME",
    description: `Un smartphone Xiaomi Redmi 15C neuf offert à tous les livreurs engagés du réseau WAZAP !

Comment le remporter ?
1️⃣ Être certifié Colis Sûr (pièce d'identité validée)
2️⃣ Réaliser 250 livraisons réussies
3️⃣ Parrainer 5 collègues livreurs actifs (au moins 25 courses chacun)

Suivi de progression en direct sur WhatsApp avec jauges visuelles ! Touchez le lien pour voir votre progression.`
  },

  // COLLECTION 3 : SÉCURITÉ, TARIFS & COLIS SÛR
  {
    collection: "🛡️ Sécurité, Tarifs & Colis Sûr",
    collectionId: "securite",
    num: 8,
    image: "catalogue_visuels/08_grille_tarifaire_abidjan.png",
    name: "📋 Grille Tarifaire Abidjan (0% Commission)",
    price: "1 000 FCFA",
    priceNum: 1000,
    sku: "WA-TARIFS-ABJ",
    link: "https://wa.me/2250544051972?text=TARIFS",
    linkText: "TARIFS",
    description: `Transparence absolue sur les prix des courses dans le Grand Abidjan :

• Même commune (intra-commune) : 1 000 FCFA net
• Commune voisine : 1 500 FCFA net
• Traversée de pont / Longue distance : 2 000 FCFA net

💡 100% du montant revient au livreur. WAZAP ne prend aucune commission sur le travail des coursiers.

Touchez le lien ci-dessous pour consulter les détails ou demander une course.`
  },
  {
    collection: "🛡️ Sécurité, Tarifs & Colis Sûr",
    collectionId: "securite",
    num: 9,
    image: "catalogue_visuels/09_qr_code_universel_colis_sur.png",
    name: "🛡️ Colis Sûr : Le QR Code Universel",
    price: "0 FCFA",
    priceNum: 0,
    sku: "WA-COLIS-SUR",
    link: "https://wa.me/2250544051972?text=COLIS",
    linkText: "COLIS",
    description: `La fin définitive des vols de caisse et des livreurs qui disparaissent avec votre argent !

• Règlement exclusivement digital par Scan du QR Code Universel WAZAP
• Compatible Wave, Orange Money, MTN MoMo, Moov Money et Cartes Bancaires
• Votre recette arrive instantanément sur votre propre compte Mobile Money
• Le livreur est payé de sa course sans délai
• Zéro manipulation d'espèces sur vos articles

Touchez le lien ci-dessous pour expédier votre premier colis sécurisé.`
  }
];

function generateHtml(baseImagePath = './catalogue_visuels/') {
  const jsonCatalog = JSON.stringify(items);

  return `<!DOCTYPE html>
<html lang="fr">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Catalogue WhatsApp Business WAZAP — 3 Collections Prêtes à Charger</title>
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@400;500;600;700;800;900&display=swap" rel="stylesheet">
  <style>
    :root {
      --bg: #090D0E;
      --card-bg: #111718;
      --card-border: #1E2829;
      --primary: #00A86B;
      --primary-hover: #02C67E;
      --accent: #25D366;
      --gold: #FFB800;
      --text: #F0F4F2;
      --text-muted: #8E9E96;
      --font: 'Plus Jakarta Sans', system-ui, sans-serif;
    }

    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      background: var(--bg);
      color: var(--text);
      font-family: var(--font);
      line-height: 1.5;
      padding-bottom: 80px;
    }

    .container {
      max-width: 1200px;
      margin: 0 auto;
      padding: 0 20px;
    }

    /* HEADER */
    header {
      background: linear-gradient(180deg, #0E1617 0%, #090D0E 100%);
      border-bottom: 1px solid var(--card-border);
      padding: 40px 0 28px;
      position: sticky;
      top: 0;
      z-index: 100;
      backdrop-filter: blur(12px);
    }
    .header-content {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }
    @media (min-width: 768px) {
      .header-content {
        flex-direction: row;
        align-items: center;
        justify-content: space-between;
      }
    }
    .brand-wrap {
      display: flex;
      align-items: center;
      gap: 16px;
    }
    .logo-badge {
      width: 56px;
      height: 56px;
      border-radius: 50%;
      background: radial-gradient(circle, #00A86B 0%, #075E54 100%);
      border: 2px solid #FFFFFF;
      box-shadow: 0 4px 16px rgba(0, 168, 107, 0.4);
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 900;
      font-size: 24px;
      color: #FFF;
    }
    .title-box h1 {
      font-size: 24px;
      font-weight: 800;
      letter-spacing: -0.5px;
      display: flex;
      align-items: center;
      gap: 10px;
    }
    .title-box p {
      color: var(--text-muted);
      font-size: 14px;
      margin-top: 2px;
    }
    .official-badge {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      background: rgba(37, 211, 102, 0.12);
      border: 1px solid rgba(37, 211, 102, 0.3);
      color: #25D366;
      padding: 6px 14px;
      border-radius: 20px;
      font-weight: 700;
      font-size: 13px;
    }

    /* FILTERS / TABS */
    .tabs-bar {
      display: flex;
      flex-wrap: wrap;
      gap: 10px;
      margin: 28px 0;
    }
    .tab-btn {
      background: #151D1E;
      border: 1px solid var(--card-border);
      color: var(--text-muted);
      padding: 10px 18px;
      border-radius: 30px;
      font-size: 14px;
      font-weight: 700;
      cursor: pointer;
      transition: all 0.2s;
    }
    .tab-btn:hover {
      border-color: var(--primary);
      color: var(--text);
    }
    .tab-btn.active {
      background: var(--primary);
      border-color: var(--primary);
      color: #FFF;
      box-shadow: 0 4px 14px rgba(0, 168, 107, 0.35);
    }

    /* HOW TO USE GUIDE CARD */
    .guide-banner {
      background: linear-gradient(135deg, #10231D 0%, #0D1917 100%);
      border: 1px solid #1C4234;
      border-radius: 16px;
      padding: 24px;
      margin-bottom: 32px;
      display: grid;
      grid-template-columns: 1fr;
      gap: 20px;
    }
    @media (min-width: 900px) {
      .guide-banner {
        grid-template-columns: 2fr 1fr;
        align-items: center;
      }
    }
    .guide-banner h2 {
      font-size: 18px;
      font-weight: 800;
      color: #25D366;
      margin-bottom: 8px;
    }
    .guide-steps {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 12px;
      margin-top: 14px;
    }
    .step-item {
      background: rgba(0, 0, 0, 0.25);
      border-radius: 10px;
      padding: 12px;
      font-size: 13px;
    }
    .step-item strong {
      color: #FFF;
      display: block;
      margin-bottom: 2px;
    }

    /* CARDS GRID */
    .cards-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(350px, 1fr));
      gap: 28px;
    }

    .card {
      background: var(--card-bg);
      border: 1px solid var(--card-border);
      border-radius: 20px;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      box-shadow: 0 10px 30px rgba(0, 0, 0, 0.3);
      transition: transform 0.2s, border-color 0.2s;
    }
    .card:hover {
      transform: translateY(-4px);
      border-color: #263836;
    }

    .card-img-wrap {
      position: relative;
      width: 100%;
      aspect-ratio: 1 / 1;
      background: #000;
      overflow: hidden;
    }
    .card-img {
      width: 100%;
      height: 100%;
      object-fit: cover;
      display: block;
      transition: transform 0.3s;
    }
    .card:hover .card-img {
      transform: scale(1.02);
    }
    .card-collection-badge {
      position: absolute;
      top: 14px;
      left: 14px;
      background: rgba(0, 0, 0, 0.75);
      backdrop-filter: blur(8px);
      border: 1px solid rgba(255, 255, 255, 0.15);
      color: #FFF;
      padding: 6px 12px;
      border-radius: 20px;
      font-size: 12px;
      font-weight: 700;
    }
    .card-price-pill {
      position: absolute;
      bottom: 14px;
      right: 14px;
      background: var(--primary);
      color: #FFF;
      padding: 8px 16px;
      border-radius: 24px;
      font-size: 16px;
      font-weight: 900;
      box-shadow: 0 4px 16px rgba(0, 0, 0, 0.5);
    }

    .card-body {
      padding: 24px;
      display: flex;
      flex-direction: column;
      flex-grow: 1;
      gap: 16px;
    }

    .field-row {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
    .field-label {
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.8px;
      font-weight: 800;
      color: var(--text-muted);
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .field-val-box {
      background: #0B1011;
      border: 1px solid #1C2728;
      border-radius: 10px;
      padding: 10px 14px;
      font-size: 14px;
      color: #FFF;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 10px;
      word-break: break-all;
    }
    .field-val-box.desc-box {
      font-size: 13px;
      line-height: 1.5;
      color: #D1DDD8;
      white-space: pre-line;
      max-height: 180px;
      overflow-y: auto;
      word-break: normal;
    }
    .field-val-box.desc-box::-webkit-scrollbar {
      width: 4px;
    }
    .field-val-box.desc-box::-webkit-scrollbar-thumb {
      background: #253935;
      border-radius: 4px;
    }

    .btn-copy-mini {
      background: #182424;
      border: 1px solid #2B3D3B;
      color: #8E9E96;
      padding: 4px 10px;
      border-radius: 6px;
      font-size: 11px;
      font-weight: 700;
      cursor: pointer;
      transition: all 0.15s;
      white-space: nowrap;
    }
    .btn-copy-mini:hover {
      background: var(--primary);
      border-color: var(--primary);
      color: #FFF;
    }

    /* CARD FOOTER ACTIONS */
    .card-footer {
      padding-top: 8px;
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    .btn-action-primary {
      background: linear-gradient(135deg, #00A86B 0%, #075E54 100%);
      border: none;
      color: #FFF;
      padding: 12px;
      border-radius: 12px;
      font-size: 14px;
      font-weight: 800;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      transition: all 0.2s;
      text-decoration: none;
    }
    .btn-action-primary:hover {
      filter: brightness(1.15);
      box-shadow: 0 4px 16px rgba(0, 168, 107, 0.4);
    }
    .btn-action-secondary {
      background: #141E1F;
      border: 1px solid #243534;
      color: var(--text-muted);
      padding: 10px;
      border-radius: 10px;
      font-size: 13px;
      font-weight: 700;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      text-decoration: none;
      transition: all 0.15s;
    }
    .btn-action-secondary:hover {
      background: #1C2B2C;
      color: #FFF;
      border-color: #364F4D;
    }

    /* TOAST NOTIFICATION */
    #toast {
      position: fixed;
      bottom: 24px;
      right: 24px;
      background: #00A86B;
      color: #FFF;
      padding: 12px 24px;
      border-radius: 30px;
      font-weight: 800;
      font-size: 14px;
      box-shadow: 0 10px 30px rgba(0, 0, 0, 0.6);
      transform: translateY(100px);
      opacity: 0;
      transition: all 0.25s cubic-bezier(0.175, 0.885, 0.32, 1.275);
      z-index: 999;
      display: flex;
      align-items: center;
      gap: 8px;
    }
    #toast.show {
      transform: translateY(0);
      opacity: 1;
    }
  </style>
</head>
<body>

  <header>
    <div class="container">
      <div class="header-content">
        <div class="brand-wrap">
          <div class="logo-badge">W</div>
          <div class="title-box">
            <h1>Catalogue WhatsApp Business WAZAP</h1>
            <p>Guide maître & 3 collections officielles prêtes à copier-coller</p>
          </div>
        </div>
        <div class="official-badge">
          <span>🟢</span> WhatsApp Officiel : <strong>+225 05 44 05 19 72</strong>
        </div>
      </div>
    </div>
  </header>

  <div class="container">

    <!-- TABS -->
    <div class="tabs-bar">
      <button class="tab-btn active" onclick="filterCategory('all', this)">⚡ Tous les articles (9)</button>
      <button class="tab-btn" onclick="filterCategory('commercants', this)">🏪 Formules Commerçants (5)</button>
      <button class="tab-btn" onclick="filterCategory('livreurs', this)">🛵 Espace Livreurs (2)</button>
      <button class="tab-btn" onclick="filterCategory('securite', this)">🛡️ Sécurité & Tarifs (2)</button>
      <button class="tab-btn" onclick="copyFullManifest()">📋 Copier Tout le Manifeste</button>
    </div>

    <!-- GUIDE RAPIDE -->
    <div class="guide-banner">
      <div>
        <h2>📱 Comment ajouter ces articles sur le Smartphone (+225 05 44 05 19 72) :</h2>
        <div class="guide-steps">
          <div class="step-item">
            <strong>Étape 1</strong>
            Ouvrir WhatsApp Business ➔ ⋮ Outils professionnels ➔ Catalogue.
          </div>
          <div class="step-item">
            <strong>Étape 2</strong>
            Appuyer sur <em>Ajouter un nouvel article</em>.
          </div>
          <div class="step-item">
            <strong>Étape 3</strong>
            Télécharger l'image HD ci-dessous, puis copier/coller le Nom, Prix et Description.
          </div>
          <div class="step-item">
            <strong>Étape 4</strong>
            Appuyer sur <em>Plus d'options</em> pour coller le Lien 1-clic et le SKU. Sauvegarder !
          </div>
        </div>
      </div>
      <div style="text-align: right;">
        <button class="btn-action-primary" style="display:inline-flex;" onclick="window.print()">
          🖨️ Imprimer la Fiche Récap
        </button>
      </div>
    </div>

    <!-- ARTICLES GRID -->
    <div class="cards-grid" id="cardsContainer">
      ${items.map(item => `
        <div class="card" data-cat="${item.collectionId}">
          <div class="card-img-wrap">
            <img class="card-img" src="${baseImagePath}${item.image.replace('catalogue_visuels/', '')}" alt="${item.name}">
            <div class="card-collection-badge">${item.collection}</div>
            <div class="card-price-pill">${item.price}</div>
          </div>
          <div class="card-body">
            
            <div class="field-row">
              <div class="field-label">
                <span>Nom de l'article</span>
                <button class="btn-copy-mini" onclick="copyText('${escapeForJs(item.name)}', 'Nom copié')">Copier</button>
              </div>
              <div class="field-val-box">
                <strong>${item.name}</strong>
              </div>
            </div>

            <div class="field-row">
              <div class="field-label">
                <span>Prix (FCFA)</span>
                <button class="btn-copy-mini" onclick="copyText('${item.priceNum}', 'Prix copié')">Copier</button>
              </div>
              <div class="field-val-box">
                <span>${item.price}</span>
              </div>
            </div>

            <div class="field-row">
              <div class="field-label">
                <span>Description (100% Clé en main)</span>
                <button class="btn-copy-mini" onclick="copyText('${escapeForJs(item.description)}', 'Description copiée')">Copier</button>
              </div>
              <div class="field-val-box desc-box">${item.description}</div>
            </div>

            <div class="field-row">
              <div class="field-label">
                <span>Lien d'action 1-Tap (wa.me)</span>
                <button class="btn-copy-mini" onclick="copyText('${item.link}', 'Lien copié')">Copier</button>
              </div>
              <div class="field-val-box" style="font-size:12px; color:#25D366;">
                <span>${item.link}</span>
              </div>
            </div>

            <div class="field-row">
              <div class="field-label">
                <span>Code SKU / Référence</span>
                <button class="btn-copy-mini" onclick="copyText('${item.sku}', 'SKU copié')">Copier</button>
              </div>
              <div class="field-val-box" style="font-size:12px; font-family:monospace;">
                <span>${item.sku}</span>
              </div>
            </div>

            <div class="card-footer">
              <button class="btn-action-primary" onclick="copyFullArticle(${item.num})">
                ⚡ Copier toute la fiche (Prête à coller)
              </button>
              <a class="btn-action-secondary" href="${baseImagePath}${item.image.replace('catalogue_visuels/', '')}" download="${item.image.replace('catalogue_visuels/', '')}" target="_blank">
                📥 Télécharger l'image Carrée HD (2160×2160)
              </a>
            </div>

          </div>
        </div>
      `).join('')}
    </div>

  </div>

  <div id="toast">✅ Copié dans le presse-papiers !</div>

  <script>
    const CATALOG = ${jsonCatalog};

    function filterCategory(cat, btn) {
      document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');

      const cards = document.querySelectorAll('.card');
      cards.forEach(card => {
        if (cat === 'all' || card.getAttribute('data-cat') === cat) {
          card.style.display = 'flex';
        } else {
          card.style.display = 'none';
        }
      });
    }

    function showToast(msg) {
      const toast = document.getElementById('toast');
      toast.textContent = msg || '✅ Copié !';
      toast.classList.add('show');
      setTimeout(() => toast.classList.remove('show'), 2200);
    }

    function copyText(txt, label) {
      navigator.clipboard.writeText(txt).then(() => {
        showToast('✅ ' + (label || 'Texte copié !'));
      }).catch(() => {
        // Fallback
        const ta = document.createElement('textarea');
        ta.value = txt;
        document.body.appendChild(ta);
        ta.select();
        document.execCommand('copy');
        document.body.removeChild(ta);
        showToast('✅ ' + (label || 'Texte copié !'));
      });
    }

    function copyFullArticle(num) {
      const item = CATALOG.find(x => x.num === num);
      if (!item) return;

      const fullTxt = \`NOM : \${item.name}
PRIX : \${item.price}
LIEN : \${item.link}
SKU : \${item.sku}

DESCRIPTION :
\${item.description}\`;

      copyText(fullTxt, 'Fiche complète copiée !');
    }

    function copyFullManifest() {
      let txt = "CATALOGUE WHATSAPP BUSINESS WAZAP (+225 05 44 05 19 72)\\n\\n";
      CATALOG.forEach(item => {
        txt += \`===================================\\n\`;
        txt += \`[\${item.collection}]\\n\`;
        txt += \`ARTICLE : \${item.name}\\n\`;
        txt += \`PRIX : \${item.price}\\n\`;
        txt += \`LIEN : \${item.link}\\n\`;
        txt += \`SKU : \${item.sku}\\n\\n\`;
        txt += \`DESCRIPTION :\\n\${item.description}\\n\\n\`;
      });
      copyText(txt, 'Manifeste complet de 9 articles copié !');
    }
  </script>
</body>
</html>`;
}

function escapeForJs(str) {
  return str.replace(/\\/g, '\\\\').replace(/'/g, "\\'").replace(/"/g, '\\"').replace(/\n/g, '\\n').replace(/\r/g, '');
}

console.log("🚀 Génération des pages Catalogue WhatsApp Business...");

// 1. Marketing preview HTML
const marketingHtml = generateHtml('./catalogue_visuels/');
const marketingTarget = path.join(rootDir, 'marketing', 'whatsapp', 'catalogue_preview.html');
fs.writeFileSync(marketingTarget, marketingHtml, 'utf8');
console.log(`✅ Généré : ${marketingTarget}`);

// 2. Production wwwroot HTML
const wwwrootHtml = generateHtml('./catalogue_visuels/');
const wwwrootTarget = path.join(rootDir, 'src', 'Wazap.API', 'wwwroot', 'whatsapp', 'catalogue.html');
fs.writeFileSync(wwwrootTarget, wwwrootHtml, 'utf8');
console.log(`✅ Généré : ${wwwrootTarget}`);
