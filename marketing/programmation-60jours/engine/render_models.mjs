import fs from 'fs';
import path from 'path';
import { spawnSync } from 'child_process';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const rootDir = path.resolve(__dirname, '..');

const edgePaths = [
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
];
const browserPath = edgePaths.find(p => fs.existsSync(p));

if (!browserPath) {
  console.error("❌ Aucun navigateur compatible trouvé !");
  process.exit(1);
}

const outFeedDir = path.join(rootDir, 'visuels', 'feed');
const outStoryDir = path.join(rootDir, 'visuels', 'story');
const tempDir = path.join(rootDir, 'templates', 'temp');

if (!fs.existsSync(outFeedDir)) fs.mkdirSync(outFeedDir, { recursive: true });
if (!fs.existsSync(outStoryDir)) fs.mkdirSync(outStoryDir, { recursive: true });
if (!fs.existsSync(tempDir)) fs.mkdirSync(tempDir, { recursive: true });

const models = [
  {
    id: 'modele_01',
    name: 'Modèle 1 — Fini les livreurs qui disparaissent (Pose A)',
    imgFile: path.join(rootDir, 'assets', 'modele_visuel_commercante_1.jpg'),
    bgColor: '#F6E6C4'
  },
  {
    id: 'modele_02',
    name: 'Modèle 2 — Fini les livreurs qui disparaissent (Pose B)',
    imgFile: path.join(rootDir, 'assets', 'modele_visuel_commercante_2.jpg'),
    bgColor: '#EDE8E2'
  }
];

function createHtml(model, format) {
  const isFeed = format === 'feed';
  const width = 1080;
  const height = isFeed ? 1080 : 1920;
  const imgUri = 'file:///' + model.imgFile.replace(/\\/g, '/');

  return `<!DOCTYPE html>
<html lang="fr">
<head>
  <meta charset="utf-8">
  <title>${model.name}</title>
  <style>
    * { margin: 0; padding: 0; box-sizing: border-box; }
    body {
      width: ${width}px;
      height: ${height}px;
      background-color: ${model.bgColor};
      display: flex;
      justify-content: center;
      align-items: center;
      overflow: hidden;
    }
    .poster-img {
      ${isFeed ? 'height: 1080px; width: auto;' : 'width: 1080px; height: auto;'}
      display: block;
      object-fit: contain;
    }
  </style>
</head>
<body>
  <img class="poster-img" src="${imgUri}" alt="${model.name}">
</body>
</html>`;
}

console.log("🚀 Rendu haute définition des deux modèles officiels...");

for (const m of models) {
  // Feed
  const feedHtml = createHtml(m, 'feed');
  const tempFeed = path.join(tempDir, `render_feed_${m.id}.html`);
  fs.writeFileSync(tempFeed, feedHtml, 'utf8');
  const outFeed = path.join(outFeedDir, `visuel_${m.id}_fini_les_livreurs_feed_square.png`);
  
  spawnSync(browserPath, [
    '--headless',
    '--disable-gpu',
    '--no-sandbox',
    '--no-first-run',
    '--hide-scrollbars',
    '--window-size=1080,1080',
    '--force-device-scale-factor=2',
    `--screenshot=${outFeed}`,
    'file:///' + tempFeed.replace(/\\/g, '/')
  ]);

  if (fs.existsSync(outFeed)) {
    const size = Math.round(fs.statSync(outFeed).size / 1024);
    console.log(`✅ ${m.name} -> FEED 1:1 généré (${size} Ko) : ${path.basename(outFeed)}`);
  }

  // Story
  const storyHtml = createHtml(m, 'story');
  const tempStory = path.join(tempDir, `render_story_${m.id}.html`);
  fs.writeFileSync(tempStory, storyHtml, 'utf8');
  const outStory = path.join(outStoryDir, `visuel_${m.id}_fini_les_livreurs_story_vertical.png`);

  spawnSync(browserPath, [
    '--headless',
    '--disable-gpu',
    '--no-sandbox',
    '--no-first-run',
    '--hide-scrollbars',
    '--window-size=1080,1920',
    '--force-device-scale-factor=2',
    `--screenshot=${outStory}`,
    'file:///' + tempStory.replace(/\\/g, '/')
  ]);

  if (fs.existsSync(outStory)) {
    const size = Math.round(fs.statSync(outStory).size / 1024);
    console.log(`✅ ${m.name} -> STORY 9:16 généré (${size} Ko) : ${path.basename(outStory)}`);
  }
}

console.log("🎉 Rendu terminé avec succès !");
