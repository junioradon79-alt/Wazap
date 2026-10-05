import http from 'http';
import fs from 'fs';
import path from 'path';
import { spawn, spawnSync } from 'child_process';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const PORT = 8901;
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

const htmlFile = path.join(__dirname, 'wazap_motion_design_engine_30s.html');
const logoFile = path.resolve(__dirname, '..', 'visuels', 'logo_officiel_transparent.png');
const qrFile = path.resolve(__dirname, '..', 'programmation-60jours', 'assets', 'qr_universel_wazap.png');
const assetsDir = path.resolve(__dirname, '..', 'programmation-60jours', 'assets');
const previewsDir = path.join(__dirname, 'previews');

if (!fs.existsSync(previewsDir)) fs.mkdirSync(previewsDir, { recursive: true });

// 1. ÉTAPE 1 : Génération des 6 pistes audio studio avec Python / edge-tts
console.log('===============================================================');
console.log('🎙️ ÉTAPE 1 : GÉNÉRATION DES 6 VOIX STUDIO & MIX AFROBEAT (30s)');
console.log('===============================================================\n');

const pyScript = path.join(__dirname, 'generate_6_audios.py');
const pyRes = spawnSync('python', [pyScript], { 
  stdio: 'inherit',
  env: { ...process.env, PYTHONIOENCODING: 'utf-8' }
});
if (pyRes.status !== 0) {
  console.error("❌ Erreur lors de la génération des pistes audio.");
  process.exit(1);
}

// Les 6 vidéos à produire
const VIDEOS = [
  { id: '01', name: 'commercant_antivol', label: 'Commerçant 1 • Sécurité QR Code Universel & Zéro Cash' },
  { id: '02', name: 'commercant_rapidite', label: 'Commerçant 2 • Livreur en 3 Minutes Chrono en 1 Tap' },
  { id: '03', name: 'commercant_colis_sur', label: 'Commerçant 3 • 100% WhatsApp & Scellé Colis Sûr' },
  { id: '04', name: 'livreur_zero_commission', label: 'Livreur 1 • 0% Commission (100% gains nets)' },
  { id: '05', name: 'livreur_securite_dignite', label: 'Livreur 2 • Zéro Cash & Statut Livreur Certifié' },
  { id: '06', name: 'livreur_proximite_smartphone', label: 'Livreur 3 • Proximité & 50 Smartphones Redmi 15C' }
];

console.log('\n===============================================================');
console.log('🎬 ÉTAPE 2 : PRODUCTION EN SÉRIE DES 6 VIDÉOS MOTION DESIGN');
console.log('   Résolution : 1080x1920 (9:16 Vertical Full HD)');
console.log('   Durée : 30.0s exactes (900 frames par vidéo à 30 fps)');
console.log('   Navigateur Headless : ' + browserPath);
console.log('===============================================================\n');

let currentVideoIdx = 0;
let ffmpegVideo = null;
let edgeProc = null;
let receivedFrames = 0;
const TOTAL_FRAMES = 900;
let tempVideoPath = '';
let currentVideo = null;

// Serveur HTTP local
const server = http.createServer((req, res) => {
  const url = new URL(req.url, `http://127.0.0.1:${PORT}`);

  if (url.pathname === '/' || url.pathname === '/wazap_motion_design_engine_30s.html') {
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    res.end(fs.readFileSync(htmlFile));
  } else if (url.pathname.includes('logo_officiel_transparent.png')) {
    res.writeHead(200, { 'Content-Type': 'image/png' });
    res.end(fs.readFileSync(logoFile));
  } else if (url.pathname.includes('qr_universel_wazap.png')) {
    res.writeHead(200, { 'Content-Type': 'image/png' });
    res.end(fs.readFileSync(qrFile));
  } else if (url.pathname.includes('/assets/')) {
    const filename = path.basename(url.pathname);
    const targetFile = path.join(assetsDir, filename);
    if (fs.existsSync(targetFile)) {
      res.writeHead(200, { 'Content-Type': filename.endsWith('.png') ? 'image/png' : 'image/jpeg' });
      res.end(fs.readFileSync(targetFile));
    } else {
      res.writeHead(404);
      res.end();
    }
  } else if (url.pathname === '/upload-frame') {
    let chunks = [];
    req.on('data', chunk => chunks.push(chunk));
    req.on('end', () => {
      if (ffmpegVideo && ffmpegVideo.stdin.writable) {
        ffmpegVideo.stdin.write(Buffer.concat(chunks));
        receivedFrames++;
        if (receivedFrames % 100 === 0 || receivedFrames === TOTAL_FRAMES) {
          const sec = (receivedFrames / 30).toFixed(1);
          process.stdout.write(`\r⚙️  [Vidéo ${currentVideo.id}/06] Rendu : ${receivedFrames}/${TOTAL_FRAMES} frames (${sec}s / 30.0s)...`);
        }
      }
      res.writeHead(200);
      res.end('ok');
    });
  } else if (url.pathname === '/render-complete') {
    console.log(`\n✅ [Vidéo ${currentVideo.id}/06] 900 frames capturées.`);
    res.writeHead(200);
    res.end('ok');

    if (ffmpegVideo) {
      ffmpegVideo.stdin.end();
    }
    if (edgeProc) {
      try { edgeProc.kill(); } catch {}
    }
  } else {
    res.writeHead(404);
    res.end();
  }
});

function runNextVideo() {
  if (currentVideoIdx >= VIDEOS.length) {
    console.log('\n===============================================================');
    console.log('🎉 TOUTES LES 6 VIDÉOS MOTION DESIGN 30s SONT ACHEVÉES !');
    console.log('===============================================================\n');
    server.close();
    process.exit(0);
    return;
  }

  currentVideo = VIDEOS[currentVideoIdx];
  receivedFrames = 0;
  tempVideoPath = path.join(__dirname, `temp_visual_${currentVideo.id}.mp4`);
  const finalMp4Path = path.join(__dirname, `wazap_motion_${currentVideo.id}_${currentVideo.name}_30s.mp4`);
  const audioTrackPath = path.join(__dirname, 'audio', `master_track_${currentVideo.id}_30s.aac`);

  console.log(`\n▶️  Lancement Vidéo ${currentVideo.id}/06 : ${currentVideo.label}`);

  ffmpegVideo = spawn('ffmpeg', [
    '-y',
    '-f', 'image2pipe',
    '-vcodec', 'mjpeg',
    '-framerate', '30',
    '-i', '-',
    '-c:v', 'libx264',
    '-preset', 'fast',
    '-crf', '19',
    '-pix_fmt', 'yuv420p',
    '-movflags', '+faststart',
    tempVideoPath
  ], { stdio: ['pipe', 'ignore', 'ignore'] });

  ffmpegVideo.on('close', (code) => {
    console.log(`🎞️  [Vidéo ${currentVideo.id}] Flux visuel encodé.`);

    if (!fs.existsSync(tempVideoPath)) {
      console.error(`❌ Échec pour vidéo ${currentVideo.id}`);
      currentVideoIdx++;
      runNextVideo();
      return;
    }

    console.log(`🎵 [Vidéo ${currentVideo.id}] Muxing avec master audio 30s...`);
    spawnSync('ffmpeg', [
      '-y',
      '-i', tempVideoPath,
      '-i', audioTrackPath,
      '-c:v', 'copy',
      '-c:a', 'copy',
      '-shortest',
      finalMp4Path
    ], { stdio: 'ignore' });

    try { fs.unlinkSync(tempVideoPath); } catch {}

    // Générer la vignette de prévisualisation à t=10s
    const thumbPath = path.join(previewsDir, `thumb_${currentVideo.id}_${currentVideo.name}.jpg`);
    spawnSync('ffmpeg', [
      '-y',
      '-ss', '00:00:10',
      '-i', finalMp4Path,
      '-vframes', '1',
      thumbPath
    ], { stdio: 'ignore' });

    // Synchronisation dans marketing/tiktok/videos/ et marketing/videos/
    const tiktokTarget = path.resolve(__dirname, '..', 'tiktok', 'videos', `wazap_motion_${currentVideo.id}_${currentVideo.name}_30s.mp4`);
    const generalTarget = path.resolve(__dirname, '..', 'videos', `wazap_motion_${currentVideo.id}_${currentVideo.name}_30s.mp4`);
    try {
      fs.copyFileSync(finalMp4Path, tiktokTarget);
      fs.copyFileSync(finalMp4Path, generalTarget);
    } catch {}

    const stats = fs.statSync(finalMp4Path);
    const sizeMb = (stats.size / (1024 * 1024)).toFixed(2);
    console.log(`✅ [Vidéo ${currentVideo.id}/06] TERMINÉE : ${path.basename(finalMp4Path)} (${sizeMb} Mo)`);

    currentVideoIdx++;
    runNextVideo();
  });

  // Lancement de Edge headless pour cette vidéo
  const targetUrl = `http://127.0.0.1:${PORT}/wazap_motion_design_engine_30s.html?headless=true&theme=${currentVideo.id}`;
  edgeProc = spawn(browserPath, [
    '--headless',
    '--disable-gpu',
    '--no-sandbox',
    '--no-first-run',
    '--hide-scrollbars',
    '--window-size=1080,1920',
    targetUrl
  ], { stdio: 'ignore' });
}

server.listen(PORT, () => {
  console.log(`🚀 Serveur actif sur port ${PORT}. Début de la chaîne de rendu...`);
  runNextVideo();
});
