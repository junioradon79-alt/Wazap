import fs from 'fs';
import path from 'path';
import { spawnSync } from 'child_process';
import { fileURLToPath } from 'url';
import os from 'os';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Paths
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const manifestFile = path.resolve(__dirname, '..', 'manifest_tiktok_90jours.json');
const htmlTemplate = path.resolve(__dirname, 'render_slide.html');
const outputDir = path.resolve(__dirname, '..', 'generated');
const tempFramesDir = path.resolve(__dirname, 'temp_frames');
const csvFile = path.resolve(__dirname, '..', 'CALENDRIER_PROGRAMMATION_TIKTOK.csv');

// Create directories
fs.mkdirSync(outputDir, { recursive: true });
fs.mkdirSync(tempFramesDir, { recursive: true });

// Parse CLI arguments
const args = process.argv.slice(2);
let startId = 1;
let endId = 90;
let force = false;

for (let i = 0; i < args.length; i++) {
  if (args[i] === '--start' && args[i + 1]) {
    startId = parseInt(args[++i], 10);
  } else if (args[i] === '--end' && args[i + 1]) {
    endId = parseInt(args[++i], 10);
  } else if (args[i] === '--id' && args[i + 1]) {
    const id = parseInt(args[++i], 10);
    startId = id;
    endId = id;
  } else if (args[i] === '--force') {
    force = true;
  } else if (args[i] === '--all') {
    startId = 1;
    endId = 90;
  }
}

if (!fs.existsSync(manifestFile)) {
  console.error(`Manifeste introuvable : ${manifestFile}`);
  process.exit(1);
}

const manifest = JSON.parse(fs.readFileSync(manifestFile, 'utf8'));

console.log('=====================================================');
console.log('🎬 MOTEUR DE PRODUCTION VIDÉO TIKTOK WAZAP 1080×1920');
console.log(`   Plage ciblée : Vidéos ${startId} à ${endId} (Total : ${manifest.length})`);
console.log(`   Dossier de sortie : ${outputDir}`);
console.log('=====================================================\n');

function renderFrame(frameData, target, outPngPath) {
  const tempUserDir = path.join(os.tmpdir(), 'wazap_shot_' + Math.random().toString(36).substring(2));
  
  const queryParams = new URLSearchParams({
    target: target || 'commercant',
    badge: frameData.badge || '',
    badge_color: frameData.badge_color || '',
    hook: frameData.hook || '',
    subtitle: frameData.subtitle || '',
    points: frameData.points || '',
    special_title: frameData.special_title || '',
    special_desc: frameData.special_desc || '',
    special_badge: frameData.special_badge || '',
    special_color: frameData.special_color || '',
    cta: frameData.cta || '👉 LIEN DIRECT DANS LA BIO',
    prompt: frameData.prompt || 'Dis ton quartier en commentaire 👇'
  });

  const url = `file:///${htmlTemplate.replace(/\\/g, '/')}?${queryParams.toString()}`;

  const edgeArgs = [
    '--headless',
    '--disable-gpu',
    '--no-sandbox',
    '--no-first-run',
    '--hide-scrollbars',
    `--user-data-dir=${tempUserDir}`,
    '--window-size=1080,1920',
    '--force-device-scale-factor=1',
    `--screenshot=${outPngPath}`,
    url
  ];

  spawnSync(edgePath, edgeArgs, { stdio: 'pipe' });

  // Cleanup temp dir
  if (fs.existsSync(tempUserDir)) {
    try { fs.rmSync(tempUserDir, { recursive: true, force: true }); } catch {}
  }

  return fs.existsSync(outPngPath);
}

function compileVideo(f1, f2, f3, outMp4Path) {
  // 3 images loopées pendant 5 secondes chacune = 15 secondes total à 30 fps
  const ffArgs = [
    '-y',
    '-loop', '1', '-t', '5', '-i', f1,
    '-loop', '1', '-t', '5', '-i', f2,
    '-loop', '1', '-t', '5', '-i', f3,
    '-filter_complex',
    '[0:v]scale=1080:1920,setsar=1[v0];[1:v]scale=1080:1920,setsar=1[v1];[2:v]scale=1080:1920,setsar=1[v2];[v0][v1][v2]concat=n=3:v=1:a=0,format=yuv420p[outv]',
    '-map', '[outv]',
    '-c:v', 'libx264',
    '-preset', 'fast',
    '-crf', '22',
    '-pix_fmt', 'yuv420p',
    '-r', '30',
    '-movflags', '+faststart',
    outMp4Path
  ];

  const res = spawnSync('ffmpeg', ffArgs, { stdio: 'pipe' });
  return res.status === 0 && fs.existsSync(outMp4Path);
}

let generatedCount = 0;
let skippedCount = 0;

for (const item of manifest) {
  if (item.id < startId || item.id > endId) continue;

  const paddedId = String(item.id).padStart(2, '0');
  const outMp4 = path.join(outputDir, `wazap_tiktok_j${paddedId}.mp4`);

  if (!force && fs.existsSync(outMp4)) {
    console.log(`[SKIP] Vidéo ${paddedId} déjà existante : ${path.basename(outMp4)}`);
    skippedCount++;
    continue;
  }

  process.stdout.write(`⚙️  Génération Vidéo ${paddedId}/90 (${item.target.toUpperCase()} - J${item.jour} ${item.slot})... `);

  const f1 = path.join(tempFramesDir, `j${paddedId}_f1.png`);
  const f2 = path.join(tempFramesDir, `j${paddedId}_f2.png`);
  const f3 = path.join(tempFramesDir, `j${paddedId}_f3.png`);

  const ok1 = renderFrame(item.frame1, item.target, f1);
  const ok2 = renderFrame(item.frame2, item.target, f2);
  const ok3 = renderFrame(item.frame3, item.target, f3);

  if (!ok1 || !ok2 || !ok3) {
    console.log(`❌ Échec rendu des frames pour Vidéo ${paddedId}`);
    continue;
  }

  const okVideo = compileVideo(f1, f2, f3, outMp4);

  // Clean temp frames
  try {
    if (fs.existsSync(f1)) fs.unlinkSync(f1);
    if (fs.existsSync(f2)) fs.unlinkSync(f2);
    if (fs.existsSync(f3)) fs.unlinkSync(f3);
  } catch {}

  if (okVideo) {
    const stats = fs.statSync(outMp4);
    const sizeMb = (stats.size / (1024 * 1024)).toFixed(2);
    console.log(`✅ OK (${sizeMb} Mo)`);
    generatedCount++;
  } else {
    console.log(`❌ Échec compilation MP4 ffmpeg`);
  }
}

// Clean temp directory
try { fs.rmSync(tempFramesDir, { recursive: true, force: true }); } catch {}

// Generate CSV Schedule
console.log('\n📊 Génération du calendrier de programmation CSV...');
const csvHeader = '"ID","Jour","Slot","Cible","Fichier_Video","Hook","Legende","Commentaire_Epingle","Statut"\n';
const csvRows = manifest.map(v => {
  const paddedId = String(v.id).padStart(2, '0');
  const vidFile = `generated/wazap_tiktok_j${paddedId}.mp4`;
  const cleanHook = (v.hook || '').replace(/"/g, '""');
  const cleanCaption = (v.caption || '').replace(/"/g, '""');
  const cleanComment = (v.comment || '').replace(/"/g, '""');
  return `"${v.id}","${v.jour}","${v.slot}","${v.target}","${vidFile}","${cleanHook}","${cleanCaption}","${cleanComment}","Prêt à programmer"`;
}).join('\n');

fs.writeFileSync(csvFile, '\uFEFF' + csvHeader + csvRows, 'utf8');
console.log(`✅ Fichier CSV créé : ${csvFile}`);

console.log('\n=====================================================');
console.log(`🏁 BILAN : ${generatedCount} vidéo(s) générée(s), ${skippedCount} ignorée(s).`);
console.log('=====================================================');
