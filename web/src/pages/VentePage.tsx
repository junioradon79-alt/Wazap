import { useEffect, useMemo, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import BrandLogo from '../components/BrandLogo'
import '../styles/landing.css'

const COMMUNES = [
  'Marcory', 'Cocody', 'Yopougon', 'Plateau', 'Adjamé', 'Treichville',
  'Koumassi', 'Abobo', 'Port-Bouët', 'Bingerville',
]

const FAQ_ITEMS = [
  {
    q: 'Comment fonctionne WAZAP et que paie le commerçant ?',
    a: 'WAZAP vous met en relation avec un livreur disponible près de votre commerce. Vous achetez des crédits : un crédit correspond à une course confiée et n’est utilisé que lorsqu’un livreur accepte la commande. Les frais de livraison habituels sont réglés directement au livreur par vous ou votre client.',
  },
  {
    q: 'Mes clients doivent-ils installer une application ?',
    a: 'Non. Vos clients continuent de commander naturellement par WhatsApp. Ils reçoivent un lien de suivi et la confirmation de réception de leur colis.',
  },
  {
    q: 'Que comprend Colis Sûr ?',
    a: 'Chaque livreur du réseau est identifié. La réception peut être validée par le QR Code Universel WAZAP. En cas d’incident, vous pouvez le signaler directement sur WhatsApp pour ouvrir une prise en charge.',
  },
  {
    q: 'Qu’est-ce qui est offert au démarrage ?',
    a: 'Les frais de service WAZAP sont offerts sur vos 15 premières livraisons. Il n’y a ni abonnement, ni engagement au démarrage.',
  },
]

const PACKS = [
  { name: 'Mini', deliveries: '6 courses', price: '1 000 F' },
  { name: 'Petit', deliveries: '35 courses', price: '5 000 F' },
  { name: 'Moyen', deliveries: '80 courses', price: '10 000 F' },
  { name: 'Grand', deliveries: '220 courses', price: '25 000 F' },
]

export default function VentePage() {
  const { user } = useAuth()
  const location = useLocation()
  const query = useMemo(() => new URLSearchParams(location.search), [location.search])
  const source = query.get('src') || 'page-vente'

  const [whatsapp, setWhatsapp] = useState('')
  const [business, setBusiness] = useState('')
  const [contact, setContact] = useState('')
  const [phone, setPhone] = useState('')
  const [zone, setZone] = useState(() => query.get('zone') ?? 'Marcory')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  useEffect(() => {
    api.get<{ whatsappNumber: string }>('/public/sales/config')
      .then((config) => setWhatsapp(config.whatsappNumber))
      .catch(() => setWhatsapp(''))
  }, [])

  const waLink = (text: string) => {
    const rawNumber = whatsapp || '+2250544051972'
    const number = rawNumber.replace(/[^\d]/g, '').replace(/^0+/, '')
    return `https://wa.me/${number}?text=${encodeURIComponent(text)}`
  }

  const submit = async () => {
    setError(null)
    if (!business.trim()) {
      setError('Indiquez le nom de votre commerce.')
      return
    }
    if (!phone.trim() || phone.replace(/\D/g, '').length < 8) {
      setError('Veuillez renseigner un numéro WhatsApp ivoirien valide.')
      return
    }

    setBusy(true)
    try {
      await api.post<{ leadId: string }>('/public/leads', {
        businessName: business.trim(),
        contactName: contact.trim() || null,
        whatsappNumber: phone.trim(),
        zone,
        source,
      })
      setDone(true)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Une erreur est survenue lors de l’envoi. Veuillez réessayer.')
    } finally {
      setBusy(false)
    }
  }

  const baseAssetUrl = import.meta.env.BASE_URL || '/app/'

  return (
    <div className="landing-wrap">
      <div className="landing-notice">15 livraisons offertes à l’activation · Sans abonnement · Abidjan</div>

      <header className="landing-topbar">
        <div className="landing-container landing-topbar__inner">
          <Link to="/" className="landing-brand-link" aria-label="Accueil WAZAP">
            <BrandLogo size="md" variant="inline" showTagline={false} theme="light" />
          </Link>

          <nav className="landing-nav" aria-label="Navigation principale">
            <a href="#fonctionnement">Comment ça marche</a>
            <a href="#offre">L’offre</a>
            <a href="#securite">Colis Sûr</a>
          </nav>

          <div className="landing-topbar__actions">
            <Link to="/livreurs" className="landing-link-rider">Je suis livreur</Link>
            {user ? (
              <Link to="/" className="landing-link-account">Mon espace</Link>
            ) : (
              <Link to="/login" className="landing-link-account">Se connecter</Link>
            )}
            <a href="#inscription" className="landing-btn landing-btn--primary">Essayer WAZAP</a>
          </div>
        </div>
      </header>

      <main>
        <section className="landing-container landing-hero" aria-labelledby="landing-title">
          <div className="landing-hero__copy">
            <p className="landing-eyebrow"><span aria-hidden="true" /> La livraison pensée pour Abidjan</p>
            <h1 id="landing-title" className="landing-hero__title">
              Votre commerce livre <em>sans appels, sans stress.</em>
            </h1>
            <p className="landing-hero__sub">
              Vos commandes restent sur WhatsApp. WAZAP trouve un livreur disponible, suit le colis et informe votre client — simplement.
            </p>
            <div className="landing-hero__cta">
              <a href="#inscription" className="landing-btn landing-btn--primary">Activer mes 15 courses offertes <span aria-hidden="true">→</span></a>
              <a href="#fonctionnement" className="landing-btn landing-btn--text">Voir comment ça marche</a>
            </div>
            <p className="landing-hero__note">Aucun engagement · vous payez uniquement à l’usage</p>
            <ul className="landing-hero__trust" aria-label="Garanties WAZAP">
              <li>100% WhatsApp</li>
              <li>Suivi en direct</li>
              <li>Livreurs identifiés</li>
            </ul>
          </div>

          <div className="landing-hero__visual">
            <div className="landing-portrait-glow" aria-hidden="true" />
            <figure className="landing-portrait">
              <img src={`${baseAssetUrl}images/wazap-hero-merchant.png`} alt="Commerçante souriante utilisant WAZAP" />
            </figure>
            <div className="landing-delivery-note">
              <span className="landing-delivery-note__check" aria-hidden="true">✓</span>
              <div>
                <strong>Une vente, une livraison</strong>
                <span>Votre client est informé à chaque étape.</span>
              </div>
            </div>
          </div>
        </section>

        <section className="landing-container landing-proof" aria-label="Les bénéfices WAZAP">
          <article>
            <span className="landing-proof__icon" aria-hidden="true">↗</span>
            <div><h2>Un livreur en quelques minutes</h2><p>Un réseau local pour prendre le relais au bon moment.</p></div>
          </article>
          <article>
            <span className="landing-proof__icon" aria-hidden="true">✓</span>
            <div><h2>Des colis mieux protégés</h2><p>Suivi, validation de réception et livreurs identifiés.</p></div>
          </article>
          <article>
            <span className="landing-proof__icon" aria-hidden="true">○</span>
            <div><h2>Des coûts faciles à comprendre</h2><p>Des crédits à l’usage, sans commission sur vos ventes.</p></div>
          </article>
        </section>

        <section id="fonctionnement" className="landing-section landing-container">
          <div className="landing-section__head">
            <p className="landing-eyebrow"><span aria-hidden="true" /> Simple avant tout</p>
            <h2>Trois étapes. Votre vente continue d’avancer.</h2>
            <p>WAZAP s’intègre dans la façon dont vos clients commandent déjà.</p>
          </div>
          <div className="landing-steps">
            <article><span>01</span><h3>Vous confirmez la commande</h3><p>Envoyez l’adresse et les détails de livraison directement par WhatsApp.</p></article>
            <article><span>02</span><h3>WAZAP trouve le livreur</h3><p>Un coursier disponible près de vous peut prendre la course.</p></article>
            <article><span>03</span><h3>Votre client suit la livraison</h3><p>Il reçoit les informations nécessaires jusqu’à la réception du colis.</p></article>
          </div>
        </section>

        <section id="offre" className="landing-container landing-offer">
          <div className="landing-offer__intro">
            <p>Pour essayer sans risque</p>
            <h2>15 livraisons offertes.</h2>
            <span>Testez WAZAP avec vos vraies commandes, puis choisissez les crédits adaptés à votre rythme.</span>
            <div>✓ Sans abonnement &nbsp; · &nbsp; ✓ Sans frais cachés</div>
            <a href="#inscription" className="landing-btn landing-btn--light">Activer mon offre découverte <span aria-hidden="true">→</span></a>
          </div>
          <div className="landing-offer__packs" aria-label="Aperçu des packs de crédits">
            <p>Ensuite, choisissez votre rythme</p>
            <div className="landing-packs">
              {PACKS.map((pack) => (
                <article key={pack.name}>
                  <h3>Pack {pack.name}</h3><span>{pack.deliveries}</span><strong>{pack.price}</strong>
                </article>
              ))}
            </div>
            <a href="#inscription" className="landing-offer__details">Nous vous aidons à choisir votre pack →</a>
          </div>
        </section>

        <section id="securite" className="landing-container landing-safety">
          <div className="landing-safety__copy">
            <p className="landing-eyebrow"><span aria-hidden="true" /> Colis Sûr</p>
            <h2>La confiance reste au cœur de chaque course.</h2>
            <p>De l’identification du livreur à la réception, WAZAP vous aide à rendre la livraison plus lisible pour vous comme pour votre client.</p>
          </div>
          <div className="landing-safety__list">
            <article><b>01</b><div><h3>Livreurs identifiés</h3><p>Chaque livreur du réseau est vérifié avant son activation.</p></div></article>
            <article><b>02</b><div><h3>Réception confirmée</h3><p>Le QR Code Universel WAZAP peut confirmer la remise du colis.</p></div></article>
            <article><b>03</b><div><h3>Signalement facilité</h3><p>Un incident peut être signalé directement sur WhatsApp.</p></div></article>
          </div>
        </section>

        <section id="inscription" className="landing-container landing-activation">
          <div className="landing-activation__head">
            <p className="landing-eyebrow"><span aria-hidden="true" /> Activation immédiate</p>
            <h2>Activez vos 15 livraisons offertes.</h2>
            <p>Quelques informations suffisent pour démarrer. Sans carte bancaire, sans engagement.</p>
          </div>
          <div className="landing-form-card">
            {done ? (
              <div className="landing-success" role="status">
                <span aria-hidden="true">✓</span>
                <h3>Félicitations {contact || business} !</h3>
                <p>Votre demande est enregistrée. Un responsable WAZAP vous contacte sur WhatsApp pour activer vos 15 premières courses offertes.</p>
                <a href={waLink(`Bonjour WAZAP 👋 Je viens de m’inscrire pour ${business}. Je souhaite activer mes 15 courses offertes.`)} target="_blank" rel="noreferrer" className="landing-btn landing-btn--primary">Ouvrir la conversation WhatsApp</a>
              </div>
            ) : (
              <form onSubmit={(event) => { event.preventDefault(); void submit() }} noValidate>
                {error && <div className="landing-form-error" role="alert">{error}</div>}
                <div className="landing-form-grid">
                  <label>Nom de votre commerce ou boutique *<input value={business} onChange={(event) => setBusiness(event.target.value)} placeholder="Ex : Restaurant Chez Awa" /></label>
                  <label>Commune principale *<select value={zone} onChange={(event) => setZone(event.target.value)}>{COMMUNES.map((commune) => <option key={commune}>{commune}</option>)}</select></label>
                  <label>Numéro WhatsApp joignable *<input value={phone} onChange={(event) => setPhone(event.target.value)} placeholder="Ex : +225 07 01 02 03 04" inputMode="tel" /></label>
                  <label>Nom du responsable <small>facultatif</small><input value={contact} onChange={(event) => setContact(event.target.value)} placeholder="Ex : Mme Awa Koné" /></label>
                </div>
                <button type="submit" className="landing-btn landing-btn--primary landing-form-submit" disabled={busy}>{busy ? 'Traitement de l’activation…' : 'Valider et recevoir mes 15 courses gratuites →'}</button>
                <p className="landing-form-help">Vos informations servent uniquement à vous recontacter pour l’activation.</p>
              </form>
            )}
          </div>
        </section>

        <section className="landing-container landing-faq">
          <div className="landing-section__head">
            <p className="landing-eyebrow"><span aria-hidden="true" /> Questions fréquentes</p>
            <h2>Les réponses avant de commencer.</h2>
          </div>
          <div>{FAQ_ITEMS.map((item) => <details key={item.q}><summary>{item.q}<span aria-hidden="true">+</span></summary><p>{item.a}</p></details>)}</div>
        </section>
      </main>

      <footer className="landing-footer">
        <div className="landing-container">
          <BrandLogo size="sm" variant="inline" showTagline={false} theme="light" />
          <p>La livraison par WhatsApp, pensée pour les commerces d’Abidjan.</p>
          <Link to="/livreurs">Vous êtes livreur ? Rejoindre le réseau →</Link>
          <small>© {new Date().getFullYear()} WAZAP Côte d’Ivoire</small>
        </div>
      </footer>

      <div className="landing-sticky-bar"><a href="#inscription" className="landing-btn landing-btn--primary">Activer mes 15 courses offertes</a></div>
    </div>
  )
}
