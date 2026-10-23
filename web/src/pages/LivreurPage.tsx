import { useState } from 'react'
import { Link } from 'react-router-dom'
import BrandLogo from '../components/BrandLogo'
import '../styles/livreur.css'

const WA_RIDER_NUMBER = '2250544051972'
const WA_RIDER_DISPLAY = '05 44 05 19 72'
const WA_RIDER_LINK = `https://wa.me/${WA_RIDER_NUMBER}?text=DISPO`

const TARIFFS = [
  { level: 'Intra-commune', distance: '0 à 4 km', price: '1 000 F', examples: 'Cocody → Cocody, Marcory → Marcory, Yopougon → Yopougon' },
  { level: 'Communes voisines', distance: '4 à 8 km', price: '1 500 F', examples: 'Cocody → Plateau, Marcory → Treichville ou Koumassi', featured: true },
  { level: 'Traversée / pont', distance: '8 à 16 km', price: '2 000 F', examples: 'Yopougon → Cocody ou Marcory, Abobo → Plateau' },
  { level: 'Périphérie express', distance: 'Plus de 16 km', price: '2 500 F', examples: 'Bingerville, Songon, Grand-Bassam' },
]

const FAQ_ITEMS = [
  { q: 'L’inscription pour devenir livreur WAZAP est-elle gratuite ?', a: 'Oui. L’enrôlement démarre gratuitement sur WhatsApp. L’équipe vous guide ensuite pour compléter votre profil et votre activation.' },
  { q: 'Faut-il télécharger une application ?', a: 'Non. Les informations et les alertes de course se gèrent directement dans WhatsApp, l’application que vous utilisez déjà.' },
  { q: 'Comment puis-je commencer ?', a: 'Envoyez simplement « DISPO » au numéro WAZAP. Vous choisissez votre commune, transmettez les informations nécessaires à votre identification et recevez les étapes suivantes.' },
  { q: 'Puis-je choisir mes moments de disponibilité ?', a: 'Oui. Vous signalez votre disponibilité depuis WhatsApp. Vous pouvez repasser indisponible lorsque vous ne souhaitez plus recevoir de propositions.' },
]

export default function LivreurPage() {
  const [coursesPerDay, setCoursesPerDay] = useState(8)
  const baseAssetUrl = import.meta.env.BASE_URL || '/app/'
  const dailyGain = coursesPerDay * 1250
  const monthlyGain = dailyGain * 26

  return (
    <div className="rider-wrap">
      <div className="rider-notice">Recrutement ouvert à Abidjan · Inscription gratuite sur WhatsApp</div>

      <header className="rider-topbar">
        <div className="rider-container rider-topbar__inner">
          <Link to="/" className="rider-brand" aria-label="Accueil WAZAP"><BrandLogo size="md" variant="inline" showTagline={false} theme="light" /></Link>
          <nav className="rider-nav" aria-label="Navigation principale"><a href="#fonctionnement">Comment ça marche</a><a href="#bareme">Le barème</a><a href="#faq">Questions</a></nav>
          <div className="rider-topbar__actions"><Link to="/vente" className="rider-merchant-link">Je suis commerçant</Link><a href={WA_RIDER_LINK} target="_blank" rel="noreferrer" className="rider-btn rider-btn--primary">Devenir livreur</a></div>
        </div>
      </header>

      <main>
        <section className="rider-container rider-hero" aria-labelledby="rider-title">
          <div className="rider-hero__copy">
            <p className="rider-eyebrow"><span aria-hidden="true" /> Le réseau de livraison WAZAP</p>
            <h1 id="rider-title">Livrez à votre rythme. <em>Gardez la main.</em></h1>
            <p className="rider-hero__sub">WAZAP aide les motards d’Abidjan à recevoir des propositions de courses sur WhatsApp, simplement et sans application supplémentaire.</p>
            <div className="rider-hero__cta"><a href={WA_RIDER_LINK} target="_blank" rel="noreferrer" className="rider-btn rider-btn--primary">Envoyer « DISPO » sur WhatsApp <span aria-hidden="true">→</span></a><a href="#bareme" className="rider-btn rider-btn--text">Voir le barème</a></div>
            <p className="rider-hero__note">Numéro officiel WAZAP : <strong>{WA_RIDER_DISPLAY}</strong></p>
            <ul className="rider-hero__trust" aria-label="Les avantages WAZAP"><li>Inscription gratuite</li><li>100% WhatsApp</li><li>Disponibilité flexible</li></ul>
          </div>
          <div className="rider-hero__visual">
            <div className="rider-portrait-glow" aria-hidden="true" />
            <figure className="rider-portrait"><img src={`${baseAssetUrl}images/wazap-hero-rider.png`} alt="Livreur WAZAP souriant tenant son casque" /></figure>
            <div className="rider-availability-card"><span className="rider-availability-card__dot" aria-hidden="true" /><div><strong>Vous choisissez vos moments</strong><span>Passez disponible quand vous êtes prêt à rouler.</span></div></div>
          </div>
        </section>

        <section className="rider-container rider-proof" aria-label="Les repères pour livreurs">
          <article><span aria-hidden="true">01</span><div><h2>Une grille transparente</h2><p>Un barème clair pour savoir à quoi vous attendre avant de commencer.</p></div></article>
          <article><span aria-hidden="true">02</span><div><h2>Pas d’application à installer</h2><p>Les échanges essentiels restent dans WhatsApp, sur votre téléphone.</p></div></article>
          <article><span aria-hidden="true">03</span><div><h2>Un réseau local</h2><p>WAZAP est pensé pour les trajets et les commerces d’Abidjan.</p></div></article>
        </section>

        <section id="fonctionnement" className="rider-section rider-container">
          <div className="rider-section__head"><p className="rider-eyebrow"><span aria-hidden="true" /> Démarrer simplement</p><h2>Votre parcours commence par un message.</h2><p>Quelques étapes claires, accompagnées par WhatsApp.</p></div>
          <div className="rider-steps">
            <article><span>01</span><h3>Envoyez « DISPO »</h3><p>Écrivez à WAZAP sur WhatsApp pour lancer votre demande d’enrôlement.</p></article>
            <article><span>02</span><h3>Indiquez votre zone</h3><p>Choisissez la commune où vous roulez habituellement et transmettez les éléments demandés pour votre profil.</p></article>
            <article><span>03</span><h3>Activez votre disponibilité</h3><p>Une fois votre profil activé, signalez les moments où vous êtes prêt à recevoir des courses.</p></article>
          </div>
        </section>

        <section id="bareme" className="rider-rate-section"><div className="rider-container">
          <div className="rider-rate-section__head"><p className="rider-eyebrow"><span aria-hidden="true" /> Repère de rémunération</p><h2>Le barème Grand Abidjan, en toute clarté.</h2><p>Le montant dépend de la distance et du type de trajet. Le détail est partagé avant votre course.</p></div>
          <div className="rider-rates">{TARIFFS.map((tariff) => <article key={tariff.level} className={tariff.featured ? 'rider-rate-card rider-rate-card--featured' : 'rider-rate-card'}><p>{tariff.level}</p><strong>{tariff.price}</strong><span>{tariff.distance}</span><small>{tariff.examples}</small></article>)}</div>
        </div></section>

        <section className="rider-container rider-estimate">
          <div className="rider-estimate__copy"><p className="rider-eyebrow"><span aria-hidden="true" /> Une estimation, à votre rythme</p><h2>Projetez une journée de livraison.</h2><p>Choisissez un volume indicatif. Cette estimation repose sur une moyenne illustrative de 1 250 F par course et ne constitue pas une garantie de revenu.</p></div>
          <div className="rider-calculator" aria-label="Simulateur indicatif de revenus"><p>Combien de courses souhaitez-vous faire ?</p><div className="rider-calculator__choices">{[4, 6, 8, 10, 12].map((value) => <button key={value} type="button" className={coursesPerDay === value ? 'is-active' : ''} onClick={() => setCoursesPerDay(value)}>{value} courses</button>)}</div><div className="rider-calculator__result"><div><span>Estimation par jour</span><strong>{dailyGain.toLocaleString('fr-FR')} F</strong></div><div><span>Estimation sur 26 jours</span><strong>{monthlyGain.toLocaleString('fr-FR')} F</strong></div></div></div>
        </section>

        <section className="rider-container rider-cta-panel"><div><p className="rider-eyebrow"><span aria-hidden="true" /> Prêt à démarrer ?</p><h2>Un message suffit pour rejoindre le réseau.</h2><p>Envoyez « DISPO » à WAZAP et suivez les étapes d’inscription directement dans WhatsApp.</p></div><a href={WA_RIDER_LINK} target="_blank" rel="noreferrer" className="rider-btn rider-btn--light">Envoyer « DISPO » <span aria-hidden="true">→</span></a></section>

        <section id="faq" className="rider-container rider-faq"><div className="rider-section__head"><p className="rider-eyebrow"><span aria-hidden="true" /> Questions fréquentes</p><h2>Les réponses avant de partir.</h2></div><div>{FAQ_ITEMS.map((item) => <details key={item.q}><summary>{item.q}<span aria-hidden="true">+</span></summary><p>{item.a}</p></details>)}</div></section>
      </main>

      <footer className="rider-footer"><div className="rider-container"><BrandLogo size="sm" variant="inline" showTagline={false} theme="light" /><p>La livraison par WhatsApp, pensée pour Abidjan.</p><Link to="/vente">Vous êtes commerçant ? Découvrir WAZAP →</Link><small>© {new Date().getFullYear()} WAZAP Côte d’Ivoire</small></div></footer>
      <div className="rider-sticky-bar"><a href={WA_RIDER_LINK} target="_blank" rel="noreferrer" className="rider-btn rider-btn--primary">Envoyer « DISPO » sur WhatsApp</a></div>
    </div>
  )
}
