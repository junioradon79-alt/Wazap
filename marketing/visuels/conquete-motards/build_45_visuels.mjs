import fs from 'fs';
import path from 'path';
import { spawnSync } from 'child_process';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const manifestPath = path.join(__dirname, 'manifest_45_visuels.json');
const templatePath = path.join(__dirname, 'render_template.html');
const outDir = path.join(__dirname, 'generated');

if (!fs.existsSync(outDir)) {
  fs.mkdirSync(outDir, { recursive: true });
}

const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
const baseTemplate = fs.readFileSync(templatePath, 'utf8');

const edgePaths = [
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
];
const browserPath = edgePaths.find(p => fs.existsSync(p));

if (!browserPath) {
  console.error("Aucun navigateur trouvé !");
  process.exit(1);
}

console.log(`Navigateur détecté : ${browserPath}`);
console.log(`Nombre total de visuels à générer : ${manifest.length}`);

// Test sur les arguments ou exécution complète
const tempHtmlPath = path.join(__dirname, 'temp_render.html');
const tempUserDataDir = path.join(process.env.TEMP || 'C:\\temp', 'wazap-batch-edge');

let successCount = 0;

for (let i = 0; i < manifest.length; i++) {
  const item = manifest[i];
  const dayStr = String(item.jour).padStart(2, '0');
  const filename = `visuel_j${dayStr}_${item.slot}.png`;
  const outPngPath = path.join(outDir, filename);

  const injectionScript = `
  <script>
    const data = ${JSON.stringify(item)};
    document.getElementById('badge-text').textContent = data.badge;
    document.getElementById('day-text').textContent = "JOUR " + data.jour + " • " + data.slot.toUpperCase();
    document.getElementById('avatar-img').src = "../facebook-livreurs/assets/avatars/" + data.character + ".jpg";
    document.getElementById('avatar-name').textContent = data.characterName;
    document.getElementById('statement-hook').textContent = data.hook;
    document.getElementById('statement-highlight').textContent = data.highlight;
    document.getElementById('statement-subtext').textContent = data.subtext;
    document.getElementById('kpi-header').textContent = data.kpiTitle;
    document.getElementById('kpi-other').textContent = data.kpiOther;
    document.getElementById('kpi-wazap').textContent = data.kpiWazap;
  </script>
</body>`;

  // Remplacement du script final
  const customizedHtml = baseTemplate.replace(/<script>[\s\S]*?<\/script>\s*<\/body>/i, injectionScript);
  fs.writeFileSync(tempHtmlPath, customizedHtml, 'utf8');

  const fileUri = 'file:///' + tempHtmlPath.replace(/\\/g, '/');

  const args = [
    '--headless',
    '--disable-gpu',
    '--no-sandbox',
    '--no-first-run',
    '--hide-scrollbars',
    `--user-data-dir=${tempUserDataDir}`,
    '--window-size=1080,1080',
    '--force-device-scale-factor=2',
    '--virtual-time-budget=2000',
    `--screenshot=${outPngPath}`,
    fileUri
  ];

  const res = spawnSync(browserPath, args, { stdio: 'ignore', timeout: 15000 });

  if (fs.existsSync(outPngPath) && fs.statSync(outPngPath).size > 10000) {
    const sizeKb = Math.round(fs.statSync(outPngPath).size / 1024);
    console.log(`[${i + 1}/${manifest.length}] ✅ ${filename} généré (${sizeKb} Ko) - Jour ${item.jour} (${item.slot})`);
    successCount++;
  } else {
    console.error(`[${i + 1}/${manifest.length}] ❌ Échec pour ${filename}`);
  }
}

// Nettoyage
if (fs.existsSync(tempHtmlPath)) fs.unlinkSync(tempHtmlPath);

console.log(`\n🎉 Génération terminée : ${successCount}/${manifest.length} visuels prêts dans ${outDir}`);
