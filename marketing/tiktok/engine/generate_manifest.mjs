import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const outFile = path.resolve(__dirname, '..', 'manifest_tiktok_90jours.json');

const communes = [
  { nom: 'Cocody', secteurs: 'Angré, Riviera, Deux Plateaux, Saint-Jean', punch: 'Les clientes de Cocody veulent leur colis avant 17h.' },
  { nom: 'Yopougon', secteurs: 'Siporex, Bel Air, Niangon, Maroc', punch: 'À Yopougon, la marchandise doit arriver vite sans palabres.' },
  { nom: 'Marcory', secteurs: 'Zone 4, Anoumabo, Résidentiel', punch: 'Les restaurants et snacks de Zone 4 livrent à pleine cadence.' },
  { nom: 'Adjamé', secteurs: 'Marché Gouro, Forum, Dallas', punch: 'Le coeur du commerce de gros d’Abidjan a enfin ses livreurs connectés.' },
  { nom: 'Koumassi', secteurs: 'Remblais, Inch’Allah, Grand Campement', punch: 'Koumassi bouge, les colis partent en un clin d’oeil.' },
  { nom: 'Treichville', secteurs: 'Avenue 16, Arras, Rue 12', punch: 'Du carrefour Solibra au port, zéro retard de livraison.' },
  { nom: 'Port-Bouët', secteurs: 'Vridi, Derrière Wharf, Jean Folly', punch: 'Même en bord de mer, ton commerce livre tout Abidjan.' },
  { nom: 'Abobo', secteurs: 'Mairie, Samaké, PK 18', punch: 'Le secteur le plus peuplé d’Abidjan a ses coursiers dédiés.' },
  { nom: 'Plateau', secteurs: 'Centre des affaires, Immeubles', punch: 'Les déjeuners et dossiers livrés en express au bureau.' },
  { nom: 'Bingerville', secteurs: 'Feh Kessé, Nouveau Quartier', punch: 'Fini le refus "c’est trop loin" : WAZAP couvre Bingerville.' }
];

const titlesCom = [
  'Combien de clients tu [perds chaque semaine] faute de livreur ?',
  'Tes clients veulent commander... et [personne ne livre] ?',
  'Comment expédier tes colis [sans bouger] de ta boutique ?',
  'Arrête de payer des [abonnements de livraison] inutiles !',
  'Le secret des boutiques qui [livrent en 45 min] à Abidjan ⚡',
  '15 premières livraisons [OFFERTES] pour ton commerce 🎁'
];

const titlesRider = [
  'Tu as une moto ? Fais [5 000 F à 10 000 F] par après-midi 💰',
  'Marre d’attendre au bord de la route [sans savoir si ça va mordre] ?',
  'Sois ton propre patron : [choisis tes heures] et tes courses 🛵',
  'Programme Ambassadeur : [Un smartphone offert] à nos livreurs 📱',
  'Gagne de l’argent [dans ton propre quartier] sans traverser les bouchons',
  'Pourquoi rouler pour rien quand les boutiques [t’appellent direct] ?'
];

const titlesTech = [
  'Fini le légendaire [Allô le livreur tu es où] ? 🗺️📍',
  'Le [Code Secret PIN] qui empêche les vols de colis à Abidjan 🔒',
  'Regarde le livreur [arriver en temps réel] sur la carte interactive ⚡',
  'La [Garantie Colis Sûr] : ton colis indemnisé à 100% en cas de souci'
];

const titlesHumour = [
  'Quand la cliente te dit [Je suis en bas] mais elle se maquille encore 💄😂',
  'Les [3 pires mensonges] des livreurs quand le colis a 2h de retard 🛵',
  'Commander du [garba chaud] et le voir arriver frais et croustillant 🍗🔥',
  'Le moment où le client te dit [J’ai seulement billet de 10 000 F] 💀'
];

const videos = [];

for (let i = 1; i <= 90; i++) {
  const jour = Math.ceil(i / 2);
  const isMatin = (i % 2 !== 0);
  const slot = isMatin ? 'matin (12h00)' : 'soir (19h00)';
  const mod = (i - 1) % 15;
  const comData = communes[(i - 1) % communes.length];

  let target = '';
  let badge = '';
  let badgeColor = '';
  let hook = '';
  let subtitle = '';
  let points1 = '';
  let points2 = '';
  let specialTitle = '';
  let specialDesc = '';
  let specialBadge = '';
  let specialColor = '';
  let caption = '';
  let comment = '';

  if ([0, 3, 6, 9, 12].includes(mod)) {
    // PILIER 1 : COMMERCANTS
    target = 'commercant';
    badge = '🏪 COMMERCES ABIDJAN';
    badgeColor = '';
    hook = titlesCom[i % titlesCom.length];
    subtitle = 'Active la livraison à la demande pour ta boutique en 30 secondes chrono.';
    points1 = 'Un client commande dans ta boutique|Un livreur proche arrive en 10 min|Le client suit le coursier sur la carte';
    points2 = 'Zéro abonnement mensuel|Paiement à l’usage dès 1 000 FCFA|Garantie Colis Sûr contre la perte';
    specialTitle = 'Offre Spéciale Vendeurs';
    specialDesc = '15 premières courses offertes à l’inscription';
    specialBadge = '15 OFFERTES';
    specialColor = '';
    caption = `${hook.replace(/\[(.*?)\]/g, '$1')}\n\nAvec WAZAP, tu trouves un livreur vérifié dans ton quartier en 1 clic. Zéro frais d’abonnement, tes 15 premières livraisons sont offertes pour tester !\n\n👉 Clique le lien dans la bio pour activer ton commerce en 30 secondes.\n\n#VenteEnLigne #CommerceAbidjan #BoutiqueAbidjan #AbidjanShopping #LivraisonAbidjan #WAZAP`;
    comment = 'Tu vends quoi dans ta boutique ? Vêtements, nourriture, mèches, beauté ? Dis-le en com 👇';

  } else if ([1, 4, 7, 10, 13].includes(mod)) {
    // PILIER 2 : LIVREURS
    target = 'livreur';
    badge = '🛵 RECRUTEMENT LIVREURS';
    badgeColor = 'gold';
    hook = titlesRider[i % titlesRider.length];
    subtitle = 'Reçois les courses des commerces de ton secteur directement sur ton téléphone.';
    points1 = 'Courses de ton quartier à proximité|Tu acceptes quand tu es disponible|Payé à chaque course sans retenue';
    points2 = 'Zéro frais d’inscription|Validation rapide de ta pièce d’identité|Bonus et cadeaux aux livreurs fidèles';
    specialTitle = 'Programme Ambassadeur WAZAP';
    specialDesc = '250 livraisons = 1 smartphone neuf offert';
    specialBadge = 'SMARTPHONE 🎁';
    specialColor = 'gold';
    caption = `${hook.replace(/\[(.*?)\]/g, '$1')}\n\nRejoins la flotte WAZAP : reçois les propositions de livraison proches de toi. Zéro commission abusive, liberté totale.\n\n👉 Postule gratuitement via le lien dans la bio !\n\n#LivreurAbidjan #MotoAbidjan #JobAbidjan #EmploiCI #GagnerDeLargent #Abidjan225 #WAZAP`;
    comment = 'Tu roules sur quelle moto et dans quel quartier ? Dis-le en com 🛵👇';

  } else if ([2, 8].includes(mod)) {
    // PILIER 3 : SECURITE & RADAR LIVE
    target = 'client';
    badge = '📍 SÉCURITÉ & RADAR LIVE';
    badgeColor = '';
    hook = titlesTech[i % titlesTech.length];
    subtitle = 'La première plateforme de livraison avec suivi VTC transparent à Abidjan.';
    points1 = 'Radar de distance et heure d’arrivée exacte|Code PIN secret à 4 chiffres exigé à la remise|Preuve photo déchiffrée dans l’application';
    points2 = 'Appel et chat direct avec le coursier|Pourboire Mobile Money Wave en 1 tap|Notation 5 étoiles du service';
    specialTitle = 'Garantie Colis Sûr';
    specialDesc = 'Vérification CNI & Sécurité anti-fraude';
    specialBadge = '100% SÛR';
    specialColor = '';
    caption = `${hook.replace(/\[(.*?)\]/g, '$1')}\n\nAvec WAZAP, chaque course est tracée sur la carte avec un code PIN secret. Le client sait où est son colis à la seconde près !\n\n👉 Découvre l’expérience en cliquant sur le lien en bio.\n\n#TechCI #AbidjanInnovation #SecuriteColis #LivraisonFiable #WAZAP`;
    comment = 'C’est quoi la pire excuse qu’un coursier t’a déjà sortie ? Raconte en com 😂👇';

  } else if ([5, 11].includes(mod)) {
    // PILIER 4 : COMMUNES D'ABIDJAN
    target = 'quartier';
    badge = `🗺️ FOCUS ${comData.nom.toUpperCase()}`;
    badgeColor = 'gold';
    hook = `Avis aux boutiques et livreurs de [${comData.nom}] ! 📍`;
    subtitle = `${comData.punch} (${comData.secteurs}).`;
    points1 = `Livraison express dans tout ${comData.nom}|Connexion directe entre boutiques et livreurs du coin|Évite les ponts et les bouchons d’Abidjan`;
    points2 = `15 premières courses offertes aux commerçants|Inscription prioritaire pour les livreurs locaux|Activation immédiate en ligne`;
    specialTitle = `Zone ${comData.nom}`;
    specialDesc = comData.secteurs;
    specialBadge = 'OUVERT ⚡';
    specialColor = 'gold';
    caption = `Avis à ${comData.nom} ! WAZAP connecte les commerces et les livreurs de votre commune pour livrer en moins d’une heure. ${comData.secteurs}.\n\n👉 15 livraisons offertes via le lien en bio !\n\n#${comData.nom} #Abidjan #QuartierAbidjan #Boutique${comData.nom} #CommerceCI #WAZAP`;
    comment = `Tu es dans quel coin de ${comData.nom} ? Dis-nous en commentaire 👇`;

  } else {
    // PILIER 5 : HUMOUR & BUZZ LOCAL
    target = 'humour';
    badge = '😂 RÉALITÉS D’ABIDJAN';
    badgeColor = 'red';
    hook = titlesHumour[i % titlesHumour.length];
    subtitle = 'À Abidjan, la livraison est un sport national... mais avec WAZAP c’est réglé !';
    points1 = 'Suivi en direct : tu vois s’il est vraiment en bas|Paiement Mobile Money Wave direct sans monnaie|Livreurs courtois et vérifiés';
    points2 = 'Plus de stress, plus d’excuses bidons|Colis intact, livraison rapide|Satisfaction garantie';
    specialTitle = 'La Révolution WAZAP';
    specialDesc = 'La livraison moderne sans maux de tête';
    specialBadge = 'WAZAP ⚡';
    specialColor = '';
    caption = `${hook.replace(/\[(.*?)\]/g, '$1')}\n\nFini les galères de livraison à Babi. Avec WAZAP, tu suis le livreur sur la carte et tout est carré.\n\n👉 Rejoins le mouvement via le lien en bio !\n\n#HumourIvoirien #AbidjanRires #Babi225 #CoteDIvoireComedy #WAZAP`;
    comment = 'Ça t’est déjà arrivé ? Raconte ton expérience la plus drôle en com 👇';
  }

  videos.push({
    id: i,
    jour,
    slot,
    target,
    badge,
    badge_color: badgeColor,
    hook,
    subtitle,
    frame1: {
      badge,
      badge_color: badgeColor,
      hook,
      subtitle,
      points: '',
      special_title: '',
      cta: '👉 SWIPE POUR VOIR LA SUITE',
      prompt: 'Regarde jusqu’à la fin ⚡'
    },
    frame2: {
      badge,
      badge_color: badgeColor,
      hook: 'Comment ça marche avec [WAZAP] ?',
      subtitle: 'La solution simple et rapide pour toute la ville d’Abidjan.',
      points: points1,
      special_title: '',
      cta: '👉 DÉCOUVRE L’OFFRE EN BIO',
      prompt: 'Dis ton quartier en commentaire 👇'
    },
    frame3: {
      badge,
      badge_color: badgeColor,
      hook: 'Profite de [l’offre de lancement] !',
      subtitle: 'Rejoins les centaines de boutiques et coursiers actifs.',
      points: points2,
      special_title: specialTitle,
      special_desc: specialDesc,
      special_badge: specialBadge,
      special_color: specialColor,
      cta: '👉 LIEN DIRECT DANS LA BIO',
      prompt: 'Dis ton quartier en commentaire, on répond vite 👇'
    },
    caption,
    comment
  });
}

fs.writeFileSync(outFile, JSON.stringify(videos, null, 2), 'utf8');
console.log(`Manifeste généré avec succès : ${outFile} (${videos.length} vidéos).`);
