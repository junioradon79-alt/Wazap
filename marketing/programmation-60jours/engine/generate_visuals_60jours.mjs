import fs from 'fs';
import path from 'path';
import { spawnSync } from 'child_process';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const rootDir = path.resolve(__dirname, '..');
const templatesDir = path.join(rootDir, 'templates');
const outFeedDir = path.join(rootDir, 'visuels', 'feed');
const outStoryDir = path.join(rootDir, 'visuels', 'story');
const tempDir = path.join(rootDir, 'templates', 'temp');

if (!fs.existsSync(outFeedDir)) fs.mkdirSync(outFeedDir, { recursive: true });
if (!fs.existsSync(outStoryDir)) fs.mkdirSync(outStoryDir, { recursive: true });
if (!fs.existsSync(tempDir)) fs.mkdirSync(tempDir, { recursive: true });

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

const tempUserDataDir = path.join(process.env.TEMP || 'C:\\temp', 'wazap-3d-poster-edge');

const catalogPath = path.join(__dirname, 'visuals_catalog.json');
const catalog = JSON.parse(fs.readFileSync(catalogPath, 'utf8'));

const feedTemplateStr = fs.readFileSync(path.join(templatesDir, 'poster_feed_3d_template.html'), 'utf8');
const storyTemplateStr = fs.readFileSync(path.join(templatesDir, 'poster_story_3d_template.html'), 'utf8');

console.log(`================================================================`);
console.log(`🎨 GÉNÉRATEUR D'AFFICHES PUBLICITAIRES 3D WAZAP (CAMPAGNE 60 JOURS)`);
console.log(`📍 Moteur : ${browserPath}`);
console.log(`🎯 Catalogue : ${catalog.length} affiches 3D (Feed 1:1 + Story 9:16)`);
console.log(`================================================================\n`);

function renderTemplate(templateStr, item, format) {
  const stepsHtml = (format === 'story')
    ? item.steps.map(s => `
      <div class="step-item">
        <div class="step-number-tag">${s.num}</div>
        <div class="step-icon-circle">${s.icon}</div>
        <div class="step-label">${s.label}</div>
      </div>
    `).join('\n')
    : item.steps.map(s => `
      <div class="step-item-feed">
        <div class="step-number-tag-feed">${s.num}</div>
        <div class="step-icon-circle-feed">${s.icon}</div>
        <div class="step-label-feed">${s.label}</div>
      </div>
    `).join('\n');

  return templateStr
    .replace(/\{\{BADGE_TOP\}\}/g, item.badge_top)
    .replace(/\{\{BADGE_TOP_BG\}\}/g, item.badge_top_bg)
    .replace(/\{\{KICKER_TEXT\}\}/g, item.kicker_text)
    .replace(/\{\{TITLE_LINE_1\}\}/g, item.title_line_1)
    .replace(/\{\{TITLE_LINE_2\}\}/g, item.title_line_2)
    .replace(/\{\{SUBTITLE_TEXT\}\}/g, item.subtitle_text)
    .replace(/\{\{HERO_IMG\}\}/g, item.hero_img)
    .replace(/\{\{HERO_ALT\}\}/g, item.hero_alt)
    .replace(/\{\{HERO_TAG\}\}/g, item.hero_tag)
    .replace(/\{\{BADGE_LEFT_TITLE\}\}/g, item.badge_left_title)
    .replace(/\{\{BADGE_LEFT_VAL\}\}/g, item.badge_left_val)
    .replace(/\{\{BADGE_RIGHT_TITLE\}\}/g, item.badge_right_title)
    .replace(/\{\{BADGE_RIGHT_VAL\}\}/g, item.badge_right_val)
    .replace(/\{\{BADGE_RIGHT_SUB\}\}/g, item.badge_right_sub)
    .replace(/\{\{STEPS_HTML\}\}/g, stepsHtml)
    .replace(/\{\{RIBBON_TEXT\}\}/g, item.ribbon_text)
    .replace(/\{\{CTA_TEXT\}\}/g, item.cta_text)
    .replace(/\{\{QR_SRC\}\}/g, item.qr_src)
    .replace(/\{\{QR_BOTTOM_TEXT\}\}/g, item.qr_bottom_text);
}

let generatedCount = 0;

for (let i = 0; i < catalog.length; i++) {
  const item = catalog[i];
  console.log(`\n📌 [${i + 1}/${catalog.length}] Affiche 3D : ${item.hero_tag} (${item.theme.toUpperCase()})`);

  // --- 1. Rendu Feed Carré 1:1 (1080x1080 @ 2x = 2160x2160) ---
  const feedHtml = renderTemplate(feedTemplateStr, item, 'feed');
  const tempFeedFile = path.join(tempDir, `temp_feed_${item.id}.html`);
  fs.writeFileSync(tempFeedFile, feedHtml, 'utf8');

  const outFeedPng = path.join(outFeedDir, `${item.id}_feed_square.png`);
  if (fs.existsSync(outFeedPng)) fs.unlinkSync(outFeedPng);
  const feedUri = 'file:///' + tempFeedFile.replace(/\\/g, '/');

  const feedUserDir = path.join(tempUserDataDir, `feed_${i}`);
  const argsFeed = [
    '--headless',
    '--disable-gpu',
    '--no-sandbox',
    '--no-first-run',
    '--hide-scrollbars',
    `--user-data-dir=${feedUserDir}`,
    '--window-size=1080,1080',
    '--force-device-scale-factor=2',
    '--virtual-time-budget=3500',
    `--screenshot=${outFeedPng}`,
    feedUri
  ];

  spawnSync(browserPath, argsFeed, { timeout: 40000 });

  if (fs.existsSync(outFeedPng) && fs.statSync(outFeedPng).size > 20000) {
    const sizeKb = Math.round(fs.statSync(outFeedPng).size / 1024);
    console.log(`  ✅ FEED 1:1  : ${item.id}_feed_square.png (${sizeKb} Ko - 2160×2160 3D HD)`);
    generatedCount++;
  } else {
    console.error(`  ❌ Échec Feed 3D pour ${item.id}`);
  }

  // --- 2. Rendu Story Vertical 9:16 (1080x1920 @ 2x = 2160x3840) ---
  const storyHtml = renderTemplate(storyTemplateStr, item, 'story');
  const tempStoryFile = path.join(tempDir, `temp_story_${item.id}.html`);
  fs.writeFileSync(tempStoryFile, storyHtml, 'utf8');

  const outStoryPng = path.join(outStoryDir, `${item.id}_story_vertical.png`);
  if (fs.existsSync(outStoryPng)) fs.unlinkSync(outStoryPng);
  const storyUri = 'file:///' + tempStoryFile.replace(/\\/g, '/');

  const storyUserDir = path.join(tempUserDataDir, `story_${i}`);
  const argsStory = [
    '--headless',
    '--disable-gpu',
    '--no-sandbox',
    '--no-first-run',
    '--hide-scrollbars',
    `--user-data-dir=${storyUserDir}`,
    '--window-size=1080,1920',
    '--force-device-scale-factor=2',
    '--virtual-time-budget=3500',
    `--screenshot=${outStoryPng}`,
    storyUri
  ];

  spawnSync(browserPath, argsStory, { timeout: 40000 });

  if (fs.existsSync(outStoryPng) && fs.statSync(outStoryPng).size > 20000) {
    const sizeKb = Math.round(fs.statSync(outStoryPng).size / 1024);
    console.log(`  ✅ STORY 9:16 : ${item.id}_story_vertical.png (${sizeKb} Ko - 2160×3840 3D HD)`);
    generatedCount++;
  } else {
    console.error(`  ❌ Échec Story 3D pour ${item.id}`);
  }
}

console.log(`\n================================================================`);
console.log(`🎉 SUCCÈS TOTAL : ${generatedCount} affiches publicitaires 3D générées avec succès !`);
console.log(`📁 Affiches Feed Carré 1:1 : ${outFeedDir}`);
console.log(`📁 Affiches Story 9:16     : ${outStoryDir}`);
console.log(`================================================================\n`);
