import fs from 'fs';
import path from 'path';
import { spawnSync } from 'child_process';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const posters = [
  {
    filename: "affiche_01_plancher_1000f.png",
    badge: "<span>🛑</span> STOP AUX 450 FCFA !",
    photo: "motard_pouce.jpg",
    name: "Amara T. • Livreur Cocody",
    headSub: "Grille Tarifaire Inviolable",
    headMain: '1 000 F <span class="glow-green">MINIMUM</span> PAR COURSE !',
    headDesc: "Même pour 300 mètres dans le même quartier. Zéro commission prélevée sur toi !",
    p1Title: "0% de Commission Livreur",
    p1Sub: "100% du prix de la course est pour toi en direct",
    p2Title: "Tout se passe sur WhatsApp",
    p2Sub: "Pas d'application lourde qui plante ton téléphone",
    p3Title: "Défi Smartphone Redmi 15C",
    p3Sub: "1 Smartphone neuf offert chaque mois au livreur N°1"
  },
  {
    filename: "affiche_02_smartphone_redmi.png",
    badge: "<span>🎁</span> DÉFI DU MOIS LIVREURS WAZAP",
    photo: "motard_redmi.jpg",
    name: "Bakary S. • Livreur Marcory",
    headSub: "Le Grand Cadeau des Pionniers",
    headMain: 'SMARTPHONE <span class="glow-green">REDMI 15C</span> NEUF OFFERT !',
    headDesc: "Chaque fin de mois, le livreur le plus sérieux et assidu d'Abidjan repart avec sa boîte scellée !",
    p1Title: "Batterie 5 000 mAh & Écran HD",
    p1Sub: "Idéal pour tenir toute la journée sur la moto sans s'éteindre",
    p2Title: "Gagné au mérite et au travail",
    p2Sub: "Basé sur ton sérieux, ta ponctualité et tes avis 5 étoiles",
    p3Title: "1 000 F Net Minimum par Course",
    p3Sub: "Tu gagnes dignement ta vie sur chaque livraison"
  },
  {
    filename: "affiche_03_zero_appli_whatsapp.png",
    badge: "<span>⚡</span> 100% SIMPLE & LÉGER",
    photo: "motard_ecran.jpg",
    name: "Koffi M. • Livreur Koumassi",
    headSub: "Zéro Tracas Technique",
    headMain: 'TOUT SE PASSE SUR <span class="glow-green">WHATSAPP</span> !',
    headDesc: "Pas besoin de télécharger une application de 150 Mo qui bouffe ta batterie et tes données.",
    p1Title: "Alerte de Course en direct",
    p1Sub: "Tu vois le quartier, la destination et le prix net garanti",
    p2Title: "Tu cliques pour Accepter",
    p2Sub: "Feuille de route immédiate avec itinéraire GPS en 1 tap",
    p3Title: "Paiement Direct à la Remise",
    p3Sub: "Validation instantanée via le QR Code Universel WAZAP"
  },
  {
    filename: "affiche_04_bilan_journee.png",
    badge: "<span>💰</span> FAIS LE CALCUL DE TA VIE",
    photo: "motard_pouce.jpg",
    name: "Bakary & Amara • Livreurs WAZAP",
    headSub: "Comparatif 8 Courses par Jour",
    headMain: '8 COURSES = <span class="glow-green">8 000 F NETS</span> EN POCHE !',
    headDesc: "Sur les autres applis tu touches à peine 2 800 F après commissions. Chez WAZAP, tu gagnes 3 fois plus !",
    p1Title: "0% de Commission Prélevée",
    p1Sub: "Pas de gérant de flotte qui coupe ton argent",
    p2Title: "L'Essence Rentabilisée",
    p2Sub: "Avec le litre de super à 875 F, chaque course compte",
    p3Title: "Flotte Pionnière Ouverte",
    p3Sub: "Inscris-toi maintenant avant la clôture des 100 places"
  }
];

const templatePath = path.join(__dirname, 'render_poster_carre.html');
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

const tempHtmlPath = path.join(__dirname, 'temp_poster.html');
const tempUserDataDir = path.join(process.env.TEMP || 'C:\\temp', 'wazap-recrutement-edge');

console.log(`Génération des affiches de recrutement avec ${browserPath}...`);

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
    document.getElementById('p1-title').textContent = ${JSON.stringify(item.p1Title)};
    document.getElementById('p1-sub').textContent = ${JSON.stringify(item.p1Sub)};
    document.getElementById('p2-title').textContent = ${JSON.stringify(item.p2Title)};
    document.getElementById('p2-sub').textContent = ${JSON.stringify(item.p2Sub)};
    document.getElementById('p3-title').textContent = ${JSON.stringify(item.p3Title)};
    document.getElementById('p3-sub').textContent = ${JSON.stringify(item.p3Sub)};
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
    '--window-size=1080,1080',
    '--force-device-scale-factor=2',
    '--virtual-time-budget=2500',
    `--screenshot=${outPngPath}`,
    fileUri
  ];

  spawnSync(browserPath, args, { stdio: 'ignore', timeout: 20000 });

  if (fs.existsSync(outPngPath) && fs.statSync(outPngPath).size > 10000) {
    const sizeKb = Math.round(fs.statSync(outPngPath).size / 1024);
    console.log(`[${i + 1}/${posters.length}] ✅ ${item.filename} généré (${sizeKb} Ko) - 2160x2160`);
  } else {
    console.error(`[${i + 1}/${posters.length}] ❌ Échec pour ${item.filename}`);
  }
}

if (fs.existsSync(tempHtmlPath)) fs.unlinkSync(tempHtmlPath);

console.log("\n🎉 Génération des affiches carrées terminée !");
