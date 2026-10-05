import http from 'http';
import fs from 'fs';
import path from 'path';
import { spawn, spawnSync } from 'child_process';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const PORT = 8421;
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

const htmlFile = path.join(__dirname, 'wazap_motion_design.html');
const logoFile = path.resolve(__dirname, '..', 'visuels', 'logo_officiel_transparent.png');
const qrFile = path.resolve(__dirname, '..', 'programmation-60jours', 'assets', 'qr_universel_wazap.png');
const audioFile = path.join(__dirname, 'audio', 'master_soundtrack_20s.aac');
const tempVideoPath = path.join(__dirname, 'temp_visual_track.mp4');
const finalVideoPath = path.join(__dirname, 'wazap_motion_design_20s_tiktok.mp4');

console.log('===============================================================');
console.log('🎬 RENDU MOTION DESIGN OFFICIEL WAZAP — 20s TIKTOK (1080x1920)');
console.log('   Navigateur : ' + browserPath);
console.log('   Audio Master : ' + audioFile);
console.log('   Sortie Finale : ' + finalVideoPath);
console.log('===============================================================\n');

// 1. Démarrer le processus FFmpeg pour encoder le flux vidéo
const ffmpegVideo = spawn('ffmpeg', [
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
], {
  stdio: ['pipe', 'inherit', 'inherit']
});

let edgeProc = null;
let receivedFrames = 0;
const TOTAL_FRAMES = 600;

// 2. Serveur HTTP pour orchestrer le rendu fluide
const server = http.createServer((req, res) => {
  const url = new URL(req.url, `http://127.0.0.1:${PORT}`);
  
  if (url.pathname === '/' || url.pathname === '/wazap_motion_design.html') {
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    res.end(fs.readFileSync(htmlFile));
  } else if (url.pathname.includes('logo_officiel_transparent.png')) {
    res.writeHead(200, { 'Content-Type': 'image/png' });
    res.end(fs.readFileSync(logoFile));
  } else if (url.pathname.includes('qr_universel_wazap.png')) {
    res.writeHead(200, { 'Content-Type': 'image/png' });
    res.end(fs.readFileSync(qrFile));
  } else if (url.pathname.includes('master_soundtrack_20s.aac')) {
    res.writeHead(200, { 'Content-Type': 'audio/aac' });
    res.end(fs.readFileSync(audioFile));
  } else if (url.pathname === '/upload-frame') {
    let chunks = [];
    req.on('data', chunk => chunks.push(chunk));
    req.on('end', () => {
      const buffer = Buffer.concat(chunks);
      ffmpegVideo.stdin.write(buffer);
      receivedFrames++;
      if (receivedFrames % 60 === 0 || receivedFrames === TOTAL_FRAMES) {
        const sec = (receivedFrames / 30).toFixed(1);
        process.stdout.write(`\r⚙️  Rendu en cours : ${receivedFrames}/${TOTAL_FRAMES} frames (${sec}s / 20.0s)...`);
      }
      res.writeHead(200);
      res.end('ok');
    });
  } else if (url.pathname === '/render-complete') {
    console.log(`\n✅ Toutes les ${receivedFrames} frames ont été capturées avec succès !`);
    res.writeHead(200);
    res.end('ok');

    // Fermer l'entrée stdin de FFmpeg
    ffmpegVideo.stdin.end();

    // Fermer le navigateur
    if (edgeProc) {
      try { edgeProc.kill(); } catch {}
    }
  } else {
    res.writeHead(404);
    res.end();
  }
});

server.listen(PORT, () => {
  console.log(`🚀 Serveur de capture démarré sur http://127.0.0.1:${PORT}`);
  console.log(`🎬 Lancement de Headless Edge en résolution 1080x1920...`);

  edgeProc = spawn(browserPath, [
    '--headless',
    '--disable-gpu',
    '--no-sandbox',
    '--no-first-run',
    '--hide-scrollbars',
    '--window-size=1080,1920',
    `http://127.0.0.1:${PORT}/wazap_motion_design.html?headless=true`
  ]);
});

// 3. Quand le flux vidéo est compilé, fusionner avec l'audio master
ffmpegVideo.on('close', (code) => {
  console.log(`\n🎞️  Piste vidéo temporaire générée (code ${code}).`);
  server.close();

  if (!fs.existsSync(tempVideoPath)) {
    console.error("❌ Échec de génération de la piste vidéo !");
    process.exit(1);
  }

  console.log(`🎵 Muxing de la piste vidéo avec la bande son master (+225 05 44 05 19 72 / Afrobeat)...`);

  const muxRes = spawnSync('ffmpeg', [
    '-y',
    '-i', tempVideoPath,
    '-i', audioFile,
    '-c:v', 'copy',
    '-c:a', 'copy',
    '-shortest',
    finalVideoPath
  ], { stdio: 'inherit' });

  // Supprimer la piste vidéo intermédiaire
  try { fs.unlinkSync(tempVideoPath); } catch {}

  if (fs.existsSync(finalVideoPath)) {
    const stats = fs.statSync(finalVideoPath);
    const sizeMb = (stats.size / (1024 * 1024)).toFixed(2);
    console.log('\n===============================================================');
    console.log(`🎉 VIDÉO MOTION DESIGN WAZAP 20s TIKTOK GÉNÉRÉE AVEC SUCCÈS !`);
    console.log(`📁 Fichier : ${finalVideoPath}`);
    console.log(`⚖️ Poids : ${sizeMb} Mo (Optimisé pour TikTok 1080x1920 30fps)`);
    console.log('===============================================================\n');
    process.exit(0);
  } else {
    console.error("❌ Échec du muxing final.");
    process.exit(1);
  }
});
