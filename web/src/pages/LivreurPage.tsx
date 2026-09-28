import { useState } from 'react'
import { Link } from 'react-router-dom'
import BrandLogo from '../components/BrandLogo'
import '../styles/livreur.css'

const WA_RIDER_NUMBER = '2250544051972'
const WA_RIDER_DISPLAY = '05 44 05 19 72'
const WA_RIDER_LINK = `https://wa.me/${WA_RIDER_NUMBER}?text=DISPO`

interface TarifPalier {
  level: string
  distance: string
  price: string
  examples: string
  featured?: boolean
}

const TARIFFS: TarifPalier[] = [
  {
    level: 'Palier 1 · Intra-commune',
    distance: '0 à 4 km',
    price: '1 000 FCFA',
    examples: 'Cocody ➔ Cocody, Marcory ➔ Marcory, Yopougon ➔ Yopougon',
    featured: true,
  },
  {
    level: 'Palier 2 · Communes voisines',
    distance: '4 à 8 km',
    price: '1 500 FCFA',
    examples: 'Cocody ➔ Plateau, Marcory ➔ Treichville / Koumassi',
  },
  {
    level: 'Palier 3 · Traversée / Pont',
    distance: '8 à 16 km',
    price: '2 000 FCFA',
    examples: 'Yopougon ➔ Cocody / Marcory, Abobo ➔ Plateau / Zone Sud',
  },
  {
    level: 'Palier 4 · Périphérie express',
    distance: 'Plus de 16 km',
    price: '2 500 FCFA',
    examples: 'Bingerville, Songon, Grand-Bassam',
  },
]

const FAQ_ITEMS = [
  {
    q: 'Combien coûte l’inscription pour devenir livreur WAZAP ?',
    a: 'C’est 100% GRATUIT. Vous ne payez aucun frais d’inscription, aucune caution et aucun abonnement forcé. Votre compte est créé instantanément sur WhatsApp.',
  },
  {
    q: 'Combien je gagne par course ?',
    a: 'Vous touchez au minimum 1 000 FCFA net dès le 1er mètre (même pour 300 mètres). Pour les trajets entre communes voisines, c’est 1 500 FCFA, et 2 000 FCFA pour les traversées de pont. 100% de ce montant vous revient directement en direct (Wave, Orange Money ou Espèces). WAZAP ne prend 0% de commission sur vos courses.',
  },
  {
    q: 'Dois-je télécharger une application sur mon téléphone ?',
    a: 'NON ! Aucune application lourde qui chauffe le téléphone ou vide votre batterie et vos données internet. Tout fonctionne simplement dans l’application WhatsApp que vous utilisez déjà tous les jours.',
  },
  {
    q: 'Comment s’inscrire en 1 minute ?',
    a: '1. Envoyez « DISPO » au 05 44 05 19 72 sur WhatsApp. 2. Choisissez votre commune en tapant un chiffre (1 à 6). 3. Envoyez la photo de votre CNI (notre robot lit votre nom automatiquement). 4. Cliquez sur le bouton vert 🟢 DISPO et vous commencez à recevoir des courses !',
  },
  {
    q: 'Comment fonctionne le défi du smartphone Xiaomi Redmi 15C ?',
    a: 'Chaque mois, les livreurs les plus réguliers et ceux qui parrainent des collègues motards reçoivent un smartphone Xiaomi Redmi 15C neuf scellé, remis en main propre par l’équipe WAZAP à Abidjan.',
  },
  {
    q: 'Que sont les alertes prioritaires (Vague 1) ?',
    a: 'C’est une option facultative pour ceux qui veulent maximiser leurs gains : pour seulement 50 FCFA par alerte (Pack Flash à 1 000 F = 20 alertes), vous recevez les propositions de courses 30 secondes avant les autres livreurs du secteur.',
  },
]

export default function LivreurPage() {
  const [coursesPerDay, setCoursesPerDay] = useState<number>(8)
  const [openFaq, setOpenFaq] = useState<number | null>(null)

  const dailyGain = coursesPerDay * 1250 // Moyenne pondérée 1 000F - 1 500F
  const monthlyGain = dailyGain * 26 // 26 jours de travail

  return (
    <div className="livreur-wrap">
      {/* Alerte Urgence Recrutement */}
      <div className="livreur-banner-alert">
        <span>🟢 RECRUTEMENT OUVERT — 150 NOUVEAUX LIVREURS RECHERCHÉS À ABIDJAN</span>
        <span style={{ opacity: 0.7 }}>•</span>
        <span>0% COMMISSION PRÉLEVÉE SUR LE LIVREUR</span>
      </div>

      {/* Topbar */}
      <header className="livreur-topbar">
        <div className="livreur-topbar-inner">
          <Link to="/" style={{ textDecoration: 'none' }}>
            <BrandLogo size="md" variant="inline" showTagline={false} />
          </Link>

          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <Link
              to="/vente"
              className="btn-livreur-secondary"
              style={{ padding: '8px 14px', fontSize: 13 }}
            >
              🏪 Vous êtes commerçant ?
            </Link>
            <a
              href={WA_RIDER_LINK}
              target="_blank"
              rel="noreferrer"
              className="btn-livreur-wa"
              style={{ padding: '8px 16px', fontSize: 13 }}
            >
              📲 Postuler (WhatsApp)
            </a>
          </div>
        </div>
      </header>

      {/* Hero Section */}
      <section className="livreur-hero">
        <div>
          <div className="livreur-tag">
            🛵 Spécial Livreurs Indépendants & Flottes
          </div>
          <h1 className="livreur-hero-title">
            1 000 FCFA net <span className="highlight">minimum par course.</span><br />
            0% de commission. 100% sur WhatsApp.
          </h1>
          <p className="livreur-hero-desc">
            Marre des courses à 350 ou 400 FCFA qui ne paient même pas le carburant de ta moto ?
            Chez WAZAP, <strong>100% du prix de la course va directement dans ta poche</strong>.
            Zéro patron sur ton dos, zéro application lourde qui plante.
          </p>

          <div className="livreur-hero-cta-box">
            <a
              href={WA_RIDER_LINK}
              target="_blank"
              rel="noreferrer"
              className="btn-livreur-wa"
            >
              <span>📲 ENVOIE « DISPO » SUR WHATSAPP</span>
            </a>
            <a href="#tarifs" className="btn-livreur-secondary">
              📊 Voir la grille officielle
            </a>
          </div>

          <div style={{ fontSize: 13, color: '#94a3b8', marginBottom: 16 }}>
            Numéro officiel terrain : <strong style={{ color: '#00d66c' }}>{WA_RIDER_DISPLAY}</strong> • Réponse automatique en 5 secondes
          </div>

          <div className="livreur-pills-row">
            <span className="livreur-pill">🛡️ Plancher garanti : 1 000 F dès le 1er mètre</span>
            <span className="livreur-pill">⚡ 0% commission prélevée</span>
            <span className="livreur-pill">📱 Tout se passe sur WhatsApp</span>
            <span className="livreur-pill">🎁 Smartphone Redmi 15C à gagner</span>
          </div>
        </div>

        {/* Visuel Carte Livreur */}
        <div>
          <div className="livreur-hero-card">
            <div className="livreur-hero-card-header">
              <img src="/app/avatars/koffi.jpg" alt="Livreur WAZAP" />
              <div>
                <div style={{ fontWeight: 900, fontSize: 18, color: '#fff' }}>Koffi M. (Livreur Certifié)</div>
                <div style={{ fontSize: 12, color: '#00d66c', fontWeight: 700 }}>● Actif à Koumassi & Marcory</div>
                <div style={{ fontSize: 12, color: '#f59e0b', marginTop: 2 }}>★★★★★ (4.9/5 · 312 courses)</div>
              </div>
            </div>

            <div className="livreur-card-kpi-grid">
              <div className="livreur-card-kpi">
                <span className="val">1 000 F</span>
                <span className="label">Plancher minimum</span>
              </div>
              <div className="livreur-card-kpi">
                <span className="val">0 %</span>
                <span className="label">Commission prélevée</span>
              </div>
              <div className="livreur-card-kpi">
                <span className="val">&lt; 3 min</span>
                <span className="label">Assignation course</span>
              </div>
              <div className="livreur-card-kpi">
                <span className="val">Direct</span>
                <span className="label">Paiement Wave/OM/Cash</span>
              </div>
            </div>

            <div style={{
              background: 'rgba(0, 214, 108, 0.1)',
              border: '1px solid rgba(0, 214, 108, 0.25)',
              borderRadius: 14,
              padding: 14,
              fontSize: 13,
              color: '#cbd5e1',
            }}>
              💬 <em>« Avec WAZAP, 8 courses dans la journée c'est minimum 8 000 F à 12 000 F net pour moi. Je ne tourne plus à vide dans Abidjan ! »</em>
            </div>
          </div>
        </div>
      </section>

      {/* Duel Comparatif : Anciennes Applis vs WAZAP */}
      <section className="livreur-section">
        <div className="livreur-section-header">
          <span className="livreur-section-tag">Vérité terrain</span>
          <h2 className="livreur-section-title">Pourquoi les motards d’Abidjan rejoignent WAZAP</h2>
          <p className="livreur-section-desc">
            Comparez en toute transparence ce que vous gagnez réellement sur votre journée.
          </p>
        </div>

        <div className="duel-grid">
          {/* Mauvais : Anciennes applis */}
          <div className="duel-card duel-card--bad">
            <div className="duel-card-title" style={{ color: '#ef4444' }}>
              <span>❌</span> Anciennes Applis (Yango, etc.)
            </div>
            <ul className="duel-list">
              <li>
                <span style={{ color: '#ef4444' }}>✕</span>
                <span><strong>Tarif de misère :</strong> 320 FCFA de base + 50 F/km. Des courses payées 350 à 450 FCFA.</span>
              </li>
              <li>
                <span style={{ color: '#ef4444' }}>✕</span>
                <span><strong>Commissions lourdes :</strong> 15% à 25% de commission prélevée sur votre sueur par les flottes.</span>
              </li>
              <li>
                <span style={{ color: '#ef4444' }}>✕</span>
                <span><strong>Application lourde :</strong> Chauffe la batterie, vide vos gigas internet et plante en plein trajet.</span>
              </li>
              <li>
                <span style={{ color: '#ef4444' }}>✕</span>
                <span><strong>Bilan décourageant :</strong> Il faut 25 à 30 courses pour espérer ramener 6 500 F net après essence.</span>
              </li>
              <li>
                <span style={{ color: '#ef4444' }}>✕</span>
                <span><strong>Comptes bloqués sans explication</strong> et aucun support humain à Abidjan pour vous écouter.</span>
              </li>
            </ul>
          </div>

          {/* Bon : WAZAP */}
          <div className="duel-card duel-card--good">
            <div className="duel-card-title" style={{ color: '#00d66c' }}>
              <span>⚡</span> Le Réseau WAZAP
            </div>
            <ul className="duel-list">
              <li>
                <span style={{ color: '#00d66c' }}>✔</span>
                <span><strong>Plancher garanti :</strong> 1 000 FCFA net minimum dès le 1er mètre (même pour 300 mètres !).</span>
              </li>
              <li>
                <span style={{ color: '#00d66c' }}>✔</span>
                <span><strong>0% de commission livreur :</strong> 100% du prix de la course (1 000 à 2 500 F) est pour vous.</span>
              </li>
              <li>
                <span style={{ color: '#00d66c' }}>✔</span>
                <span><strong>100% sur WhatsApp :</strong> Pas d'application à installer, aucun Mo gaspillé, rapide et fluide.</span>
              </li>
              <li>
                <span style={{ color: '#00d66c' }}>✔</span>
                <span><strong>Vraie rentabilité :</strong> 8 courses = 8 000 F à 12 000 F nets directement dans votre poche.</span>
              </li>
              <li>
                <span style={{ color: '#00d66c' }}>✔</span>
                <span><strong>Liberté totale :</strong> Vous passez en ligne ou hors ligne d'un simple message « DISPO » / « INDISPO ».</span>
              </li>
            </ul>
          </div>
        </div>
      </section>

      {/* Grille Tarifaire Grand Abidjan */}
      <section id="tarifs" className="livreur-section">
        <div className="livreur-section-header">
          <span className="livreur-section-tag">Grille officielle</span>
          <h2 className="livreur-section-title">Barème garanti Grand Abidjan</h2>
          <p className="livreur-section-desc">
            Des tarifs clairs, transparents et respectueux du travail du livreur à moto.
          </p>
        </div>

        <div className="tarifs-grid">
          {TARIFFS.map((t, idx) => (
            <div key={idx} className={`tarif-card ${t.featured ? 'tarif-card--featured' : ''}`}>
              <div style={{ fontSize: 13, fontWeight: 700, color: '#94a3b8' }}>{t.level}</div>
              <div className="tarif-amount">{t.price}</div>
              <div style={{ fontSize: 12, color: '#00d66c', fontWeight: 700, marginBottom: 12 }}>
                Distance : {t.distance}
              </div>
              <p style={{ fontSize: 13, color: '#cbd5e1', lineHeight: 1.5 }}>{t.examples}</p>
              <div style={{ marginTop: 14, paddingTop: 12, borderTop: '1px solid rgba(255,255,255,0.06)', fontSize: 12, color: '#94a3b8' }}>
                💰 100% net pour le livreur
              </div>
            </div>
          ))}
        </div>
      </section>

      {/* Parcours d'Onboarding 4 Étapes */}
      <section className="livreur-section">
        <div className="livreur-section-header">
          <span className="livreur-section-tag">Simple & Rapide</span>
          <h2 className="livreur-section-title">Comment commencer en 2 minutes chrono</h2>
          <p className="livreur-section-desc">
            Zéro paperasse, zéro déplacement en agence. Tout se fait depuis votre smartphone.
          </p>
        </div>

        <div className="steps-grid">
          <div className="step-card">
            <div className="step-num">1</div>
            <h3 style={{ fontSize: 17, fontWeight: 800, marginBottom: 8 }}>Envoie « DISPO »</h3>
            <p style={{ fontSize: 14, color: '#94a3b8' }}>
              Envoie simplement le mot <strong>DISPO</strong> sur WhatsApp au <strong>{WA_RIDER_DISPLAY}</strong>.
            </p>
          </div>

          <div className="step-card">
            <div className="step-num">2</div>
            <h3 style={{ fontSize: 17, fontWeight: 800, marginBottom: 8 }}>Choisis ta commune</h3>
            <p style={{ fontSize: 14, color: '#94a3b8' }}>
              Réponds avec le chiffre de ton quartier : <strong>1</strong> pour Cocody, <strong>2</strong> pour Marcory, <strong>3</strong> pour Yopougon, etc.
            </p>
          </div>

          <div className="step-card">
            <div className="step-num">3</div>
            <h3 style={{ fontSize: 17, fontWeight: 800, marginBottom: 8 }}>Photo de ta CNI</h3>
            <p style={{ fontSize: 14, color: '#94a3b8' }}>
              Prends en photo ta carte d'identité. Notre système intelligent extrait tes informations automatiquement.
            </p>
          </div>

          <div className="step-card">
            <div className="step-num">4</div>
            <h3 style={{ fontSize: 17, fontWeight: 800, marginBottom: 8 }}>Bouton 🟢 DISPO</h3>
            <p style={{ fontSize: 14, color: '#94a3b8' }}>
              Clique sur le bouton interactif pour passer en ligne. Tu reçois instantanément les alertes de courses proches !
            </p>
          </div>
        </div>

        <div style={{ textAlign: 'center', marginTop: 32 }}>
          <a
            href={WA_RIDER_LINK}
            target="_blank"
            rel="noreferrer"
            className="btn-livreur-wa"
          >
            <span>🚀 DÉMARRER MON ENRÔLEMENT SUR WHATSAPP</span>
          </a>
        </div>
      </section>

      {/* Simulateur de Revenus */}
      <section className="livreur-section">
        <div className="sim-card">
          <div style={{ textAlign: 'center', marginBottom: 20 }}>
            <span className="livreur-section-tag">Calculateur de rentabilité</span>
            <h3 style={{ fontSize: 24, fontWeight: 900 }}>Combien vas-tu gagner avec WAZAP ?</h3>
            <p style={{ fontSize: 14, color: '#94a3b8' }}>
              Choisis le nombre de courses que tu souhaites faire chaque jour :
            </p>
          </div>

          <div className="sim-buttons">
            {[4, 6, 8, 10, 12].map((num) => (
              <button
                key={num}
                type="button"
                className={`btn-sim ${coursesPerDay === num ? 'btn-sim--active' : ''}`}
                onClick={() => setCoursesPerDay(num)}
              >
                {num} courses / jour
              </button>
            ))}
          </div>

          <div className="sim-result-box">
            <div>
              <div style={{ fontSize: 12, color: '#94a3b8', textTransform: 'uppercase', fontWeight: 700 }}>
                Gain estimé par jour
              </div>
              <div className="sim-val">{dailyGain.toLocaleString('fr-FR')} F</div>
              <div style={{ fontSize: 12, color: '#cbd5e1' }}>100% net dans ta poche</div>
            </div>
            <div>
              <div style={{ fontSize: 12, color: '#94a3b8', textTransform: 'uppercase', fontWeight: 700 }}>
                Revenu mensuel estimé
              </div>
              <div className="sim-val" style={{ color: '#fbbf24' }}>
                {monthlyGain.toLocaleString('fr-FR')} F
              </div>
              <div style={{ fontSize: 12, color: '#cbd5e1' }}>Sur 26 jours de livraison</div>
            </div>
          </div>

          <div style={{ textAlign: 'center', marginTop: 24 }}>
            <a
              href={WA_RIDER_LINK}
              target="_blank"
              rel="noreferrer"
              className="btn-livreur-wa"
              style={{ width: '100%', maxWidth: 380 }}
            >
              Rejoindre et commencer à livrer
            </a>
          </div>
        </div>
      </section>

      {/* Défi Smartphone Redmi 15C */}
      <section className="livreur-section">
        <div className="challenge-box">
          <div>
            <div className="livreur-tag" style={{ background: 'rgba(245,158,11,0.2)', color: '#f59e0b' }}>
              🎁 Challenge Spécial Ambassadeurs
            </div>
            <h2 style={{ fontSize: 'clamp(22px, 4vw, 32px)', fontWeight: 900, marginBottom: 12 }}>
              Gagne un Smartphone Xiaomi Redmi 15C Neuf !
            </h2>
            <p style={{ color: '#cbd5e1', fontSize: 15, lineHeight: 1.6, marginBottom: 20 }}>
              Pour récompenser les meilleurs livreurs d'Abidjan : effectue tes courses avec sérieux,
              parraine 5 collègues motards et repars avec un smartphone flambant neuf scellé,
              remis en main propre lors de notre cérémonie mensuelle.
            </p>
            <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
              <div style={{ background: 'rgba(0,0,0,0.3)', padding: '8px 14px', borderRadius: 10, fontSize: 13 }}>
                📦 250 courses effectuées
              </div>
              <div style={{ background: 'rgba(0,0,0,0.3)', padding: '8px 14px', borderRadius: 10, fontSize: 13 }}>
                👥 5 collègues parrainés
              </div>
              <div style={{ background: 'rgba(0,0,0,0.3)', padding: '8px 14px', borderRadius: 10, fontSize: 13 }}>
                ⭐ Note &gt; 4.5 étoiles
              </div>
            </div>
          </div>

          <div style={{ textAlign: 'center' }}>
            <div style={{
              background: 'rgba(0,0,0,0.4)',
              border: '2px solid rgba(245, 158, 11, 0.4)',
              borderRadius: 20,
              padding: 24,
              display: 'inline-block',
            }}>
              <div style={{ fontSize: 44, marginBottom: 8 }}>📱</div>
              <div style={{ fontWeight: 900, fontSize: 18, color: '#fbbf24' }}>Xiaomi Redmi 15C Neuf</div>
              <div style={{ fontSize: 12, color: '#94a3b8', marginTop: 4 }}>128 Go · Double SIM · Scellé</div>
              <div style={{
                marginTop: 16,
                background: '#00d66c',
                color: '#052e16',
                fontWeight: 800,
                fontSize: 12,
                padding: '6px 12px',
                borderRadius: 999,
              }}>
                50 smartphones offerts
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Packs Alertes Prioritaires */}
      <section className="livreur-section">
        <div className="livreur-section-header">
          <span className="livreur-section-tag">Option Accélérateur</span>
          <h2 className="livreur-section-title">Packs d'Alertes Prioritaires (Vague 1)</h2>
          <p className="livreur-section-desc">
            Optionnel : Recevez les alertes de courses 30 secondes avant tout le monde sur votre secteur.
          </p>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 20, maxWidth: 700, margin: '0 auto' }}>
          <div className="tarif-card">
            <div style={{ fontSize: 14, fontWeight: 800, color: '#94a3b8' }}>PACK FLASH LIVREUR</div>
            <div className="tarif-amount">1 000 FCFA</div>
            <div style={{ fontSize: 16, fontWeight: 800, color: '#fff', marginBottom: 8 }}>20 alertes prioritaires</div>
            <div style={{ fontSize: 13, color: '#94a3b8', marginBottom: 16 }}>Soit 50 FCFA par alerte prioritaire Vague 1</div>
            <p style={{ fontSize: 13, color: '#cbd5e1' }}>
              Idéal pour sécuriser les meilleures courses de la journée (traversées de pont à 2 000 F).
            </p>
          </div>

          <div className="tarif-card tarif-card--featured">
            <div style={{ fontSize: 14, fontWeight: 800, color: '#fbbf24' }}>PACK PRO LIVREUR</div>
            <div className="tarif-amount">5 000 FCFA</div>
            <div style={{ fontSize: 16, fontWeight: 800, color: '#fff', marginBottom: 8 }}>100 alertes prioritaires</div>
            <div style={{ fontSize: 13, color: '#fbbf24', marginBottom: 16 }}>Bonus fidélité inclus</div>
            <p style={{ fontSize: 13, color: '#cbd5e1' }}>
              Pour les livreurs à temps plein voulant tourner à plein régime toute la semaine.
            </p>
          </div>
        </div>
      </section>

      {/* FAQ */}
      <section className="livreur-section">
        <div className="livreur-section-header">
          <span className="livreur-section-tag">Questions fréquentes</span>
          <h2 className="livreur-section-title">Tout ce que vous devez savoir</h2>
        </div>

        <div style={{ maxWidth: 760, margin: '0 auto' }}>
          {FAQ_ITEMS.map((item, idx) => {
            const isOpen = openFaq === idx
            return (
              <div key={idx} className="livreur-faq-item">
                <div
                  className="livreur-faq-q"
                  onClick={() => setOpenFaq(isOpen ? null : idx)}
                >
                  <span>{item.q}</span>
                  <span style={{ color: '#00d66c', fontSize: 20 }}>{isOpen ? '−' : '+'}</span>
                </div>
                {isOpen && <div className="livreur-faq-a">{item.a}</div>}
              </div>
            )
          })}
        </div>
      </section>

      {/* Bannière Finale CTA */}
      <section className="livreur-section" style={{ textAlign: 'center', padding: '60px 20px' }}>
        <div style={{
          background: 'linear-gradient(135deg, rgba(0, 214, 108, 0.15), rgba(7, 94, 84, 0.3))',
          border: '1.5px solid rgba(0, 214, 108, 0.4)',
          borderRadius: 24,
          padding: '48px 24px',
          maxWidth: 800,
          margin: '0 auto',
        }}>
          <h2 style={{ fontSize: 'clamp(24px, 5vw, 36px)', fontWeight: 900, marginBottom: 16 }}>
            Prêt à rouler avec 1 000 F net minimum ?
          </h2>
          <p style={{ color: '#94a3b8', fontSize: 16, maxWidth: 540, margin: '0 auto 28px' }}>
            Rejoins les centaines de livreurs d'Abidjan qui ont déjà dit non aux commissions abusives.
            Mise en ligne en 2 minutes sur WhatsApp.
          </p>

          <a
            href={WA_RIDER_LINK}
            target="_blank"
            rel="noreferrer"
            className="btn-livreur-wa"
            style={{ fontSize: 18, padding: '18px 36px' }}
          >
            <span>👉 ENVOYER « DISPO » SUR WHATSAPP</span>
          </a>

          <div style={{ marginTop: 16, fontSize: 13, color: '#cbd5e1' }}>
            Numéro officiel direct : <strong>{WA_RIDER_DISPLAY}</strong>
          </div>
        </div>
      </section>

      {/* Sticky Bottom Bar for Mobile */}
      <div className="livreur-sticky-bar">
        <div>
          <div style={{ fontWeight: 900, fontSize: 14, color: '#00d66c' }}>1 000 F Net Minimum</div>
          <div style={{ fontSize: 11, color: '#94a3b8' }}>0% Commission • Tout sur WhatsApp</div>
        </div>
        <a
          href={WA_RIDER_LINK}
          target="_blank"
          rel="noreferrer"
          className="btn-livreur-wa"
          style={{ padding: '10px 18px', fontSize: 14 }}
        >
          <span>ENVOIE « DISPO »</span>
        </a>
      </div>
    </div>
  )
}
