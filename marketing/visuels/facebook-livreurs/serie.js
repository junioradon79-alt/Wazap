// WAZAP - Serie "recrutement livreurs" pour Facebook (1 visuel/jour, format teasing).
// Chargé par post.html. Usage :
//   post.html?j=3
//   post.html?j=4&t=<titre html>&s=<sous-titre>&b=<badge>&p=<pill1,pill2,pill3>&f=phone|moto|both&c=<cta>
// Les jours 1..7 sont décrits ci-dessous (texte par défaut, surchargeable par l'URL).

const SERIE = {
  1: {
    badge: 'TEASER · JOUR 1',
    tag: '🛵 On recrute à Abidjan',
    h1: 'Quelque chose arrive<br>pour <span class="g">les livreurs.</span>',
    sub: 'WAZAP prépare une offre que <b>personne n\'a encore osé</b> à Abidjan. Un indice : ça se passe dans ton quartier. 👀',
    pills: ['👀 Bientôt', '🆓 Gratuit', '💬 WhatsApp uniquement'],
    focus: 'both',
    cta: 'Inscris-toi maintenant et sois prêt.'
  },
  2: {
    badge: '🎁 JOUR 2 · LA RÉVÉLATION',
    tag: '🎁 Offre livreurs 2026',
    h1: 'Un smartphone.<br><span class="g">Offert. Vraiment.</span>',
    sub: 'Pas de tirage, pas de hasard : remplis <b>3 conditions</b> et le téléphone est à toi. Et ce n\'est pas tout… 🏍️',
    pills: ['📱 1 smartphone', '✅ 3 conditions', '🔁 Systématique'],
    focus: 'phone',
    cta: 'Le smartphone t\'attend. Inscris-toi.'
  },
  3: {
    badge: '🏍️ JOUR 3 · LE BONUS',
    tag: '🏍️ Tirage trimestriel',
    h1: 'Et une MOTO<br><span class="g">à gagner.</span>',
    sub: 'Chaque trimestre, WAZAP tire au sort une <b>moto neuve</b> parmi les livreurs. Plus tu livres, plus tu as de tickets. 🎟️',
    pills: ['🏍️ 1 moto / trimestre', '🎟️ 1000 livraisons', '🛵 Réservé livreurs'],
    focus: 'moto',
    cta: 'Livreur WAZAP = ticket pour la moto.'
  },
  4: {
    badge: '✅ JOUR 4 · COMMENT GAGNER',
    tag: '🎁 Recette du smartphone',
    h1: '3 conditions.<br><span class="g">C\'est tout.</span>',
    sub: '1️⃣ Devenir livreur <b>certifié</b><br>2️⃣ Réaliser <b>250 livraisons</b> WAZAP<br>3️⃣ Parrainer <b>5 livreurs</b> actifs → ton smartphone, automatiquement. 📱',
    pills: ['1️⃣ Enrôlé + CNI', '2️⃣ 250 livraisons', '3️⃣ 5 filleuls'],
    focus: 'phone',
    cta: '3 conditions. 1 smartphone. À toi de jouer.'
  },
  5: {
    badge: '🎟️ JOUR 5 · LA MOTO',
    tag: '🏍️ Tirage trimestriel',
    h1: '1000 livraisons =<br><span class="g">ta moto ?</span>',
    sub: 'Le <b>tirage trimestriel</b> WAZAP : 1000 livraisons = 1 ticket garanti pour la moto. Les meilleurs livreurs partent avec une longueur d\'avance. 🏍️🔥',
    pills: ['🎟️ Tirage trimestriel', '🏍️ Moto neuve', '⚡ Multiplicateur de tickets'],
    focus: 'moto',
    cta: 'Plus tu livres, plus tu gagnes.'
  },
  6: {
    badge: '📣 JOUR 6 · ÇA BOUGE',
    tag: '📍 Abidjan — toute la ville',
    h1: 'Les inscriptions<br><span class="g">pleuvent déjà.</span>',
    sub: 'Des livreurs de tout Abidjan rejoignent WAZAP cette semaine. Le smartphone 🎁, la moto 🏍️ — et ton quartier qui t\'attend. 🛵',
    pills: ['🛵 Toute la ville', '⏱️ Inscription en 2 min', '🆓 100 % gratuit'],
    focus: 'both',
    cta: 'Rejoins la vague. Inscris-toi.'
  },
  7: {
    badge: '🔥 JOUR 7 · DERNIER RAPPEL',
    tag: '⏳ Il ne manque que toi',
    h1: 'Il ne manque<br><span class="g">plus que TOI.</span>',
    sub: 'Scanne le <b>QR code</b>, écris <b>« je veux livrer »</b> sur WhatsApp, et commence à gagner dès aujourd\'hui. Le smartphone t\'attend. 🎁🏍️',
    pills: ['🎁 Smartphone offert', '🏍️ Moto au trimestre', '🛵 Payé par course'],
    focus: 'both',
    cta: 'Scanne et commence en 2 minutes.'
  }
};

function pillClass(text) {
  if (text.indexOf('\uD83C\uDFCD') >= 0) return 'pill a';        // moto => ambre
  if (text.indexOf('\uD83D\uDCF1') >= 0) return 'pill g';        // smartphone => vert
  if (text.indexOf('\uD83C\uDF81') >= 0) return 'pill g';        // cadeau => vert
  return 'pill';
}

function applyDay(q) {
  const j = parseInt(q.get('j') || '1', 10);
  const d = SERIE[j] || SERIE[1];

  document.getElementById('badge').textContent = q.get('b') || d.badge;
  document.getElementById('tag').textContent = q.get('tag') || d.tag;
  document.getElementById('h1').innerHTML = q.get('t') || d.h1;
  document.getElementById('sub').innerHTML = q.get('s') || d.sub;

  const pills = (q.get('p') || '').split(',').filter(Boolean);
  const list = pills.length ? pills : d.pills;
  document.getElementById('pills').innerHTML = list
    .map(function (p) { return '<span class="' + (pills.length ? 'pill' : pillClass(p)) + '">' + p + '</span>'; })
    .join('');

  const focus = q.get('f') || d.focus || 'both';
  document.getElementById('prizes').className = 'prizes f-' + focus;

  document.getElementById('ctaTxt').textContent = q.get('c') || d.cta;
  document.getElementById('foot1').innerHTML = '<b>⚡ WAZAP</b> — Recrutement livreurs · Jour ' + j + '/7';
}
