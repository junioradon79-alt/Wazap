import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const rootDir = path.resolve(__dirname, '..');

const catalog = JSON.parse(fs.readFileSync(path.join(__dirname, 'visuals_catalog.json'), 'utf8'));

// Modèles de référence
const models = [
  {
    id: "visuel_modele_01_fini_les_livreurs",
    theme: "securite",
    personnage: "Modèle Officiel 1 (Commerçante Pose A)",
    badge_title: "🌟 MODÈLE MAÎTRE 2026",
    feed_image_file: "visuel_modele_01_fini_les_livreurs_feed_square.png",
    story_image_file: "visuel_modele_01_fini_les_livreurs_story_vertical.png",
    hook: "🚨 WAZAP.CI - FINI LES LIVREURS QUI DISPARAISSENT AVEC VOTRE ARGENT !",
    body: "Chaque matin à Abidjan, c'est la même angoisse pour les commerçants : confier des colis de valeur à un coursier inconnu et prier pour qu'il ne disparaisse pas avec la recette !\n\nSur WAZAP, la règle est limpide et inviolable : ZÉRO CASH SUR LA MARCHANDISE.\nÀ la remise du colis, votre client scanne simplement le QR Code Universel WAZAP (Wave, Orange Money, MTN, Moov, Carte). L'argent arrive instantanément sur VOTRE compte avant même que le livreur ne reparte.\n\n🛡️ Livreurs vérifiés CNI par IA Google Cloud Vision\n📍 Géolocalisation live sur la carte\n🎁 15 premières livraisons offertes sans commission WAZAP !",
    cta: "👉 Prêt à expédier en toute sérénité ? Envoyez « COLIS » sur WhatsApp au 05 44 05 19 72 ou touchez https://wa.me/2250544051972?text=COLIS",
    hashtags: "#Wazap #LivraisonAbidjan #EcommerceCIV #VenteEnLigneCIV #Team225 #AbidjanBusiness #Cocody #Marcory #Yopougon #Plateau",
    comment: "💬 Avez-vous déjà été victime d'un livreur qui a disparu avec votre recette de marchandise à Abidjan ? Partagez votre expérience en commentaire."
  },
  {
    id: "visuel_modele_02_fini_les_livreurs",
    theme: "securite",
    personnage: "Modèle Officiel 2 (Commerçante Pose B)",
    badge_title: "🌟 MODÈLE MAÎTRE 2026",
    feed_image_file: "visuel_modele_02_fini_les_livreurs_feed_square.png",
    story_image_file: "visuel_modele_02_fini_les_livreurs_story_vertical.png",
    hook: "⚡ WAZAP.CI : LE LIVREUR NE TOUCHE PAS À UN SEUL FRANC DE VOTRE MARCHANDISE !",
    body: "Rush de midi à Abidjan : votre client n'a pas la monnaie sur 10 000 F ou exige de payer à la livraison ?\n\nPas besoin d'annuler la vente ni d'envoyer le coursier chercher la monnaie au carrefour.\nGrâce au QR Code Universel WAZAP, le client règle le montant exact de votre marchandise au centime près par Mobile Money (Wave, Orange Money, MTN).\n\nLe livreur ne touche à aucun billet de votre marchandise : il ne perçoit que ses frais de course.\nRésultat : zéro risque de vol, zéro faux billet, encaissement immédiat dans votre trésorerie !",
    cta: "📲 Pour tester gratuitement avec 15 courses offertes : envoyez « COLIS » sur WhatsApp au 05 44 05 19 72 ou touchez https://wa.me/2250544051972?text=COLIS",
    hashtags: "#Wazap #LivraisonAbidjan #EcommerceCIV #VenteEnLigneCIV #Team225 #AbidjanBusiness #Cocody #Marcory #Yopougon #Plateau",
    comment: "🔒 Quel est votre moyen de paiement Mobile Money préféré pour encaisser vos commandes : Wave, Orange Money ou MTN ? Dites-le nous en commentaire !"
  }
];

const allItems = [
  ...models.map(m => ({
    id: m.id,
    theme: m.theme,
    title: m.personnage,
    badge: m.badge_title,
    feedImg: `visuels/feed/${m.feed_image_file}`,
    storyImg: `visuels/story/${m.story_image_file}`,
    hook: m.hook,
    body: m.body,
    cta: m.cta,
    hashtags: m.hashtags,
    comment: m.comment,
    fullCaption: `${m.hook}\n\n${m.body}\n\n${m.cta}\n\n${m.hashtags}`
  })),
  ...catalog.map(c => ({
    id: c.id,
    theme: c.theme,
    title: c.personnage,
    badge: c.badge_title,
    feedImg: `visuels/feed/${c.id}_feed_square.png`,
    storyImg: `visuels/story/${c.id}_story_vertical.png`,
    hook: c.hook,
    body: c.body,
    cta: c.cta,
    hashtags: c.hashtags,
    comment: c.comment,
    fullCaption: `${c.hook}\n\n${c.body}\n\n${c.cta}\n\n${c.hashtags}`
  }))
];

const html = `<!DOCTYPE html>
<html lang="fr">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Galerie Officielle WAZAP 2026 — Campagne 60 Jours</title>
  <style>
    @import url('https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@500;600;700;800;900&family=Playfair+Display:ital,wght@0,700;0,900;1,700&display=swap');

    :root {
      --bg: #07170E;
      --card-bg: #0E2417;
      --card-border: rgba(0, 214, 108, 0.25);
      --green: #00D66C;
      --deep-green: #0C3823;
      --gold: #E5B350;
      --text: #F8F9FA;
      --text-muted: #9BB0A3;
      --linen: #F8F4ED;
    }

    * { box-sizing: border-box; margin: 0; padding: 0; }

    body {
      background: var(--bg);
      color: var(--text);
      font-family: 'Plus Jakarta Sans', sans-serif;
      padding: 40px 24px 80px;
      line-height: 1.5;
    }

    header {
      max-width: 1320px;
      margin: 0 auto 40px;
      text-align: center;
      position: relative;
    }

    .badge-header {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      background: rgba(0, 214, 108, 0.15);
      border: 1px solid var(--green);
      color: var(--green);
      padding: 8px 20px;
      border-radius: 9999px;
      font-size: 13px;
      font-weight: 800;
      text-transform: uppercase;
      letter-spacing: 1px;
      margin-bottom: 16px;
    }

    h1 {
      font-family: 'Playfair Display', serif;
      font-size: 46px;
      font-weight: 900;
      letter-spacing: -0.5px;
      margin-bottom: 12px;
      color: #FFF;
    }

    h1 span { color: var(--green); }

    p.sub {
      color: var(--text-muted);
      font-size: 18px;
      max-width: 860px;
      margin: 0 auto 28px;
    }

    /* FILTRES & FORMAT SWITCH */
    .controls {
      display: flex;
      flex-wrap: wrap;
      justify-content: center;
      gap: 14px;
      margin-bottom: 40px;
    }

    .btn-toggle {
      background: #0E2417;
      border: 1.5px solid var(--card-border);
      color: var(--text);
      padding: 10px 22px;
      border-radius: 12px;
      font-weight: 800;
      font-size: 14px;
      cursor: pointer;
      transition: all 0.2s ease;
      display: inline-flex;
      align-items: center;
      gap: 8px;
    }

    .btn-toggle:hover {
      border-color: var(--green);
      background: rgba(0, 214, 108, 0.1);
    }

    .btn-toggle.active {
      background: var(--green);
      color: #040c07;
      border-color: var(--green);
      box-shadow: 0 0 20px rgba(0, 214, 108, 0.4);
    }

    /* GRILLE DES VISUELS */
    .grid {
      max-width: 1320px;
      margin: 0 auto;
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(390px, 1fr));
      gap: 32px;
    }

    .card {
      background: var(--card-bg);
      border: 1.5px solid var(--card-border);
      border-radius: 20px;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      box-shadow: 0 15px 35px rgba(0, 0, 0, 0.5);
      transition: transform 0.25s ease, border-color 0.25s ease;
    }

    .card:hover {
      transform: translateY(-4px);
      border-color: var(--green);
    }

    .media-wrap {
      position: relative;
      background: #051009;
      width: 100%;
      display: flex;
      justify-content: center;
      align-items: center;
      overflow: hidden;
    }

    .media-wrap.format-feed {
      aspect-ratio: 1 / 1;
    }

    .media-wrap.format-story {
      aspect-ratio: 9 / 16;
      max-height: 590px;
    }

    .media-wrap img {
      width: 100%;
      height: 100%;
      object-fit: contain;
      display: block;
    }

    .card-content {
      padding: 24px;
      display: flex;
      flex-direction: column;
      flex: 1;
    }

    .card-header-badge {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      font-weight: 800;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      color: var(--gold);
      margin-bottom: 8px;
    }

    .card-title {
      font-family: 'Playfair Display', serif;
      font-size: 21px;
      font-weight: 800;
      color: #FFF;
      margin-bottom: 12px;
      line-height: 1.25;
    }

    .card-desc-box {
      background: rgba(4, 12, 7, 0.85);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 12px;
      padding: 14px;
      font-size: 13px;
      color: #CBD5E1;
      margin-bottom: 12px;
      max-height: 180px;
      overflow-y: auto;
      white-space: pre-wrap;
      font-family: inherit;
      line-height: 1.45;
    }

    .comment-box {
      background: rgba(0, 214, 108, 0.08);
      border: 1px dashed rgba(0, 214, 108, 0.35);
      border-radius: 10px;
      padding: 10px 14px;
      font-size: 12px;
      color: #A7F3D0;
      margin-bottom: 16px;
    }

    .comment-title {
      font-weight: 800;
      text-transform: uppercase;
      font-size: 11px;
      letter-spacing: 0.5px;
      color: var(--green);
      margin-bottom: 4px;
    }

    .actions-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 8px;
      margin-top: auto;
      margin-bottom: 10px;
    }

    .btn-action {
      background: rgba(0, 214, 108, 0.15);
      border: 1px solid var(--green);
      color: var(--green);
      font-weight: 800;
      font-size: 12px;
      padding: 10px;
      border-radius: 10px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
      transition: all 0.2s ease;
      text-align: center;
    }

    .btn-action:hover {
      background: var(--green);
      color: #040C07;
    }

    .btn-action.btn-comment {
      background: rgba(229, 179, 80, 0.15);
      border-color: var(--gold);
      color: var(--gold);
    }

    .btn-action.btn-comment:hover {
      background: var(--gold);
      color: #040C07;
    }

    .download-links {
      display: flex;
      gap: 8px;
    }

    .link-download {
      flex: 1;
      text-align: center;
      font-size: 11px;
      font-weight: 700;
      color: var(--text-muted);
      text-decoration: none;
      padding: 8px;
      border-radius: 8px;
      border: 1px solid rgba(255, 255, 255, 0.12);
      transition: all 0.2s;
    }

    .link-download:hover {
      color: #FFF;
      border-color: var(--green);
      background: rgba(255, 255, 255, 0.05);
    }

    .toast {
      position: fixed;
      bottom: 24px;
      right: 24px;
      background: #00D66C;
      color: #040C07;
      padding: 14px 24px;
      border-radius: 12px;
      font-weight: 800;
      font-size: 14px;
      box-shadow: 0 10px 25px rgba(0, 0, 0, 0.6);
      display: none;
      z-index: 9999;
    }
  </style>
</head>
<body>

  <header>
    <div class="badge-header">🚀 Suite Marketing Automatisée WAZAP 2026</div>
    <h1>Catalogue & Textes <span>WAZAP 60 Jours</span></h1>
    <p class="sub">
      Nouvelle charte visuelle haut de gamme Ivoire & Émeraude • 10 Personnages Réels • Formats Feed 1:1 (2160×2160) & Story 9:16 (2160×3840) • Descriptions Complètes Clé en Main & 1er Commentaire Épinglé
    </p>

    <div class="controls">
      <button class="btn-toggle active" onclick="setFormat('feed')">🖼️ Format Feed Carré (1:1)</button>
      <button class="btn-toggle" onclick="setFormat('story')">📱 Format Story Vertical (9:16)</button>
      <span style="border-right: 1px solid var(--card-border); margin: 0 4px;"></span>
      <button class="btn-toggle active" onclick="filterTheme('all')">Tous les Thèmes (${allItems.length})</button>
      <button class="btn-toggle" onclick="filterTheme('securite')">🛡️ Sécurité (5)</button>
      <button class="btn-toggle" onclick="filterTheme('rapidite')">⚡ Rapidité & Gain de Temps (3)</button>
      <button class="btn-toggle" onclick="filterTheme('valeur')">💎 Valeur Ajoutée (4)</button>
    </div>
  </header>

  <main class="grid" id="gridContainer">
    <!-- Généré par JS -->
  </main>

  <div id="toast" class="toast">✅ Texte copié avec succès dans le presse-papiers !</div>

  <script>
    const items = ${JSON.stringify(allItems, null, 2)};

    let currentFormat = 'feed';
    let currentTheme = 'all';

    function setFormat(fmt) {
      currentFormat = fmt;
      document.querySelectorAll('.controls .btn-toggle').forEach(btn => {
        if (btn.innerText.includes('Feed') && fmt === 'feed') btn.classList.add('active');
        else if (btn.innerText.includes('Story') && fmt === 'story') btn.classList.add('active');
        else if (btn.innerText.includes('Feed') || btn.innerText.includes('Story')) btn.classList.remove('active');
      });
      renderGrid();
    }

    function filterTheme(th) {
      currentTheme = th;
      document.querySelectorAll('.controls .btn-toggle').forEach(btn => {
        if (btn.innerText.includes('Tous') && th === 'all') btn.classList.add('active');
        else if (btn.innerText.includes('Sécurité') && th === 'securite') btn.classList.add('active');
        else if (btn.innerText.includes('Rapidité') && th === 'rapidite') btn.classList.add('active');
        else if (btn.innerText.includes('Valeur') && th === 'valeur') btn.classList.add('active');
        else if (!btn.innerText.includes('Feed') && !btn.innerText.includes('Story')) btn.classList.remove('active');
      });
      renderGrid();
    }

    function showToast(msg) {
      const toast = document.getElementById('toast');
      toast.innerText = msg;
      toast.style.display = 'block';
      setTimeout(() => { toast.style.display = 'none'; }, 2500);
    }

    function copyToClipboard(text, msg) {
      navigator.clipboard.writeText(text).then(() => {
        showToast(msg);
      });
    }

    function renderGrid() {
      const container = document.getElementById('gridContainer');
      const filtered = items.filter(it => currentTheme === 'all' || it.theme === currentTheme);

      container.innerHTML = filtered.map(it => {
        const imgSrc = currentFormat === 'feed' ? it.feedImg : it.storyImg;
        const formatClass = currentFormat === 'feed' ? 'format-feed' : 'format-story';
        return \`
          <div class="card">
            <div class="media-wrap \${formatClass}">
              <img src="\${imgSrc}" alt="\${it.title}" loading="lazy">
            </div>
            <div class="card-content">
              <div class="card-header-badge">\${it.badge}</div>
              <h2 class="card-title">\${it.title}</h2>
              <div class="card-desc-box">\${it.fullCaption}</div>
              
              <div class="comment-box">
                <div class="comment-title">💬 1er Commentaire Épinglé :</div>
                <div>\${it.comment}</div>
              </div>

              <div class="actions-grid">
                <button class="btn-action" onclick="copyToClipboard(\`\${encodeURIComponent(it.fullCaption)}\`, '📋 Description copiée !')">
                  📋 Copier Caption
                </button>
                <button class="btn-action btn-comment" onclick="copyToClipboard(\`\${encodeURIComponent(it.comment)}\`, '💬 1er Commentaire copié !')">
                  💬 Copier 1er Com.
                </button>
              </div>

              <div class="download-links">
                <a href="\${it.feedImg}" download class="link-download">💾 HD Feed 1:1 (2160p)</a>
                <a href="\${it.storyImg}" download class="link-download">💾 HD Story 9:16 (2160p)</a>
              </div>
            </div>
          </div>
        \`;
      }).join('');
    }

    // Décodage au clic
    window.copyToClipboard = function(encoded, msg) {
      const text = decodeURIComponent(encoded);
      navigator.clipboard.writeText(text).then(() => {
        showToast(msg);
      });
    };

    renderGrid();
  </script>
</body>
</html>
`;

fs.writeFileSync(path.join(rootDir, 'galerie_preview_60jours.html'), html, 'utf8');
console.log(`✅ galerie_preview_60jours.html généré avec succès !`);
