import fs from 'fs';
import path from 'path';
import { spawnSync } from 'child_process';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const posters = [
  {
    filename: "story_01_plancher_1000f.png",
    badge: "🛑 STOP AUX 450 FCFA !",
    photo: "motard_pouce.jpg",
    name: "Amara T. • Livreur Cocody",
    headSub: "Grille Tarifaire Inviolable",
    headMain: '1 000 F <span>MINIMUM</span> PAR COURSE !',
    headDesc: "0% de commission prélevée sur toi • 100% de la course dans ta poche !"
  },
  {
    filename: "story_02_smartphone_redmi.png",
    badge: "🎁 DÉFI DU MOIS LIVREURS",
    photo: "motard_redmi.jpg",
    name: "Bakary S. • Livreur Marcory",
    headSub: "Le Grand Cadeau des Pionniers",
    headMain: 'SMARTPHONE <span>REDMI 15C</span> OFFERT !',
    headDesc: "Chaque fin de mois, le livreur le plus sérieux d'Abidjan repart avec sa boîte scellée !"
  },
  {
    filename: "story_03_zero_appli_whatsapp.png",
    badge: "⚡ 100% SIMPLE & LÉGER",
    photo: "motard_ecran.jpg",
    name: "Koffi M. • Livreur Koumassi",
    headSub: "Zéro Tracas Technique",
    headMain: 'TOUT SE PASSE SUR <span>WHATSAPP</span> !',
    headDesc: "Pas d'application lourde qui plante. Tu reçois la course ➔ Tu cliques ➔ Tu gagnes !"
  },
  {
    filename: "story_04_bilan_journee.png",
    badge: "💰 FAIS LE CALCUL DE TA VIE",
    photo: "motard_pouce.jpg",
    name: "Bakary & Amara • Livreurs WAZAP",
    headSub: "Comparatif 8 Courses par Jour",
    headMain: '8 COURSES = <span>8 000 F NETS</span> EN POCHE !',
    headDesc: "Arrête d'enrichir les autres. Gagne enfin 3 fois plus sur ta propre moto !"
  }
];

const templatePath = path.join(__dirname, 'render_poster_story.html');
const outDir = path.join(__dirname, 'generated');

if (!fs.existsSync(outDir)) {
  fs.mkdirSync(outDir, { recursive: true });
}

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

const tempHtmlPath = path.join(__dirname, 'temp_story.html');
const tempUserDataDir = path.join(process.env.TEMP || 'C:\\temp', 'wazap-story-edge');

console.log(`Génération des affiches Stories 9:16 (1080x1920) avec ${browserPath}...`);

for (let i = 0; i < posters.length; i++) {
  const item = posters[i];
  const outPngPath = path.join(outDir, item.filename);

  const injectionScript = `
  <script>
    document.getElementById('top-badge').innerHTML = ${JSON.stringify(item.badge)};
    document.getElementById('poster-photo').src = "assets/" + ${JSON.stringify(item.photo)};
    document.getElementById('tag-name').textContent = ${JSON.stringify(item.name)};
    document.getElementById('head-sub').textContent = ${JSON.stringify(item.headSub)};
    document.getElementById('head-main').innerHTML = ${JSON.stringify(item.headMain)};
    document.getElementById('head-desc').textContent = ${JSON.stringify(item.headDesc)};
  </script>
</body>`;

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
    '--window-size=1080,1920',
    '--virtual-time-budget=2500',
    `--screenshot=${outPngPath}`,
    fileUri
  ];

  spawnSync(browserPath, args, { stdio: 'ignore', timeout: 20000 });

  if (fs.existsSync(outPngPath) && fs.statSync(outPngPath).size > 10000) {
    const sizeKb = Math.round(fs.statSync(outPngPath).size / 1024);
    console.log(`[${i + 1}/${posters.length}] ✅ ${item.filename} généré (${sizeKb} Ko) - 1080x1920 Story`);
  } else {
    console.error(`[${i + 1}/${posters.length}] ❌ Échec pour ${item.filename}`);
  }
}

if (fs.existsSync(tempHtmlPath)) fs.unlinkSync(tempHtmlPath);

console.log("\n🎉 Génération des affiches Stories 9:16 terminée !");
