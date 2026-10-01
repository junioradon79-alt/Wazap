import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const rootDir = path.resolve(__dirname, '..');

const catalog = JSON.parse(fs.readFileSync(path.join(__dirname, 'visuals_catalog.json'), 'utf8'));

// Modèles de contenu par pilier et créneau horaire
const copyTemplates = {
  securite: {
    matin: (char, visual) => ({
      hook: `🚨 WAZAP.CI - FINI LES LIVREURS QUI DISPARAISSENT AVEC VOTRE ARGENT !`,
      body: `Chaque matin à Abidjan, c'est la même angoisse pour les commerçants : confier des colis de valeur à un coursier inconnu et prier pour qu'il ne disparaisse pas avec la recette !\n\nSur WAZAP, la règle est limpide et inviolable : ZÉRO CASH SUR LA MARCHANDISE.\nÀ la remise du colis, votre client scanne simplement le QR Code Universel WAZAP (Wave, Orange Money, MTN, Moov, Carte). L'argent arrive instantanément sur VOTRE compte avant même que le livreur ne reparte.\n\n🛡️ Livreurs vérifiés CNI par IA Google Cloud Vision\n📍 Géolocalisation live sur la carte\n🎁 15 premières livraisons offertes sans commission WAZAP !`,
      cta: `👉 Prêt à expédier en toute sérénité ? Envoyez « COLIS » sur WhatsApp au 05 44 05 19 72 ou scannez le QR code sur le visuel.`,
      comment: `💬 Avez-vous déjà été victime d'un livreur qui a disparu avec votre recette de marchandise à Abidjan ? Partagez votre expérience en commentaire.`
    }),
    midi: (char, visual) => ({
      hook: `⚡ WAZAP.CI : LE LIVREUR NE TOUCHE PAS À UN SEUL FRANC DE VOTRE MARCHANDISE !`,
      body: `Rush de midi à Abidjan : votre client n'a pas la monnaie sur 10 000 F ou exige de payer à la livraison ?\n\nPas besoin d'annuler la vente ni d'envoyer le coursier chercher la monnaie au carrefour.\nGrâce au QR Code Universel WAZAP, le client règle le montant exact de votre marchandise au centime près par Mobile Money (Wave, Orange Money, MTN).\n\nLe livreur ne touche à aucun billet de votre marchandise : il ne perçoit que ses frais de course.\nRésultat : zéro risque de vol, zéro faux billet, encaissement immédiat dans votre trésorerie !`,
      cta: `📲 Pour tester gratuitement avec 15 courses offertes : envoyez « COLIS » sur WhatsApp au 05 44 05 19 72.`,
      comment: `🔒 Quel est votre moyen de paiement Mobile Money préféré pour encaisser vos commandes : Wave, Orange Money ou MTN ? Dites-le nous en commentaire !`
    }),
    soir: (char, visual) => ({
      hook: `🌙 18h30 à Abidjan : clôturez votre journée avec 100% de vos recettes en caisse et l'esprit tranquille.`,
      body: `Combien de fois avez-vous attendu le retour d'un livreur tard le soir pour récupérer vos sous, la peur au ventre qu'il ait coupé son téléphone ?\nAvec WAZAP, ce stress appartient définitivement au passé.\n\nChaque colis livré dans la journée a été payé en direct à la seconde même de la remise. Vous consultez votre tableau de bord marchand, toutes vos courses sont validées, et l'Assurance Colis Sûr a couvert chaque article.\n\nC'est ça, la nouvelle norme du commerce sécurisé à Abidjan.`,
      cta: `💼 Rejoignez plus de 1 000 commerçants satisfaits : envoyez « COLIS » sur WhatsApp au 05 44 05 19 72.`,
      comment: `✨ À quelle heure clôturez-vous habituellement vos comptes de livraison le soir ? Partagez vos réalités d'entrepreneur ci-dessous !`
    })
  },
  rapidite: {
    matin: (char, visual) => ({
      hook: `⏱️ 8h00 du matin : votre première commande urgente vient de tomber ? Ne paniquez plus !`,
      body: `Fini d'appeler 10 livreurs pour vous entendre dire « chef je suis trop loin » ou « y'a embouteillage ».\nSur WAZAP, vous validez votre besoin en 3 clics sur WhatsApp.\n\nNotre algorithme intelligent géolocalise et alerte les 5 livreurs disponibles les plus proches de votre boutique. En moins de 3 minutes, un coursier professionnel accepte et prend la route.\n\n⚡ Gain de temps prouvé : 40 minutes économisées par course\n📍 Suivi en direct sur la carte interactive\n🎁 Vos 15 premières courses sans aucun frais de service WAZAP !`,
      cta: `🛵 Envie d'un livreur disponible en 3 min ? Envoyez « COLIS » sur WhatsApp au 05 44 05 19 72.`,
      comment: `🚀 Quel est le délai de livraison le plus rapide que vous ayez promis à un client ? Racontez-nous !`
    }),
    midi: (char, visual) => ({
      hook: `🔥 12h30 : le rush des repas chauds et des commandes express est lancé à Abidjan !`,
      body: `Quand un client a faim ou attend un cadeau d'anniversaire, chaque minute de retard est fatale pour votre note de réputation.\n\nAvec les livreurs certifiés WAZAP, vos colis partent immédiatement. Pas d'attente, pas d'intermédiaire inutile.\nVotre client reçoit un lien de suivi en direct pour regarder la moto avancer rue par rue jusqu'à son bureau ou domicile.\n\nLe résultat ? Des clients fidélisés qui recommandent les yeux fermés !`,
      cta: `📦 Expédiez vos commandes express sans stress : envoyez « COLIS » sur WhatsApp au 05 44 05 19 72.`,
      comment: `🍲 Vos clients préfèrent-ils être livrés sur leur lieu de travail à midi ou à la maison le soir ? On attend vos retours en commentaire !`
    }),
    soir: (char, visual) => ({
      hook: `🚦 Embouteillages du soir sur le pont De Gaulle ou le boulevard Mitterrand ? WAZAP livre quand même !`,
      body: `À l'heure où tout Abidjan est bloqué dans les embouteillages, vos colis continuent d'arriver à destination.\nNos livreurs indépendants connaissent chaque raccourci, chaque ruelle de leur commune de rattachement.\n\nVous gardez les yeux sur la carte interactive sans devoir harceler le coursier au téléphone. Vous gagnez du temps, votre client est serein, et la transaction se conclut avec 5 étoiles !`,
      cta: `👉 Profitez de 15 livraisons offertes dès aujourd'hui : envoyez « COLIS » sur WhatsApp au 05 44 05 19 72.`,
      comment: `📍 Dans quelle commune d'Abidjan vos livraisons rencontrent-elles le plus de bouchons en fin de journée ?`
    })
  },
  valeur: {
    matin: (char, visual) => {
      if (char.includes('Bakary') || char.includes('Koffi')) {
        return {
          hook: `🛵 Livreurs d'Abidjan : commencez votre journée avec 0% de commission prélevée sur votre sueur !`,
          body: `Pourquoi continuer à donner 20% à 25% de vos revenus aux applications qui vous exploitent ?\nSur WAZAP, la règle est sacrée : 100% du prix de la course est pour VOUS.\n\n💰 Minimum 1 000 FCFA net dès le 1er mètre\n🛡️ 0 cash marchandise à transporter (sécurité maximale contre les agressions)\n📱 Grand Défi Trimestriel : 50 smartphones Xiaomi Redmi 15C neufs offerts aux coursiers réguliers !\nTout se passe sur WhatsApp Business sans application lourde qui décharge votre téléphone.`,
          cta: `🔥 Rejoignez le réseau des livreurs gagnants : envoyez « DISPO » sur WhatsApp au 05 44 05 19 72 et commencez en 2 minutes !`,
          comment: `💪 Combien de courses faites-vous en moyenne par jour à Abidjan ? Dites-le nous en commentaire !`
        };
      }
      return {
        hook: `💎 Commerçants : et si vos 15 prochaines courses ne vous coûtaient STRICTEMENT RIEN en commission ?`,
        body: `Les plateformes classiques vous prennent jusqu'à 30% sur vos articles ou vous imposent des abonnements mensuels ruineux.\nWAZAP casse les codes : nous vous offrons 15 recherches de livreurs sans aucun frais de mise en relation.\n\nVous ne payez que le coursier indépendant à son juste tarif (1 000 à 2 000 FCFA selon la distance), et WAZAP prend 0 FCFA de commission.\nC'est notre façon de vous prouver l'efficacité de notre technologie sans que vous n'ayez à sortir un franc de votre poche.`,
        cta: `🎁 Activez votre Pack 15 Courses Offertes en envoyant « COLIS » sur WhatsApp au 05 44 05 19 72.`,
        comment: `📢 Quel pourcentage de marge perdez-vous habituellement avec les autres solutions de livraison ? Témoignez ci-dessous !`
      };
    },
    midi: (char, visual) => {
      if (char.includes('Bakary') || char.includes('Koffi')) {
        return {
          hook: `🛵 Pourquoi les meilleurs livreurs d'Abidjan ont tous rejoint WAZAP ? La réponse en 3 chiffres !`,
          body: `1️⃣ 0 FCFA : c'est la commission que WAZAP te prélève sur tes courses.\n2️⃣ 1 000 FCFA : c'est le plancher garanti net dès la première course dans la même commune.\n3️⃣ 50 Smartphones Redmi 15C : offerts tous les 3 mois aux motards actifs du réseau !\n\nEn plus, tu ne transportes pas l'argent du commerçant : le client paie directement par QR Code Universel. Zéro risque de vol ou d'accusation de monnaie manquante !`,
          cta: `📲 Envoie « DISPO » dès maintenant sur WhatsApp au 05 44 05 19 72 et reçois tes premières alertes !`,
          comment: `🏆 Qui veut son smartphone Redmi 15C neuf ce trimestre ? Écrivez « MOI » en commentaire !`
        };
      }
      return {
        hook: `🏪 Cybervendeuses de Cocody, Marcory, Yopougon : votre boutique mérite un service VIP sans abonnement !`,
        body: `Vous passez des heures à concevoir vos produits, soigner vos photos et répondre aux clients.\nNe gâchez pas tout au moment de la livraison avec un service médiocre.\n\nAvec WAZAP, bénéficiez de :\n✅ Livreurs souriants, polis et vêtus de leur chasuble officielle\n✅ Assurance Colis Sûr protégeant chaque envoi\n✅ Encaissement direct Mobile Money à la porte du client\n✅ 15 courses offertes pour démarrer sans risque.`,
        cta: `🚀 Rejoignez le cercle des cybervendeuses sereines : envoyez « COLIS » sur WhatsApp au 05 44 05 19 72.`,
        comment: `❤️ Quelle est votre plus grande fierté dans votre boutique aujourd'hui ? Dites-le nous avec votre lien de page !`
      };
    },
    soir: (char, visual) => {
      if (char.includes('Bakary') || char.includes('Koffi')) {
        return {
          hook: `🌙 Fin de journée pour les livreurs WAZAP : le compte est clair, l'argent est 100% dans la poche !`,
          body: `8 courses faites aujourd'hui = 8 000 à 12 000 FCFA nets gagnés sans déduction, sans prélèvement arbitraire.\nPas de commission à reverser en fin de semaine, pas de compte bloqué sans explication.\n\nTu es libre de rouler quand tu veux : tape « DISPO » pour être visible, tape « INDISPO » quand tu rentres te reposer auprès de ta famille.\nC'est la liberté et le respect du travailleur ivoirien.`,
          cta: `🛵 Deviens coursier officiel WAZAP : envoie « DISPO » sur WhatsApp au 05 44 05 19 72.`,
          comment: `🤝 Quelle commune d'Abidjan a été la plus rentable pour vous aujourd'hui ? Échangeons entre collègues !`
        };
      }
      return {
        hook: `⚖️ Le match est plié : l'ancienne méthode de livraison face à la révolution WAZAP à Abidjan !`,
        body: `Ancienne méthode :\n❌ Livreur injoignable après 45 min\n❌ Manipulation d'espèces et risque de vol\n❌ Discontinuité des colis et zéro recours en cas de casse\n\nRévolution WAZAP :\n✅ Livreur en 3 minutes chrono\n✅ Zéro cash marchandise : virement direct par QR Code Universel\n✅ Assurance Colis Sûr incluse\n✅ 15 courses offertes sans engagement !`,
        cta: `👉 Le choix est évident : envoyez « COLIS » sur WhatsApp au 05 44 05 19 72 et testez dès demain matin.`,
        comment: `🎯 Prêt à moderniser vos livraisons pour les 60 prochains jours ? Tapez OUI en commentaire !`
      };
    }
  }
};

const slots = [
  { time: '08:00', label: 'matin', name: 'Post 1 — Matin (08h00)' },
  { time: '12:30', label: 'midi', name: 'Post 2 — Midi (12h30)' },
  { time: '18:30', label: 'soir', name: 'Post 3 — Soir (18h30)' }
];

const hashtags = `#Wazap #LivraisonAbidjan #EcommerceCIV #VenteEnLigneCIV #Team225 #AbidjanBusiness #Cocody #Marcory #Yopougon #Plateau`;

const totalDays = 60;
const schedule = [];

const startDate = new Date(2026, 9, 1); // 01 Octobre 2026

for (let day = 1; day <= totalDays; day++) {
  const currentDate = new Date(startDate.getTime() + (day - 1) * 24 * 60 * 60 * 1000);
  const dateStr = currentDate.toISOString().split('T')[0];

  // Rotation sur les 10 visuels du catalogue
  const visualIndex = (day - 1) % catalog.length;
  const visual = catalog[visualIndex];

  for (let s = 0; s < slots.length; s++) {
    const slot = slots[s];
    const postIndex = (day - 1) * 3 + s + 1;

    // Récupération de la copie adaptée
    const themeCopies = copyTemplates[visual.theme] || copyTemplates.valeur;
    const copyGen = themeCopies[slot.label];
    const copy = copyGen(visual.personnage, visual);

    const fullCaption = `${copy.hook}\n\n${copy.body}\n\n${copy.cta}\n\n${hashtags}`;

    schedule.push({
      post_id: `POST_${String(postIndex).padStart(3, '0')}`,
      day_number: day,
      day_code: `J${String(day).padStart(2, '0')}`,
      date: dateStr,
      time: slot.time,
      slot_label: slot.label,
      slot_title: slot.name,
      channels: ['Facebook Feed & Story', 'Instagram Feed & Story', 'WhatsApp Statut'],
      theme_id: visual.theme,
      theme_title: visual.badge_top,
      personnage: visual.personnage,
      visual_id: visual.id,
      feed_image_file: `${visual.id}_feed_square.png`,
      feed_image_path: `marketing/programmation-60jours/visuels/feed/${visual.id}_feed_square.png`,
      story_image_file: `${visual.id}_story_vertical.png`,
      story_image_path: `marketing/programmation-60jours/visuels/story/${visual.id}_story_vertical.png`,
      caption_hook: copy.hook,
      caption_body: copy.body,
      caption_cta: copy.cta,
      full_caption: fullCaption,
      first_comment: copy.comment
    });
  }
}

// 1. Sauvegarde du Manifeste JSON complet
const manifestPath = path.join(rootDir, 'manifest_60jours.json');
fs.writeFileSync(manifestPath, JSON.stringify(schedule, null, 2), 'utf8');

// 2. Exportation CSV pour importation automatique (Metricool, Buffer, Meta Business Suite)
const csvHeader = [
  'Post_ID',
  'Jour',
  'Date',
  'Heure',
  'Creneau',
  'Canaux',
  'Personnage',
  'Theme',
  'Fichier_Feed_1_1',
  'Fichier_Story_9_16',
  'Hook',
  'Texte_Complet_Publication',
  'Premier_Commentaire'
];

function sanitizeCsv(str) {
  if (!str) return '""';
  return `"${str.replace(/"/g, '""')}"`;
}

const csvRows = [csvHeader.join(',')];
for (const p of schedule) {
  const row = [
    sanitizeCsv(p.post_id),
    sanitizeCsv(p.day_code),
    sanitizeCsv(p.date),
    sanitizeCsv(p.time),
    sanitizeCsv(p.slot_label),
    sanitizeCsv(p.channels.join(' | ')),
    sanitizeCsv(p.personnage),
    sanitizeCsv(p.theme_id),
    sanitizeCsv(p.feed_image_path),
    sanitizeCsv(p.story_image_path),
    sanitizeCsv(p.caption_hook),
    sanitizeCsv(p.full_caption),
    sanitizeCsv(p.first_comment)
  ];
  csvRows.push(row.join(','));
}

const csvPath = path.join(rootDir, 'CALENDRIER_60JOURS.csv');
fs.writeFileSync(csvPath, csvRows.join('\n'), 'utf8');

// 3. Génération du grand document Markdown
let md = `# 📅 Grand Calendrier Opérationnel de Programmation WAZAP (60 Jours)\n\n`;
md += `> **Période :** 01/10/2026 au 29/11/2026 (60 jours consécutifs)\n`;
md += `> **Cadence :** 3 publications / canal / jour (Matin 08h00 · Midi 12h30 · Soir 18h30)\n`;
md += `> **Canaux Synchronisés :** **Facebook** (Feed & Story), **Instagram** (Feed & Story), **WhatsApp Business** (Statut / Story)\n`;
md += `> **Volume Total :** **180 créations éditoriales complètes** prêtes à programmer avec visuels Haute Définition (Feed 2160×2160 et Story 2160×3840).\n\n`;

md += `### 🎯 Synthèse des Piliers Stratégiques\n`;
md += `1. **Sécurité Inviolable :** Zéro cash sur la marchandise, QR Code Universel (Wave, Orange, MTN, Moov, Carte), livreurs vérifiés CNI par IA OCR, Assurance Colis Sûr.\n`;
md += `2. **Gain de Temps & Rapidité :** Livreur assigné en < 3 minutes, algorithme radar Haversine, suivi GPS en direct sans application lourde, fini les 20 appels.\n`;
md += `3. **Valeur Ajoutée & Révolution Économique :** 15 courses offertes aux commerçants (Pack Digital Boutique à 0 F commission), 0% commission prélevée sur le livreur (1 000 F net dès le premier mètre), Défi 50 smartphones Xiaomi Redmi 15C neufs.\n\n`;

md += `### 📁 Accès Rapide aux Ressources\n`;
md += `- **Manifeste JSON d'automatisation :** [\`manifest_60jours.json\`](manifest_60jours.json)\n`;
md += `- **Export CSV compatible Metricool / Buffer / Meta Suite :** [\`CALENDRIER_60JOURS.csv\`](CALENDRIER_60JOURS.csv)\n`;
md += `- **Dossier des visuels Feed Carré 1:1 (2160×2160) :** [\`visuels/feed/\`](visuels/feed/)\n`;
md += `- **Dossier des visuels Story Vertical 9:16 (2160×3840) :** [\`visuels/story/\`](visuels/story/)\n\n`;
md += `---\n\n`;

// Détail structuré des jours
for (let d = 1; d <= 60; d++) {
  const dayPosts = schedule.filter(p => p.day_number === d);
  const dCode = `J${String(d).padStart(2, '0')}`;
  const dDate = dayPosts[0].date;
  const heroChar = dayPosts[0].personnage;
  const heroTheme = dayPosts[0].theme_title;

  md += `## 🗓️ JOUR ${dCode} — ${dDate} • Protagoniste : ${heroChar} (${heroTheme})\n\n`;

  for (const p of dayPosts) {
    md += `### ⏰ ${p.slot_title} — Canaux : ${p.channels.join(' · ')}\n\n`;
    md += `* **Format Visuel :** Feed Carré 1:1 (\`visuels/feed/${p.feed_image_file}\`) & Story Verticale 9:16 (\`visuels/story/${p.story_image_file}\`)\n`;
    md += `* **Bénéfice Clé :** ${p.theme_title}\n\n`;
    md += `#### 📝 Légende prête à coller (Caption Facebook & Instagram) :\n`;
    md += `\`\`\`text\n${p.full_caption}\n\`\`\`\n\n`;
    md += `#### 💬 Premier commentaire épinglé suggéré :\n`;
    md += `> ${p.first_comment}\n\n`;
    md += `---\n\n`;
  }
}

const mdPath = path.join(rootDir, 'CALENDRIER_PROGRAMMATION_60JOURS.md');
fs.writeFileSync(mdPath, md, 'utf8');

console.log(`✅ Calendrier complet de 60 jours (180 publications) généré avec succès !`);
console.log(`📄 Markdown : ${mdPath}`);
console.log(`📊 CSV      : ${csvPath}`);
console.log(`📋 JSON     : ${manifestPath}`);
