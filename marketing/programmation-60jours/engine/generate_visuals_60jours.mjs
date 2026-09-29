import fs from 'fs';
import path from 'path';
import { spawnSync } from 'child_process';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const rootDir = path.resolve(__dirname, '..');
const assetsDir = path.join(rootDir, 'assets');
const templatesDir = path.join(rootDir, 'templates');
const outFeedDir = path.join(rootDir, 'visuels', 'feed');
const outStoryDir = path.join(rootDir, 'visuels', 'story');
const tempDir = path.join(rootDir, 'templates', 'temp');

if (!fs.existsSync(outFeedDir)) fs.mkdirSync(outFeedDir, { recursive: true });
if (!fs.existsSync(outStoryDir)) fs.mkdirSync(outStoryDir, { recursive: true });
if (!fs.existsSync(tempDir)) fs.mkdirSync(tempDir, { recursive: true });

// Détection navigateur Headless (Edge ou Chrome)
const edgePaths = [
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
];
const browserPath = edgePaths.find(p => fs.existsSync(p));

if (!browserPath) {
  console.error("❌ Aucun navigateur compatible trouvé pour le rendu !");
  process.exit(1);
}

const tempUserDataDir = path.join(process.env.TEMP || 'C:\\temp', 'wazap-60jours-render');

// Lecture du catalogue et des templates
const catalogPath = path.join(__dirname, 'visuals_catalog.json');
const catalog = JSON.parse(fs.readFileSync(catalogPath, 'utf8'));

const feedTemplateStr = fs.readFileSync(path.join(templatesDir, 'template_feed_square.html'), 'utf8');
const storyTemplateStr = fs.readFileSync(path.join(templatesDir, 'template_story_vertical.html'), 'utf8');

console.log(`================================================================`);
console.log(`🎨 GÉNÉRATEUR OFFICIEL DE VISUELS WAZAP — CAMPAGNE 60 JOURS`);
console.log(`📍 Moteur : ${browserPath}`);
console.log(`🎯 Catalogue : ${catalog.length} visuels maîtres (Feed 1:1 + Story 9:16)`);
console.log(`================================================================\n`);

function renderTemplate(templateStr, item, format) {
  const pointsHtml = item.points.map(pt => `
    <div class="shield-item ${pt.highlight ? 'highlight' : ''}">
      <span class="shield-icon">${pt.icon}</span>
      <div class="shield-content">
        <div class="shield-title">${pt.title}</div>
        <div class="shield-desc">${pt.desc}</div>
      </div>
    </div>
  `).join('\n');

  return templateStr
    .replace(/\{\{BADGE_TOP\}\}/g, item.badge_top)
    .replace(/\{\{BADGE_TOP_BG\}\}/g, item.badge_top_bg)
    .replace(/\{\{BADGE_TOP_SHADOW\}\}/g, item.badge_top_shadow)
    .replace(/\{\{HEADLINE_TAG\}\}/g, item.headline_tag)
    .replace(/\{\{HEADLINE_TITLE\}\}/g, item.headline_title)
    .replace(/\{\{HEADLINE_SUB\}\}/g, item.headline_sub)
    .replace(/\{\{PERSONNAGE_IMG\}\}/g, item.personnage_img)
    .replace(/\{\{PERSONNAGE_NAME\}\}/g, item.personnage_name)
    .replace(/\{\{PERSONNAGE_BADGE\}\}/g, item.personnage_badge)
    .replace(/\{\{CARD_TITLE\}\}/g, item.card_title)
    .replace(/\{\{CARD_DESC\}\}/g, item.card_desc)
    .replace(/\{\{PAYOUT_LABEL\}\}/g, item.payout_label)
    .replace(/\{\{PAYOUT_VAL\}\}/g, item.payout_val)
    .replace(/\{\{PAYOUT_TAG\}\}/g, item.payout_tag)
    .replace(/\{\{POINTS_HTML\}\}/g, pointsHtml)
    .replace(/\{\{OFFER_STRIP_TEXT\}\}/g, item.offer_strip_text)
    .replace(/\{\{OFFER_STRIP_PILL\}\}/g, item.offer_strip_pill)
    .replace(/\{\{CTA_BANNER_TEXT\}\}/g, item.cta_banner_text)
    .replace(/\{\{QR_SRC\}\}/g, item.qr_src)
    .replace(/\{\{QR_BOTTOM_LABEL\}\}/g, item.qr_bottom_label)
    // Résolution relative correcte vers le dossier assets
    .replace(/assets\//g, '../assets/');
}

let generatedCount = 0;

for (let i = 0; i < catalog.length; i++) {
  const item = catalog[i];
  console.log(`\n📌 [${i + 1}/${catalog.length}] Traitement : ${item.personnage_name} (${item.theme.toUpperCase()})`);

  // --- 1. Rendu Feed Carré (1080x1080 @ 2x = 2160x2160) ---
  const feedHtml = renderTemplate(feedTemplateStr, item, 'feed');
  const tempFeedFile = path.join(tempDir, `temp_feed_${item.id}.html`);
  fs.writeFileSync(tempFeedFile, feedHtml, 'utf8');

  const outFeedPng = path.join(outFeedDir, `${item.id}_feed_square.png`);
  const feedUri = 'file:///' + tempFeedFile.replace(/\\/g, '/');

  const argsFeed = [
    '--headless',
    '--disable-gpu',
    '--no-sandbox',
    '--no-first-run',
    '--hide-scrollbars',
    `--user-data-dir=${tempUserDataDir}`,
    '--window-size=1080,1080',
    '--force-device-scale-factor=2',
    '--virtual-time-budget=3000',
    `--screenshot=${outFeedPng}`,
    feedUri
  ];

  spawnSync(browserPath, argsFeed, { timeout: 35000 });

  if (fs.existsSync(outFeedPng) && fs.statSync(outFeedPng).size > 15000) {
    const sizeKb = Math.round(fs.statSync(outFeedPng).size / 1024);
    console.log(`  ✅ FEED 1:1  : ${item.id}_feed_square.png (${sizeKb} Ko - 2160×2160 HD)`);
    generatedCount++;
  } else {
    console.error(`  ❌ Échec Feed pour ${item.id}`);
  }

  // --- 2. Rendu Story Vertical (1080x1920 @ 2x = 2160x3840) ---
  const storyHtml = renderTemplate(storyTemplateStr, item, 'story');
  const tempStoryFile = path.join(tempDir, `temp_story_${item.id}.html`);
  fs.writeFileSync(tempStoryFile, storyHtml, 'utf8');

  const outStoryPng = path.join(outStoryDir, `${item.id}_story_vertical.png`);
  const storyUri = 'file:///' + tempStoryFile.replace(/\\/g, '/');

  const argsStory = [
    '--headless',
    '--disable-gpu',
    '--no-sandbox',
    '--no-first-run',
    '--hide-scrollbars',
    `--user-data-dir=${tempUserDataDir}`,
    '--window-size=1080,1920',
    '--force-device-scale-factor=2',
    '--virtual-time-budget=3500',
    `--screenshot=${outStoryPng}`,
    storyUri
  ];

  spawnSync(browserPath, argsStory, { timeout: 40000 });

  if (fs.existsSync(outStoryPng) && fs.statSync(outStoryPng).size > 15000) {
    const sizeKb = Math.round(fs.statSync(outStoryPng).size / 1024);
    console.log(`  ✅ STORY 9:16 : ${item.id}_story_vertical.png (${sizeKb} Ko - 2160×3840 HD)`);
    generatedCount++;
  } else {
    console.error(`  ❌ Échec Story pour ${item.id}`);
  }
}

console.log(`\n================================================================`);
console.log(`🎉 SUCCÈS TOTAL : ${generatedCount} fichiers haute définition générés avec succès !`);
console.log(`📁 Visuels Feed Carré 1:1 : ${outFeedDir}`);
console.log(`📁 Visuels Story 9:16     : ${outStoryDir}`);
console.log(`================================================================\n`);
