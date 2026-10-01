import fs from 'fs';
import path from 'path';
import { spawnSync } from 'child_process';
import { fileURLToPath } from 'url';
import os from 'os';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Paths
const edgePath = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const episodesFile = path.resolve(__dirname, '..', 'episodes', 'saison1_episodes.json');
const htmlTemplate = path.resolve(__dirname, 'render_story_scene.html');
const outputDir = path.resolve(__dirname, '..', 'generated');
const tempFramesDir = path.resolve(__dirname, 'temp_frames');
const audioTrack = path.resolve(__dirname, '..', 'audio', 'music_track.aac');

// Create directories
fs.mkdirSync(outputDir, { recursive: true });
fs.mkdirSync(tempFramesDir, { recursive: true });

if (!fs.existsSync(episodesFile)) {
  console.error(`Fichier episodes introuvable : ${episodesFile}`);
  process.exit(1);
}

const episodes = JSON.parse(fs.readFileSync(episodesFile, 'utf8'));

// Parse CLI args
const args = process.argv.slice(2);
let startEp = 1;
let endEp = 1;
let modeAll = false;

for (let i = 0; i < args.length; i++) {
  if (args[i] === '--ep' && args[i + 1]) {
    const epNum = parseInt(args[++i], 10);
    startEp = epNum;
    endEp = epNum;
  } else if (args[i] === '--start' && args[i + 1]) {
    startEp = parseInt(args[++i], 10);
  } else if (args[i] === '--end' && args[i + 1]) {
    endEp = parseInt(args[++i], 10);
  } else if (args[i] === '--all') {
    modeAll = true;
  }
}

if (modeAll) {
  startEp = 1;
  endEp = episodes.length;
}

const episodesToRun = episodes.filter(e => e.number >= startEp && e.number <= endEp);

console.log('===============================================================');
console.log('🎬 MOTEUR STORYTELLING TIKTOK WAZAP : SAISON 1');
console.log(`   Nombre d'épisodes à générer : ${episodesToRun.length} (Ép. ${startEp} à ${endEp})`);
console.log(`   Dossier de sortie : ${outputDir}`);
console.log('===============================================================\n');

function renderScene(scene, episodeNum, outPngPath) {
  const tempUserDir = path.join(os.tmpdir(), 'wazap_story_' + Math.random().toString(36).substring(2));
  
  // Resolve absolute path to background image
  const absBgPath = path.resolve(__dirname, '..', scene.bgImage);
  const bgUrl = `file:///${absBgPath.replace(/\\/g, '/')}`;

  const queryParams = new URLSearchParams({
    bgImage: bgUrl,
    seriesTitle: scene.seriesTitle || '',
    isSuccess: scene.isSuccess ? 'true' : 'false',
    hookBadge: scene.hookBadge || '',
    hookBadgeColor: scene.hookBadgeColor || '',
    hookType: scene.hookType || 'neutral',
    hookText: scene.hookText || '',
    captionLabel: scene.captionLabel || '',
    captionText: scene.captionText || '',
    showCta: scene.showCta ? 'true' : 'false'
  });

  if (scene.bubble) {
    queryParams.set('bubbleMessage', scene.bubble.message || '');
    queryParams.set('bubbleSender', scene.bubble.sender || '');
    queryParams.set('bubbleTime', scene.bubble.time || '11:42');
    queryParams.set('bubbleDir', scene.bubble.direction || 'incoming');
  }

  if (scene.benefits && scene.benefits.length > 0) {
    queryParams.set('benefits', JSON.stringify(scene.benefits));
  }

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

function processEpisode(episode) {
  console.log(`\n---------------------------------------------------------------`);
  console.log(`▶️ ÉPISODE ${episode.number} : "${episode.title}" (${episode.durationSeconds}s)`);
  console.log(`---------------------------------------------------------------`);

  // 1. Render all scene frames
  const frameFiles = [];
  for (let i = 0; i < episode.scenes.length; i++) {
    const scene = episode.scenes[i];
    const framePath = path.join(tempFramesDir, `ep${String(episode.number).padStart(2, '0')}_s${i + 1}.png`);
    process.stdout.write(`   📸 Scène ${i + 1}/${episode.scenes.length} (${scene.captionLabel})... `);
    
    const ok = renderScene(scene, episode.number, framePath);
    if (ok) {
      console.log('OK');
      frameFiles.push({ path: framePath, duration: scene.duration });
    } else {
      console.log('ERREUR');
      return false;
    }
  }

  // 2. Compile with FFmpeg (Audio + Visuals + Faststart)
  const outMp4Path = path.join(outputDir, `wazap_story_ep${String(episode.number).padStart(2, '0')}.mp4`);
  process.stdout.write(`   🎞️ Encodage MP4 cinématique... `);

  const ffInputs = [];
  const filterSegments = [];

  for (let i = 0; i < frameFiles.length; i++) {
    const f = frameFiles[i];
    ffInputs.push('-loop', '1', '-t', String(f.duration), '-i', f.path);
    filterSegments.push(`[${i}:v]scale=1080:1920,setsar=1[v${i}]`);
  }

  let filterConcat = '';
  for (let i = 0; i < frameFiles.length; i++) {
    filterConcat += `[v${i}]`;
  }
  filterConcat += `concat=n=${frameFiles.length}:v=1:a=0,format=yuv420p[outv]`;

  const totalDuration = frameFiles.reduce((acc, f) => acc + f.duration, 0);

  const hasAudio = fs.existsSync(audioTrack);
  if (hasAudio) {
    ffInputs.push('-stream_loop', '-1', '-i', audioTrack);
  }

  const fullFilter = filterSegments.join(';') + ';' + filterConcat;

  const ffArgs = [
    '-y',
    ...ffInputs,
    '-filter_complex', fullFilter,
    '-map', '[outv]',
  ];

  if (hasAudio) {
    ffArgs.push(
      '-map', `${frameFiles.length}:a`,
      '-af', `afade=t=in:st=0:d=1,afade=t=out:st=${totalDuration - 2}:d=2`,
      '-c:a', 'aac',
      '-b:a', '160k'
    );
  }

  ffArgs.push(
    '-t', String(totalDuration),
    '-c:v', 'libx264',
    '-preset', 'fast',
    '-crf', '22',
    '-pix_fmt', 'yuv420p',
    '-r', '30',
    '-movflags', '+faststart',
    outMp4Path
  );

  const res = spawnSync('ffmpeg', ffArgs, { stdio: 'pipe' });

  if (fs.existsSync(outMp4Path)) {
    const stats = fs.statSync(outMp4Path);
    const sizeMb = (stats.size / (1024 * 1024)).toFixed(2);
    console.log(`OK (${sizeMb} Mo)`);
    return true;
  } else {
    console.log('ERREUR');
    console.error(res.stderr.toString());
    return false;
  }
}

// Run all selected episodes
let successCount = 0;
for (const ep of episodesToRun) {
  const success = processEpisode(ep);
  if (success) successCount++;
}

console.log('\n===============================================================');
console.log(`🎉 TOUTES LES COMPILATIONS SONT TERMINÉES !`);
console.log(`   Succès : ${successCount}/${episodesToRun.length} épisodes générés`);
console.log(`   Dossier : ${outputDir}`);
console.log('===============================================================');
