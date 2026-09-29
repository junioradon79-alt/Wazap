import fs from 'fs';
import path from 'path';
import { spawnSync } from 'child_process';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const outDir = path.join(__dirname, 'generated');
if (!fs.existsSync(outDir)) {
  fs.mkdirSync(outDir, { recursive: true });
}

const edgePaths = [
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
];
const browserPath = edgePaths.find(p => fs.existsSync(p));

if (!browserPath) {
  console.error("Aucun navigateur compatible trouvé pour le rendu !");
  process.exit(1);
}

const tempUserDataDir = path.join(process.env.TEMP || 'C:\\temp', 'wazap-commercant-edge');

const targets = [
  {
    template: 'render_visuel_vendeur_securite.html',
    output: 'visuel_facebook_securite_vendeur_01.png',
    name: 'Visuel 1 - Preuve Sécurité Terrain (Scan QR & 0% Cash)'
  },
  {
    template: 'render_visuel_vendeur_comparatif.html',
    output: 'visuel_facebook_securite_vendeur_02_comparatif.png',
    name: 'Visuel 2 - Duel Comparatif Ancienne Méthode vs WAZAP'
  },
  {
    template: 'render_visuel_vendeur_temoignage.html',
    output: 'visuel_facebook_securite_vendeur_03_temoignage.png',
    name: 'Visuel 3 - Témoignage Boutique & Colis Sécurisés (Awa Chic)'
  }
];

console.log(`🚀 Lancement de la génération des visuels Facebook Vendeurs avec ${browserPath}...\n`);

for (let i = 0; i < targets.length; i++) {
  const item = targets[i];
  const templatePath = path.join(__dirname, item.template);
  const outPngPath = path.join(outDir, item.output);
  const fileUri = 'file:///' + templatePath.replace(/\\/g, '/');

  console.log(`[${i + 1}/${targets.length}] Rendu de "${item.name}"...`);

  const args = [
    '--headless',
    '--disable-gpu',
    '--no-sandbox',
    '--no-first-run',
    '--hide-scrollbars',
    `--user-data-dir=${tempUserDataDir}`,
    '--window-size=1080,1080',
    '--force-device-scale-factor=2',
    '--virtual-time-budget=3000',
    `--screenshot=${outPngPath}`,
    fileUri
  ];

  const res = spawnSync(browserPath, args, { stdio: 'inherit', timeout: 30000 });

  if (fs.existsSync(outPngPath) && fs.statSync(outPngPath).size > 10000) {
    const sizeKb = Math.round(fs.statSync(outPngPath).size / 1024);
    console.log(`  ✅ Succès : ${item.output} (${sizeKb} Ko - 2160×2160 HD)\n`);
  } else {
    console.error(`  ❌ Échec de génération pour ${item.output}\n`);
  }
}

console.log("🎉 Tous les visuels Facebook Vendeurs sont générés dans :", outDir);
