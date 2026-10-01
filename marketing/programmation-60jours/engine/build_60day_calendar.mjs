import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const rootDir = path.resolve(__dirname, '..');

const catalog = JSON.parse(fs.readFileSync(path.join(__dirname, 'visuals_catalog.json'), 'utf8'));

// Variations spécifiques de midi et soir par visuel
const characterSlotVariations = {
  visuel_01_securite_awa: {
    midi: {
      hook: "⚡ BIJOUTERIE & ARTICLES DE VALEUR À MIDI : ZÉRO CASH MANIPULÉ, ZÉRO RISQUE !",
      body: "Pause déjeuner à Abidjan : une cliente au Plateau commande un collier ou une montre de valeur.\nComment la livrer en toute sécurité sans craindre qu'un coursier parte avec l'argent ?\n\nSur WAZAP, la règle est mathématique : ZÉRO CASH SUR LA MARCHANDISE.\nLe client scanne le QR Code Universel WAZAP (Wave, Orange Money, MTN, Moov, Carte bancaire).\nL'argent entre directement sur le compte d'Awa avant même la remise du paquet.\n\n🛡️ Coursiers vérifiés avec CNI par IA Google Cloud Vision\n📍 Trajet surveillé en temps réel par GPS\n🎁 15 premières livraisons offertes sans commission !",
      cta: "👉 Expédiez vos créations précieuses en toute sécurité : envoyez « COLIS » au 05 44 05 19 72 sur WhatsApp ou cliquez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "💎 Confieriez-vous un bijou de 50 000 F à un livreur inconnu sans garantie ? Donnez votre avis !"
    },
    soir: {
      hook: "🌙 18H30 À ABIDJAN : FERMEZ VOTRE BOUTIQUE L'ESPRIT SEREIN, 100% DE VOS RECETTES EN COMPTE !",
      body: "Fini d'attendre 20h dans l'angoisse qu'un livreur revienne verser vos recettes de la journée.\nAvec WAZAP, chaque vente est encaissée à la seconde même de la livraison par QR Code Universel.\n\nVous clôturez vos comptes en 1 coup d'œil sur votre téléphone, votre trésorerie est sécurisée, et l'Assurance Colis Sûr protège chaque expédition.\nC'est la tranquillité d'esprit que mérite tout commerçant ivoirien.",
      cta: "📦 Rejoignez les commerçants sereins : envoyez « COLIS » sur WhatsApp au 05 44 05 19 72 ou touchez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "✨ À quelle heure clôturez-vous habituellement vos comptes de livraison le soir ? Partagez vos réalités d'entrepreneur !"
    }
  },
  visuel_02_securite_fatou: {
    midi: {
      hook: "🎂 RUSH DE MIDI : GÂTEAUX D'ANNIVERSAIRE LIVRÉS AU BUREAU SANS UNE SEULE ÉGRATIGNURE !",
      body: "À 13h, la fête d'anniversaire surprise commence au bureau. Si le gâteau arrive incliné ou la crème écrasée, c'est la réputation de Fatou qui est en jeu !\n\nAvec WAZAP Colis Sûr :\n🛵 Coursiers formés au transport horizontal délicat.\n🔒 Assurance Colis Sûr garantissant le remboursement en cas d'accident.\n💳 Le collègue qui réceptionne scanne le QR code Wave / Orange / MTN au franc près !",
      cta: "📲 Vos douceurs méritent les meilleurs égards : envoyez « COLIS » sur WhatsApp au 05 44 05 19 72 ou touchez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "🧁 Avez-vous déjà vu un gâteau arriver complètement renversé par un livreur pressé ? Racontez-nous !"
    },
    soir: {
      hook: "🌙 18H30 : TOUS LES BUFFETS DU JOUR LIVRÉS IMPECCABLES & ENCAISSÉS AU DERNIER CENTIME !",
      body: "Après une journée passée devant les fourneaux à pétrir et décorer, Fatou souffle enfin.\nTous ses gâteaux et plateaux traiteurs du jour sont arrivés intacts chez les clients.\n\nZéro palabre de fausse monnaie, zéro livreur injoignable dans la nuit : l'argent de chaque commande a été versé directement par QR Code Universel dès la porte franchie.",
      cta: "🍰 Pour des livraisons gourmandes 100% sereines : envoyez « COLIS » au 05 44 05 19 72 sur WhatsApp ou cliquez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "❤️ Quelle est la commande la plus délicate que vous ayez eu à livrer jusqu'à présent ?"
    }
  },
  visuel_03_securite_clarisse: {
    midi: {
      hook: "💄 COMMANDES BEAUTÉ EXPRESS ENTRE MIDI ET DEUX : VOS CLIENTES LIVRÉES À LA PAUSE DÉJEUNER !",
      body: "Vos clientes au travail profitent de la pause déjeuner pour recevoir leurs crèmes, sérums et parfums.\nPas question de les faire attendre 45 minutes sur le trottoir !\n\nSur WAZAP, la cliente suit la moto en temps réel sur la carte interactive.\nÀ l'arrivée, scan instantané du QR Code WAZAP (Wave, OM, MTN) en 5 secondes chrono.\nElle retourne au bureau à l'heure, ravie et parfumée !",
      cta: "🌸 Vos produits méritent un service rapide et élégant : envoyez « COLIS » sur WhatsApp au 05 44 05 19 72 ou tapez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "💄 Vos clientes commandent-elles plutôt des soins du visage ou des parfums pour les livraisons de midi ?"
    },
    soir: {
      hook: "🌧️ MÊME SOUS L'ORAGE DU SOIR À ABIDJAN : VOS PARFUMS ARRIVENT AU SEC DANS DES SACS SCELLÉS !",
      body: "Quand la pluie torrentielle tombe sur le boulevard de Koumassi à 18h, la plupart des livreurs éteignent leur téléphone et s'abritent sous les ponts.\n\nLes livreurs partenaires WAZAP sont équipés de sacs étanches Colis Sûr.\nLe radar GPS continue d'émettre en direct. Vous savez exactement où se trouve votre colis, et votre cliente est rassurée.",
      cta: "📦 Expédiez par tous les temps : envoyez « COLIS » au 05 44 05 19 72 sur WhatsApp ou touchez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "🌧️ Quels aménagements faites-vous pour protéger vos colis quand la météo s'emballe à Abidjan ?"
    }
  },
  visuel_04_rapidite_salimata: {
    midi: {
      hook: "👗 RETOUCHE COUTURE LIVRÉE EN URGENCE AVANT LA SOIRÉE : WAZAP ARRIVE EN 3 MINUTES !",
      body: "La robe de soirée de votre cliente a reçu sa dernière touche d'aiguille à 12h15.\nLa cliente en a besoin d'urgence pour son gala ce soir.\n\nPourquoi passer 15 appels sans réponse ?\nSur WAZAP, 1 tap sur WhatsApp déclenche le radar des 5 livreurs les plus proches d'Angré.\nEn 3 minutes chrono, la course est prise. La robe voyage sous housse protectrice.",
      cta: "⚡ Pour vos livraisons d'ateliers et de mode : envoyez « COLIS » au 05 44 05 19 72 ou cliquez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "🧵 Combien de temps mettez-vous en moyenne pour trouver un livreur de confiance pour vos tenues de valeur ?"
    },
    soir: {
      hook: "🌙 18H30 : LES CRÉATIONS DE SALIMATA BRILLENT SUR LES ÉVÉNEMENTS, ZÉRO STRESS LIVRAISON !",
      body: "Pendant que ses clientes défilent et reçoivent des compliments dans leurs robes sur-mesure, Salimata a déjà la confirmation de toutes ses livraisons du jour.\n\nChaque cliente a scanné le QR Code Universel WAZAP à l'essayage.\nSalimata a encaissé sa recette au franc près, sans commission exorbitante prélevée sur sa créativité.",
      cta: "💎 Faites décoller votre atelier de stylisme : envoyez « COLIS » sur WhatsApp au 05 44 05 19 72 ou tapez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "✨ Quelle a été votre plus belle satisfaction client cette semaine ? Racontez-nous !"
    }
  },
  visuel_05_rapidite_momo: {
    midi: {
      hook: "🔥 12H30 : LE FEU CRÉPITE À TREICHVILLE, LES PLATS PARTENT EN 20 MIN FUMANTS ET SAVOUREUX !",
      body: "Choukouya de bœuf, poulet braisé, aloco doré... À midi, les estomacs n'attendent pas !\nUn retard de 15 minutes, et c'est un client en colère qui annule sa commande.\n\nSur WAZAP, nos livreurs de proximité sont pré-positionnés autour de vos fourneaux.\nLe repas est emballé dans des caissons isolés et livré en moins de 20 minutes chrono.\nLe client paie par QR code au centime près : zéro dispute de monnaie !",
      cta: "🍗 Multipliez vos commandes du déjeuner : envoyez « COLIS » au 05 44 05 19 72 sur WhatsApp ou cliquez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "🍗 Quel est le plat le plus commandé dans votre maquis ou fast-food le midi à Abidjan ?"
    },
    soir: {
      hook: "🌙 18H30 : LE RUSH DU DÎNER DÉMARRE ! LIVREZ VOS GRILLADES DU SOIR SANS FAUX PAS.",
      body: "À la tombée de la nuit, les travailleurs rentrent et commandent leurs dîners.\nAvec WAZAP, la flotte de coursiers reste mobilisée jusqu'à la dernière braise.\n\nChaque plat arrive chaud, la recette est versée immédiatement sur votre compte Mobile Money, et vous clôturez une journée rentable sans jamais manipuler de billets sales.",
      cta: "🔥 Boostez vos livraisons du soir : envoyez « COLIS » sur WhatsApp au 05 44 05 19 72 ou cliquez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "🍻 Jusqu'à quelle heure servez-vous des commandes à emporter le soir ?"
    }
  },
  visuel_06_rapidite_amara: {
    midi: {
      hook: "👟 SNEAKERS & STREETWEAR LIVRÉS À MIDI PILE : FINI LES ACHETEURS QUI N'ONT PAS LA MONNAIE !",
      body: "Le livreur sonne à Marcory. La paire de Jordan ou de sneakers tendance vaut 28 500 FCFA.\nL'acheteur sort 3 billets de 10 000 FCFA. Qui a la monnaie de 1 500 F ? Personne !\n\nAvec WAZAP, l'acheteur scanne le QR code Wave / Orange / MTN et paie exactement 28 500 FCFA en 1 seconde.\nPas de billets égarés, pas d'aller-retour au kiosque, Amara reçoit sa notification bancaire immédiatement.",
      cta: "🚀 Modernisez la livraison de votre shop : envoyez « COLIS » au 05 44 05 19 72 sur WhatsApp ou touchez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "👟 Combien de ventes avez-vous déjà failli annuler juste à cause d'un manque de monnaie ?"
    },
    soir: {
      hook: "🌙 18H30 : TOUTES LES PAIRES DE LA JOURNÉE LIVRÉES, TRÉSORERIE 100% AU VERT !",
      body: "La journée de vente se termine pour le streetwear à Abidjan. Les clients ont reçu leurs colis, essayent leurs paires et postent leurs unboxings.\n\nSur WAZAP, Amara n'a pas un seul franc dans la nature.\nZéro commission cachée, zéro risque de faux billet encaissé à la sauvette.",
      cta: "👟 Rejoignez la nouvelle génération de commerçants : envoyez « COLIS » au 05 44 05 19 72 ou tapez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "🔥 Quel modèle de sneakers a été votre plus gros carton de vente ce mois-ci ?"
    }
  },
  visuel_07_valeur_aicha: {
    midi: {
      hook: "🎁 COMMERÇANTS DU GRAND MARCHÉ D'ADJAMÉ : EXPÉDIEZ VOS COLIS DE MIDI À 0 F DE COMMISSION !",
      body: "Tantie Aïcha a préparé 6 complets de wax pour des clientes à Marcory et Yopougon.\nPourquoi donner 2 000 F de commission par colis aux applications intermédiaires ?\n\nSur WAZAP, le Pack Digital Boutique offre 15 courses sans aucune commission WAZAP.\nLe livreur touche son tarif net juste (1 000 F à 2 000 F), et Tantie Aïcha conserve l'intégralité de sa marge commerciale !",
      cta: "🛍️ Réservez votre pack gratuit en 30 secondes : envoyez « COLIS » sur WhatsApp au 05 44 05 19 72 ou touchez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "📢 Quel montant moyen dépensez-vous par mois en frais de livraison pour votre boutique ?"
    },
    soir: {
      hook: "🌙 18H30 : LES PORTES D'ADJAMÉ SE FERMENT, LA TRÉSORERIE DE TANTIE AÏCHA EST INTACTE !",
      body: "Quand la cloche sonne la fermeture des grands marchés d'Abidjan, Tantie Aïcha rentre chez elle le cœur léger.\nSes 15 livraisons offertes WAZAP lui ont fait économiser des dizaines de milliers de francs de commissions abusives.\n\nChaque cliente a reçu son tissu intact, payé par QR Code Universel en 1 seconde.",
      cta: "💎 Profitez-en vous aussi dès demain : envoyez « COLIS » au 05 44 05 19 72 sur WhatsApp ou cliquez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "✨ Que feriez-vous de 30 000 F de marge supplémentaire économisés chaque mois sur vos livraisons ?"
    }
  },
  visuel_08_valeur_bakary: {
    midi: {
      hook: "🛵 LIVREURS INDÉPENDANTS : 4 COURSES BOUCLÉES CE MIDI = 100% DE L'ARGENT DANS TA POCHE !",
      body: "Bakary a roulé toute la matinée entre Marcory, Treichville et Koumassi.\nSur d'autres plateformes, l'application lui aurait déjà prélevé 2 500 F sur ses gains.\n\nSur WAZAP, 0 FCFA de commission ! Tout ce qu'il a gagné est pour lui et sa moto.\nEt en plus, Bakary cumule des points pour le grand tirage trimestriel des 50 smartphones Xiaomi Redmi 15C neufs !",
      cta: "🔥 Rejoins Bakary et gagne dignement ta vie : envoie « DISPO » sur WhatsApp au 05 44 05 19 72 ou touche 👉 https://wa.me/2250544051972?text=DISPO",
      comment: "💪 Combien de courses as-tu déjà bouclées aujourd'hui sur les routes d'Abidjan ?"
    },
    soir: {
      hook: "🌙 18H30 : FIN DE JOURNÉE POUR LE LIVREUR WAZAP — ZÉRO DETTE DE COMMISSION ENVERS QUICONQUE !",
      body: "Tu rentres chez toi à Marcory après une journée intense sur le bitume.\nTu n'as aucune commission à reverser en fin de semaine, aucun compte qui risque d'être bloqué arbitrairement.\n\nTu es libre, indépendant et respecté. Tu tapes « INDISPO » sur WhatsApp et tu profites de ta soirée en famille.",
      cta: "🛵 Sois ton propre patron : envoie « DISPO » sur WhatsApp au 05 44 05 19 72 ou clique 👉 https://wa.me/2250544051972?text=DISPO",
      comment: "🤝 Quel est pour toi le plus bel avantage d'être un livreur indépendant à Abidjan ?"
    }
  },
  visuel_09_valeur_koffi: {
    midi: {
      hook: "🎯 DIGNITÉ DU COURSIER : 1 000 FCFA MINIMUM NET GARANTI DÈS LE PREMIER MÈTRE !",
      body: "À midi, les propositions de courses tombent. Pas question pour Koffi d'accepter une course à 400 ou 500 FCFA qui ne paie même pas un litre d'essence !\n\nSur WAZAP, le plancher minimum est gravé dans le marbre :\n1 000 FCFA net dans la même commune.\n1 500 FCFA commune voisine.\n2 000 FCFA longue distance / traversée de pont.\nLe travail d'un motard a une vraie valeur et WAZAP le fait respecter !",
      cta: "🛵 Roule pour des tarifs qui te respectent : envoie « DISPO » sur WhatsApp au 05 44 05 19 72 ou tape 👉 https://wa.me/2250544051972?text=DISPO",
      comment: "🎯 As-tu déjà refusé une course parce que le prix proposé était insultant ? Dis-nous !"
    },
    soir: {
      hook: "🌙 18H30 : LE BILAN DE KOFFI — DES COURSES JUSTES, UN RESPECT TOTAL ET DE LA FIERTÉ.",
      body: "Koffi range sa moto à la Riviera. Il a fait ses courses pour des commerçants de son quartier qui le connaissent et l'apprécient.\n\nPas de manipulation d'espèces dangereuse sur lui pendant les trajets de nuit : les clients ont payé par QR Code Universel.\nKoffi rentre en sécurité avec son salaire net intact.",
      cta: "🛵 Fais partie de la flotte respectée : envoie « DISPO » au 05 44 05 19 72 sur WhatsApp ou clique 👉 https://wa.me/2250544051972?text=DISPO",
      comment: "✨ Quelle est ta commune de prédilection pour rouler tranquillement le soir à Abidjan ?"
    }
  },
  visuel_10_duel_comparatif: {
    midi: {
      hook: "⚖️ MATCH DE MIDI : 45 MIN À CHERCHER UN MOTARD VS UN COURSIER EN 3 MIN AVEC WAZAP !",
      body: "Il est 12h30. Deux boutiques voisines reçoivent une commande urgente.\nBoutique A : envoie des messages dans 8 groupes WhatsApp, attend, relance, le client s'énerve.\nBoutique B (WAZAP) : tape COLIS sur WhatsApp, un livreur certifié accepte en 2 min 40 s, la commande est déjà en route.\n\nLe résultat ? Boutique B fidélise son client pendant que Boutique A perd sa vente.",
      cta: "👉 Passez dans le camp des gagnants : envoyez « COLIS » au 05 44 05 19 72 sur WhatsApp ou cliquez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "⏱️ Dans quel camp préférez-vous être pour vos livraisons de ce midi ?"
    },
    soir: {
      hook: "🌙 18H30 : LE MATCH EST PLIÉ — REGARDEZ LE BILAN DE VOTRE JOURNÉE DE COMMERCE !",
      body: "❌ ANCIENNE MÉTHODE :\n- Vous avez passé 2 heures au téléphone à traquer des livreurs.\n- Vous avez tremblé jusqu'au retour de vos recettes.\n- Vous avez perdu 25% de marge en commissions.\n\n🟢 MÉTHODE WAZAP :\n- Livreur assigné en 3 minutes chrono.\n- Zéro manipulation d'espèces sur la marchandise.\n- 15 courses gratuites et 100% de vos marges conservées.\n\nLe choix du bon sens est fait.",
      cta: "🎯 Adoptez la révolution WAZAP dès demain : envoyez « COLIS » sur WhatsApp au 05 44 05 19 72 ou tapez 👉 https://wa.me/2250544051972?text=COLIS",
      comment: "🥊 Quel est le point qui vous soulage le plus dans la méthode WAZAP ? Dites-le nous en commentaire !"
    }
  }
};

const slots = [
  { time: '08:00', label: 'matin', name: 'Post 1 — Matin (08h00)' },
  { time: '12:30', label: 'midi', name: 'Post 2 — Midi (12h30)' },
  { time: '18:30', label: 'soir', name: 'Post 3 — Soir (18h30)' }
];

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

    let copy;
    if (slot.label === 'matin') {
      copy = {
        hook: visual.hook,
        body: visual.body,
        cta: visual.cta,
        hashtags: visual.hashtags,
        comment: visual.comment
      };
    } else {
      const varMap = characterSlotVariations[visual.id] || {};
      const slotVar = varMap[slot.label];
      if (slotVar) {
        copy = {
          hook: slotVar.hook,
          body: slotVar.body,
          cta: slotVar.cta,
          hashtags: visual.hashtags,
          comment: slotVar.comment
        };
      } else {
        copy = {
          hook: visual.hook,
          body: visual.body,
          cta: visual.cta,
          hashtags: visual.hashtags,
          comment: visual.comment
        };
      }
    }

    const fullCaption = `${copy.hook}\n\n${copy.body}\n\n${copy.cta}\n\n${copy.hashtags}`;

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
      theme_title: visual.badge_title || '🛡️ WAZAP SÉCURITÉ',
      personnage: visual.personnage,
      visual_id: visual.id,
      feed_image_file: `${visual.id}_feed_square.png`,
      feed_image_path: `marketing/programmation-60jours/visuels/feed/${visual.id}_feed_square.png`,
      story_image_file: `${visual.id}_story_vertical.png`,
      story_image_path: `marketing/programmation-60jours/visuels/story/${visual.id}_story_vertical.png`,
      caption_hook: copy.hook,
      caption_body: copy.body,
      caption_cta: copy.cta,
      hashtags: copy.hashtags,
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
md += `1. **Sécurité Inviolable :** Zéro cash sur la marchandise, QR Code Universel (Wave, Orange Money, MTN, Moov, Carte bancaire), livreurs vérifiés CNI par IA Google Cloud Vision, Assurance Colis Sûr.\n`;
md += `2. **Gain de Temps & Rapidité :** Livreur assigné en < 3 minutes, algorithme radar de proximité, suivi GPS en direct sans application lourde, fini les 20 appels.\n`;
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
