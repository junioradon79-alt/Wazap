import { useEffect, useMemo, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import BrandLogo from '../components/BrandLogo'
import '../styles/landing.css'

interface CommuneInfo {
  name: string
  assignTime: string
  deliveryTime: string
  ridersCount: number
  popular: string
}

const COMMUNES: Record<string, CommuneInfo> = {
  Marcory: { name: 'Marcory', assignTime: '⚡ 2 min', deliveryTime: '15 min', ridersCount: 42, popular: 'Zone 4, Anoumabo, Résidentiel' },
  Cocody: { name: 'Cocody', assignTime: '⚡ 3 min', deliveryTime: '18 min', ridersCount: 56, popular: 'Angré, Riviera, Deux-Plateaux' },
  Yopougon: { name: 'Yopougon', assignTime: '⚡ 4 min', deliveryTime: '20 min', ridersCount: 65, popular: 'Siporex, Maroc, Bel Air' },
  Plateau: { name: 'Plateau', assignTime: '⚡ 2 min', deliveryTime: '12 min', ridersCount: 34, popular: 'Centre d’affaires, Cité administrative' },
  Adjamé: { name: 'Adjamé', assignTime: '⚡ 3 min', deliveryTime: '15 min', ridersCount: 48, popular: 'Grand Marché, Forum, Renault' },
  Treichville: { name: 'Treichville', assignTime: '⚡ 2 min', deliveryTime: '14 min', ridersCount: 30, popular: 'Avenue 8, Rue 12, Belleville' },
  Koumassi: { name: 'Koumassi', assignTime: '⚡ 3 min', deliveryTime: '16 min', ridersCount: 38, popular: 'Remblais, Campement, Prodomo' },
  Abobo: { name: 'Abobo', assignTime: '⚡ 4 min', deliveryTime: '22 min', ridersCount: 35, popular: 'Samaké, Gare, Avocatier' },
  'Port-Bouët': { name: 'Port-Bouët', assignTime: '⚡ 4 min', deliveryTime: '18 min', ridersCount: 26, popular: 'Vridi, Aéroport, Derrière Wharf' },
  Bingerville: { name: 'Bingerville', assignTime: '⚡ 5 min', deliveryTime: '25 min', ridersCount: 20, popular: 'Félicité, Blanchon, Nouveau Goudron' },
}

const FAQ_ITEMS = [
  {
    q: 'Comment fonctionne WAZAP et que paie le commerçant ?',
    a: 'WAZAP est le « Yango de la marchandise » : nous mettons en relation instantanée votre commerce avec un livreur géographiquement proche et disponible, sans passer un seul coup de fil. Vous achetez des packs de crédits (1 crédit = 1 course confiée), débités uniquement lorsque le livreur accepte votre commande. Les frais habituels de livraison (1 000 à 2 000 FCFA) restent payés directement au livreur par vous ou votre client.',
  },
  {
    q: 'Mes clients doivent-ils installer une application ?',
    a: 'Absolument aucune ! C’est la force de WAZAP : vos clients continuent de commander naturellement par message WhatsApp comme ils le font déjà. Ils reçoivent automatiquement un lien de suivi en direct et un code PIN sécurisé pour la réception du colis.',
  },
  {
    q: 'Comment fonctionne la Garantie Colis Sûr ?',
    a: 'Chaque livreur WAZAP est identifié et sa pièce d’identité est vérifiée avant toute attribution. La remise du colis exige la transmission d’un code secret à 4 chiffres. En cas d’avarie ou de perte confirmée, vous déclarez le sinistre directement par WhatsApp (« SINISTRE + code ») : le livreur est suspendu le temps de l’enquête et vous êtes indemnisé par Mobile Money sous 48h.',
  },
  {
    q: 'Qu’est-ce qui est offert au démarrage ?',
    a: 'Pour vous permettre de tester sans aucun risque, vos 15 premières livraisons sont 100% offertes (crédits gratuits offerts à l’activation). Aucun abonnement, aucun frais caché, aucun engagement.',
  },
  {
    q: 'Comment sont encaissés les paiements des clients ?',
    a: 'Vous gardez vos habitudes : le client paie en espèces à la livraison ou par Mobile Money (Wave, Orange, MTN). WAZAP ne prend aucune commission sur le montant de vos articles.',
  },
]

const COMPARISON_BAD = [
  'Appels à répétition pour chercher un coursier disponible',
  'Clients impatients qui annulent après 45 minutes d’attente',
  'Colis perdus ou abîmés sans recours ni indemnisation',
  'Livreurs injoignables sous la pluie ou dans les embouteillages',
  'Commissions élevées prélevées sur vos marges commerciales',
]

const COMPARISON_GOOD = [
  'Assignation automatique en moins de 3 minutes sur WhatsApp',
  'Lien de suivi GPS en direct envoyé automatiquement au client',
  'Garantie Colis Sûr avec code PIN secret et remboursement sous 48h',
  'Flotte active de livreurs certifiés avec CNI vérifiée sur 10 communes',
  '0% de commission sur vos articles et 15 courses offertes au départ',
]

const TESTIMONIALS = [
  {
    name: 'Aïcha B.',
    role: 'Boutique Chic & Glam · Cocody Angré',
    avatar: '/app/avatars/aicha.jpg',
    stars: '★★★★★',
    quote: 'Avant WAZAP, je perdais 4 à 5 ventes par jour parce que mes livreurs habituels ne décrochaient pas. Maintenant, je confirme sur WhatsApp, en 3 minutes le coursier est devant mon magasin.',
    tag: '⚡ +120 000 F / semaine sauvés',
  },
  {
    name: 'Amara T.',
    role: 'Chez Amara Grill · Marcory Zone 4',
    avatar: '/app/avatars/amara.jpg',
    stars: '★★★★★',
    quote: 'Mes braisés partent encore tout chauds ! Le client reçoit son lien GPS et son code PIN de livraison. Fini les litiges ou les contestations à l’arrivée.',
    tag: '🛡️ Sécurité PIN Colis Sûr',
  },
  {
    name: 'Fatou D.',
    role: 'Douceurs de Fatou · Yopougon Maroc',
    avatar: '/app/avatars/fatou.jpg',
    stars: '★★★★★',
    quote: 'Les 15 courses offertes m’ont convaincue tout de suite. Zéro engagement, pas d’application compliquée. Tout se passe dans WhatsApp, c’est magique pour mon commerce.',
    tag: '🎁 15 courses sans risque',
  },
  {
    name: 'Koffi E.',
    role: 'Livreur Partenaire · Koumassi & Marcory',
    avatar: '/app/avatars/koffi.jpg',
    stars: '★★★★★',
    quote: 'Je ne tourne plus à vide dans Abidjan. Les alertes tombent sur mon WhatsApp avec l’adresse exacte. Et 100% des frais de course (1 000 à 2 000 F) me reviennent directement.',
    tag: '🛵 100% des frais au livreur',
  },
]


export default function VentePage() {
  const { user } = useAuth()
  const location = useLocation()
  const query = useMemo(() => new URLSearchParams(location.search), [location.search])
  const src = query.get('src') || 'page-vente'

  const [whatsapp, setWhatsapp] = useState('')
  const [audience, setAudience] = useState<'vendor' | 'rider'>('vendor')
  const [selectedCommune, setSelectedCommune] = useState<string>('Marcory')
  const [dailyDeliveries, setDailyDeliveries] = useState<number>(15)

  // Champs de formulaire
  const [business, setBusiness] = useState('')
  const [contact, setContact] = useState('')
  const [phone, setPhone] = useState('')
  const [zone, setZone] = useState<string>(() => query.get('zone') ?? 'Marcory')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  useEffect(() => {
    api.get<{ whatsappNumber: string }>('/public/sales/config')
      .then((c) => setWhatsapp(c.whatsappNumber))
      .catch(() => setWhatsapp(''))
  }, [])

  const waLink = (text: string) => {
    if (!whatsapp) return null
    const n = whatsapp.replace(/[^\d]/g, '').replace(/^0+/, '')
    return `https://wa.me/${n}?text=${encodeURIComponent(text)}`
  }

  // Calculs du simulateur de rentabilité
  const monthlyOrders = dailyDeliveries * 26
  const savedOrders = Math.round(monthlyOrders * 0.22) // 22% de commandes traditionnellement perdues faute de livreur
  const hoursSavedPerMonth = Math.round((dailyDeliveries * 7 * 26) / 60)
  
  const recommendedPack = useMemo(() => {
    if (monthlyOrders <= 150) return { name: 'Pack Petit', credits: '35 courses', price: '5 000 FCFA', unit: '143 FCFA / course' }
    if (monthlyOrders <= 350) return { name: 'Pack Moyen', credits: '80 courses', price: '10 000 FCFA', unit: '125 FCFA / course' }
    if (monthlyOrders <= 700) return { name: 'Pack Grand', credits: '220 courses', price: '25 000 FCFA', unit: '114 FCFA / course' }
    return { name: 'Pack Pro', credits: '1 000 courses', price: '100 000 FCFA', unit: '100 FCFA / course' }
  }, [monthlyOrders])

  const submit = async () => {
    setError(null)
    if (!business.trim()) {
      setError(audience === 'vendor' ? 'Indiquez le nom de votre commerce.' : 'Indiquez votre prénom et nom.')
      return
    }
    if (!zone.trim()) {
      setError('Choisissez votre commune principale.')
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
        zone: zone.trim(),
        source: audience === 'vendor' ? src : `${src}-livreur`,
      })
      setDone(true)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Une erreur est survenue lors de l’envoi. Veuillez réessayer.')
    } finally {
      setBusy(false)
    }
  }

  const activeCommuneData = COMMUNES[selectedCommune] || COMMUNES['Marcory']

  return (
    <div className="landing-wrap">
      {/* Topbar sticky */}
      <header className="landing-topbar">
        <div className="landing-container landing-topbar__inner">
          <Link to="/" style={{ textDecoration: 'none' }}>
            <BrandLogo size="md" variant="inline" showTagline={false} />
          </Link>

          <div className="landing-topbar__actions">
            {user ? (
              <Link
                to="/"
                className="landing-btn landing-btn--ghost"
                style={{ padding: '8px 16px', fontSize: 13 }}
              >
                📊 Mon Espace
              </Link>
            ) : (
              <Link
                to="/login"
                className="landing-btn landing-btn--ghost"
                style={{ padding: '8px 16px', fontSize: 13 }}
              >
                🔐 Espace Marchand
              </Link>
            )}
            {waLink('Bonjour WAZAP 👋 Je souhaite avoir des informations sur la livraison de mon commerce.') && (
              <a
                href={waLink('Bonjour WAZAP 👋 Je souhaite avoir des informations sur la livraison de mon commerce.')!}
                target="_blank"
                rel="noreferrer"
                className="landing-btn landing-btn--ghost"
                style={{ padding: '8px 16px', fontSize: 13 }}
              >
                💬 WhatsApp Direct
              </a>
            )}
            <a href="#inscription" className="landing-btn landing-btn--primary" style={{ padding: '8px 18px', fontSize: 13 }}>
              🚀 Essai 15 Courses
            </a>
          </div>
        </div>
      </header>

      {/* Hero Section */}
      <section className="landing-container landing-hero">
        <div>
          <div className="landing-pill">
            <span className="landing-pill__dot" />
            <span>LE YANGO DE LA MARCHANDISE À ABIDJAN</span>
          </div>

          <h1 className="landing-hero__title">
            Vendez plus. <br />
            Livrez sans effort, <br />
            <span className="landing-hero__title-highlight">100% sur WhatsApp.</span>
          </h1>

          <p className="landing-hero__sub">
            Ne perdez plus jamais une vente faute de coursier. Vos clients commandent par message,
            un livreur proche est trouvé <strong>en moins de 3 minutes</strong> avec suivi en direct et garantie Colis Sûr.
          </p>

          <div className="landing-hero__cta">
            <a href="#inscription" className="landing-btn landing-btn--primary">
              ⚡ Activer mon commerce (15 courses offertes)
            </a>
            {waLink('Bonjour WAZAP 👋 Je veux tester la livraison pour mon commerce.') && (
              <a
                href={waLink('Bonjour WAZAP 👋 Je veux tester la livraison pour mon commerce.')!}
                target="_blank"
                rel="noreferrer"
                className="landing-btn landing-btn--ghost"
              >
                💬 Discuter en direct
              </a>
            )}
            <a
              href="/demo-video.html"
              target="_blank"
              rel="noreferrer"
              className="landing-btn landing-btn--ghost"
              style={{ padding: '12px 18px', fontSize: 13 }}
            >
              ▶️ Démo vidéo (58s)
            </a>
          </div>

          <div className="landing-hero__perks">
            <div className="landing-perk">
              <span className="landing-perk__icon">✅</span>
              <span>15 livraisons offertes</span>
            </div>
            <div className="landing-perk">
              <span className="landing-perk__icon">⚡</span>
              <span>0 application à installer</span>
            </div>
            <div className="landing-perk">
              <span className="landing-perk__icon">🛡️</span>
              <span>Livreurs certifiés Colis Sûr</span>
            </div>
            <div className="landing-perk">
              <span className="landing-perk__icon">💳</span>
              <span>Crédits dès 1 000 F à l’usage</span>
            </div>
          </div>
        </div>

        {/* Smartphone Mockup : Live WhatsApp Simulation */}
        <div>
          <div className="mockup-phone">
            <div className="mockup-phone__notch" />
            <div className="mockup-wa-header">
              <div className="mockup-wa-avatar" style={{ padding: 2, background: '#fff', overflow: 'hidden' }}>
                <img src="/app/logo-badge.png" alt="WAZAP" style={{ width: '100%', height: '100%', objectFit: 'contain', borderRadius: '50%' }} />
              </div>
              <div>
                <div style={{ fontWeight: 800, fontSize: 13, color: '#fff' }}>WAZAP Livraison</div>
                <div style={{ fontSize: 10, color: '#a7f3d0' }}>● En ligne · Abidjan</div>
              </div>
            </div>

            <div className="mockup-wa-body">
              {/* Message client */}
              <div className="mockup-msg mockup-msg--in">
                Bonjour 👋 Je veux commander <strong>2 Attiéké Poisson braisé</strong> à livrer à <strong>Marcory Zone 4</strong> (Rue du 7 Décembre).
                <div className="mockup-msg__time">12:34</div>
              </div>

              {/* Réponse commerçant / bot */}
              <div className="mockup-msg mockup-msg--out">
                Commande confirmée ! 🛵 WAZAP a assigné un livreur disponible à proximité :
                <div className="mockup-card">
                  <div style={{ fontWeight: 700, color: '#34d399' }}>🛵 Ibrahim K. (Yamaha YBR)</div>
                  <div style={{ fontSize: 11, color: '#cbd5e1' }}>À 1,2 km de votre boutique · Arrivée estimée : 14 min</div>
                  <div style={{ marginTop: 4, color: '#fcd34d', fontSize: 11, fontWeight: 700 }}>
                    🛡️ Code de sécurité client : 8492
                  </div>
                </div>
                <div className="mockup-msg__time">12:35 · Reçu ✓✓</div>
              </div>

              {/* Notification client statut */}
              <div className="mockup-msg mockup-msg--in">
                ✅ Super, je suis le livreur en direct sur la carte ! Merci pour la rapidité.
                <div className="mockup-msg__time">12:36</div>
              </div>

              {/* Statut final */}
              <div style={{ textAlign: 'center', margin: '4px 0' }}>
                <span style={{ background: 'rgba(0, 214, 108, 0.15)', color: '#00d66c', fontSize: 10, padding: '3px 10px', borderRadius: 999, fontWeight: 700 }}>
                  ✓ Course livrée avec succès en 18 minutes
                </span>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Metrics Ribbon */}
      <div className="landing-container">
        <section className="landing-ribbon">
          <div className="landing-ribbon__item">
            <span className="landing-ribbon__val">
              <span>&lt; 3</span> <span style={{ color: '#00d66c', fontSize: 24 }}>min</span>
            </span>
            <span className="landing-ribbon__lbl">Temps moyen d’assignation livreur</span>
          </div>

          <div className="landing-ribbon__item">
            <span className="landing-ribbon__val">
              <span>10</span> <span style={{ color: '#00d66c', fontSize: 24 }}>communes</span>
            </span>
            <span className="landing-ribbon__lbl">Couverture complète du Grand Abidjan</span>
          </div>

          <div className="landing-ribbon__item">
            <span className="landing-ribbon__val">
              <span>100%</span>
            </span>
            <span className="landing-ribbon__lbl">Livreurs avec CNI certifiée Colis Sûr</span>
          </div>

          <div className="landing-ribbon__item">
            <span className="landing-ribbon__val">
              <span>4.9</span> <span style={{ color: '#f59e0b', fontSize: 22 }}>★</span>
            </span>
            <span className="landing-ribbon__lbl">Score de satisfaction commerçants</span>
          </div>
        </section>
      </div>

      {/* Comparatif Avant / Après */}
      <section className="landing-container" style={{ marginTop: 60 }}>
        <div className="section-head">
          <span className="landing-pill">POURQUOI CHOISIR WAZAP</span>
          <h2 className="section-head__title">Fini le casse-tête des livraisons à Abidjan</h2>
          <p className="section-head__sub">
            Comparez la méthode artisanale avec la rapidité et la sérénité de l’automatisation WAZAP.
          </p>
        </div>

        <div className="comparison-grid">
          <div className="comparison-card comparison-card--bad">
            <div className="comparison-header">
              <span className="comparison-header__icon">❌</span>
              <h3 className="comparison-header__title" style={{ color: '#fca5a5' }}>
                Sans WAZAP (Méthode classique)
              </h3>
            </div>
            <ul className="comparison-list">
              {COMPARISON_BAD.map((item, idx) => (
                <li key={idx} className="comparison-item comparison-item--bad">
                  <span className="comparison-item__icon">✕</span>
                  <span>{item}</span>
                </li>
              ))}
            </ul>
          </div>

          <div className="comparison-card comparison-card--good">
            <div className="comparison-header">
              <span className="comparison-header__icon">⚡</span>
              <h3 className="comparison-header__title" style={{ color: '#34d399' }}>
                Avec WAZAP (100% WhatsApp)
              </h3>
            </div>
            <ul className="comparison-list">
              {COMPARISON_GOOD.map((item, idx) => (
                <li key={idx} className="comparison-item comparison-item--good">
                  <span className="comparison-item__icon">✓</span>
                  <span>{item}</span>
                </li>
              ))}
            </ul>
          </div>
        </div>
      </section>

      {/* Audience Toggle : Commerçants vs Livreurs */}
      <section className="landing-container">
        <div className="audience-tabs" role="tablist">
          <button
            type="button"
            className={`audience-tab ${audience === 'vendor' ? 'active' : ''}`}
            onClick={() => setAudience('vendor')}
            role="tab"
            aria-selected={audience === 'vendor'}
          >
            🏪 Vous êtes Commerçant / Vendeur
          </button>
          <button
            type="button"
            className={`audience-tab ${audience === 'rider' ? 'active' : ''}`}
            onClick={() => setAudience('rider')}
            role="tab"
            aria-selected={audience === 'rider'}
          >
            🛵 Vous êtes Livreur Indépendant
          </button>
        </div>

        {audience === 'vendor' ? (
          <div>
            <div className="section-head">
              <span className="landing-pill">POUR LES COMMERÇANTS ET RESTAURATEURS</span>
              <h2 className="section-head__title">Trois étapes simples. Zéro prise de tête.</h2>
              <p className="section-head__sub">
                Vous préparez vos colis, WAZAP s’occupe de trouver le coursier le plus proche et de rassurer votre client.
              </p>
            </div>

            <div className="steps-grid">
              <article className="step-card">
                <div className="step-card__num">01</div>
                <div className="step-card__icon">💬</div>
                <h3 className="step-card__title">Votre client commande</h3>
                <p className="step-card__desc">
                  Par message WhatsApp ordinaire (« 2 paires de chaussures à Cocody »). Aucune nouvelle habitude à faire adopter à votre clientèle.
                </p>
              </article>

              <article className="step-card">
                <div className="step-card__num">02</div>
                <div className="step-card__icon">🛵</div>
                <h3 className="step-card__title">Assignation automatique</h3>
                <p className="step-card__desc">
                  En un message ou un clic, l’algorithme WAZAP alerte les livreurs certifiés situés dans votre rayon de proximité immédiat.
                </p>
              </article>

              <article className="step-card">
                <div className="step-card__num">03</div>
                <div className="step-card__icon">📍</div>
                <h3 className="step-card__title">Suivi &amp; Remise sécurisée</h3>
                <p className="step-card__desc">
                  Votre client suit le trajet en direct sur son smartphone. La remise est validée par code secret PIN à 4 chiffres.
                </p>
              </article>
            </div>
          </div>
        ) : (
          <div>
            <div className="section-head">
              <span className="landing-pill">POUR LES LIVREURS INDÉPENDANTS</span>
              <h2 className="section-head__title">Des courses dans votre secteur, sans prospecter.</h2>
              <p className="section-head__sub">
                Recevez directement sur votre WhatsApp les demandes des commerces proches de votre position. Inscription 100% gratuite.
              </p>
            </div>

            <div className="steps-grid">
              <article className="step-card">
                <div className="step-card__num">01</div>
                <div className="step-card__icon">📍</div>
                <h3 className="step-card__title">Définissez votre zone</h3>
                <p className="step-card__desc">
                  Envoyez simplement votre commune ou votre position GPS par WhatsApp pour recevoir les alertes de courses à proximité immédiate.
                </p>
              </article>

              <article className="step-card">
                <div className="step-card__num">02</div>
                <div className="step-card__icon">💰</div>
                <h3 className="step-card__title">100% des frais de course pour vous</h3>
                <p className="step-card__desc">
                  Vous percevez l’intégralité des frais de livraison (1 000 à 2 000 FCFA) réglés directement par le vendeur ou l’acheteur.
                </p>
              </article>

              <article className="step-card">
                <div className="step-card__num">03</div>
                <div className="step-card__icon">⭐</div>
                <h3 className="step-card__title">Gagnez en réputation</h3>
                <p className="step-card__desc">
                  Chaque course réussie augmente votre note et votre priorité pour recevoir les meilleures courses des commerces de votre quartier.
                </p>
              </article>
            </div>
          </div>
        )}
      </section>

      {/* Témoignages Commerçants avec Avatars Réels */}
      <section className="landing-container" style={{ marginTop: 70 }}>
        <div className="section-head">
          <span className="landing-pill">ILS LIVRENT DÉJÀ AVEC WAZAP</span>
          <h2 className="section-head__title">Adopté par les commerçants d’Abidjan</h2>
          <p className="section-head__sub">
            Découvrez comment ils ont sécurisé leurs livraisons, éliminé le stress et fidélisé leur clientèle.
          </p>
        </div>

        <div className="testimonials-grid">
          {TESTIMONIALS.map((t, idx) => (
            <div key={idx} className="testimonial-card">
              <div>
                <div className="testimonial-header">
                  <img src={t.avatar} alt={t.name} className="testimonial-avatar" />
                  <div>
                    <h4 className="testimonial-info__name">{t.name}</h4>
                    <p className="testimonial-info__role">{t.role}</p>
                  </div>
                </div>
                <div className="testimonial-stars">{t.stars}</div>
                <p className="testimonial-quote">« {t.quote} »</p>
              </div>
              <div className="testimonial-tag">{t.tag}</div>
            </div>
          ))}
        </div>
      </section>

      {/* Simulator Section */}
      <section className="landing-container">
        <div className="section-head">
          <span className="landing-pill">SIMULATEUR DE RENTABILITÉ EN FCFA</span>
          <h2 className="section-head__title">Combien vous rapporte un service de livraison fluide ?</h2>
          <p className="section-head__sub">
            Ajustez votre volume de livraisons estimé pour découvrir le pack idéal et le temps économisé.
          </p>
        </div>

        <div className="simulator-box">
          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
              <span style={{ fontSize: 14, color: 'var(--wz-l-text-muted)', fontWeight: 600 }}>Volume quotidien de commandes :</span>
              <span style={{ fontSize: 32, fontWeight: 900, color: '#00d66c' }}>
                {dailyDeliveries} <span style={{ fontSize: 16 }}>courses/jour</span>
              </span>
            </div>

            <input
              type="range"
              min={2}
              max={80}
              step={1}
              value={dailyDeliveries}
              onChange={(e) => setDailyDeliveries(Number(e.target.value))}
              className="simulator-slider"
              aria-label="Nombre de livraisons par jour"
            />
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, color: 'var(--wz-l-text-dim)' }}>
              <span>2 courses/j</span>
              <span>40 courses/j</span>
              <span>80+ courses/j</span>
            </div>

            <div style={{ marginTop: 24, padding: 18, background: 'rgba(0, 214, 108, 0.08)', borderRadius: 14, border: '1px solid rgba(0, 214, 108, 0.2)' }}>
              <div style={{ fontSize: 12, textTransform: 'uppercase', color: '#00d66c', fontWeight: 800 }}>Pack Recommandé</div>
              <div style={{ fontSize: 22, fontWeight: 900, margin: '4px 0', color: '#fff' }}>{recommendedPack.name}</div>
              <div style={{ fontSize: 14, color: 'var(--wz-l-text-muted)' }}>
                {recommendedPack.credits} · {recommendedPack.price} · <strong style={{ color: '#34d399' }}>{recommendedPack.unit}</strong>
              </div>
            </div>
          </div>

          <div className="simulator-metrics">
            <div className="simulator-metric-row">
              <div>
                <div style={{ fontSize: 13, color: 'var(--wz-l-text-muted)' }}>Courses traitées / mois</div>
                <div style={{ fontSize: 24, fontWeight: 900 }}>~{monthlyOrders} livraisons</div>
              </div>
              <span style={{ fontSize: 24 }}>📦</span>
            </div>

            <div className="simulator-metric-row">
              <div>
                <div style={{ fontSize: 13, color: 'var(--wz-l-text-muted)' }}>Ventes sauvées (non perdues)</div>
                <div style={{ fontSize: 24, fontWeight: 900, color: '#00d66c' }}>+{savedOrders} ventes / mois</div>
              </div>
              <span style={{ fontSize: 24 }}>📈</span>
            </div>

            <div className="simulator-metric-row">
              <div>
                <div style={{ fontSize: 13, color: 'var(--wz-l-text-muted)' }}>Temps d’appels téléphoniques gagné</div>
                <div style={{ fontSize: 24, fontWeight: 900, color: '#f59e0b' }}>~{hoursSavedPerMonth} h / mois</div>
              </div>
              <span style={{ fontSize: 24 }}>⏱️</span>
            </div>
          </div>
        </div>
      </section>

      {/* Abidjan Communes Radar */}
      <section className="landing-container">
        <div className="section-head">
          <span className="landing-pill">RADAR DES COMMUNES COUVERTES</span>
          <h2 className="section-head__title">Disponible dans toute l’agglomération d’Abidjan</h2>
          <p className="section-head__sub">
            Cliquez sur votre quartier pour visualiser la disponibilité et les temps d’acheminement constatés.
          </p>
        </div>

        <div className="communes-grid">
          {Object.keys(COMMUNES).map((c) => (
            <button
              key={c}
              type="button"
              className={`commune-chip ${selectedCommune === c ? 'active' : ''}`}
              onClick={() => setSelectedCommune(c)}
            >
              📍 {c}
            </button>
          ))}
        </div>

        <div className="commune-card">
          <div>
            <div style={{ fontSize: 12, color: 'var(--wz-l-text-muted)', textTransform: 'uppercase', fontWeight: 700 }}>Assignation coursier</div>
            <div style={{ fontSize: 24, fontWeight: 900, color: '#00d66c', marginTop: 4 }}>{activeCommuneData.assignTime}</div>
          </div>
          <div style={{ width: 1, height: 40, background: 'var(--wz-l-border)' }} />
          <div>
            <div style={{ fontSize: 12, color: 'var(--wz-l-text-muted)', textTransform: 'uppercase', fontWeight: 700 }}>Délai moyen livraison</div>
            <div style={{ fontSize: 24, fontWeight: 900, color: '#fff', marginTop: 4 }}>~{activeCommuneData.deliveryTime}</div>
          </div>
          <div style={{ width: 1, height: 40, background: 'var(--wz-l-border)' }} />
          <div>
            <div style={{ fontSize: 12, color: 'var(--wz-l-text-muted)', textTransform: 'uppercase', fontWeight: 700 }}>Livreurs certifiés actifs</div>
            <div style={{ fontSize: 24, fontWeight: 900, color: '#f59e0b', marginTop: 4 }}>{activeCommuneData.ridersCount}+</div>
          </div>
        </div>
      </section>

      {/* Security & Colis Sûr Guarantee */}
      <section className="landing-container">
        <div className="colis-sur-card">
          <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 12 }}>
            <span style={{ fontSize: 32 }}>🛡️</span>
            <div>
              <h3 style={{ fontSize: 26, fontWeight: 900, margin: 0 }}>Garantie Colis Sûr WAZAP</h3>
              <p style={{ margin: '4px 0 0', color: 'var(--wz-l-text-muted)', fontSize: 14 }}>
                Le protocole de sécurité et d’indemnisation le plus rigoureux d’Abidjan.
              </p>
            </div>
          </div>

          <div className="colis-sur-grid">
            <div className="colis-sur-item">
              <div className="colis-sur-item__icon">🪪</div>
              <h4 style={{ margin: '0 0 6px', fontSize: 16 }}>Identité Vérifiée</h4>
              <p style={{ margin: 0, fontSize: 13, color: 'var(--wz-l-text-muted)' }}>
                Chaque coursier fournit une CNI ou Passeport valide, vérifié par nos équipes avant la première course.
              </p>
            </div>

            <div className="colis-sur-item">
              <div className="colis-sur-item__icon">🔢</div>
              <h4 style={{ margin: '0 0 6px', fontSize: 16 }}>Code PIN Secret</h4>
              <p style={{ margin: 0, fontSize: 13, color: 'var(--wz-l-text-muted)' }}>
                La course ne peut être clôturée que si le client destinataire transmet son code secret à 4 chiffres.
              </p>
            </div>

            <div className="colis-sur-item">
              <div className="colis-sur-item__icon">🚨</div>
              <h4 style={{ margin: '0 0 6px', fontSize: 16 }}>Suspension Immédiate</h4>
              <p style={{ margin: 0, fontSize: 13, color: 'var(--wz-l-text-muted)' }}>
                Un simple message « SINISTRE + code » bloque instantanément le livreur et ouvre l’enquête opérationnelle.
              </p>
            </div>

            <div className="colis-sur-item">
              <div className="colis-sur-item__icon">💸</div>
              <h4 style={{ margin: '0 0 6px', fontSize: 16 }}>Remboursement 48h</h4>
              <p style={{ margin: 0, fontSize: 13, color: 'var(--wz-l-text-muted)' }}>
                Crédit restitué et indemnisation par Mobile Money sous 48h en cas d’avarie ou perte confirmée.
              </p>
            </div>
          </div>
        </div>
      </section>

      {/* Onboarding Form */}
      <section id="inscription" className="landing-container">
        <div className="onboarding-card">
          {done ? (
            <div style={{ textAlign: 'center', padding: '20px 0' }}>
              <div style={{ fontSize: 52, marginBottom: 12 }}>🎉</div>
              <h2 style={{ fontSize: 28, fontWeight: 900, margin: '0 0 10px', color: '#00d66c' }}>
                Félicitations {contact || business} !
              </h2>
              <p style={{ color: 'var(--wz-l-text-muted)', fontSize: 16, lineHeight: 1.6, maxWidth: 480, margin: '0 auto 24px' }}>
                Votre demande a bien été enregistrée. Un responsable WAZAP vous contacte sur WhatsApp dans l’heure pour activer vos <strong>15 premières courses offertes</strong>.
              </p>

              {waLink(`Bonjour WAZAP 👋 Je viens de m’inscrire sur le site (${business}). Je souhaite activer mes 15 courses offertes immédiatement !`) && (
                <a
                  href={waLink(`Bonjour WAZAP 👋 Je viens de m’inscrire sur le site (${business}). Je souhaite activer mes 15 courses offertes immédiatement !`)!}
                  target="_blank"
                  rel="noreferrer"
                  className="landing-btn landing-btn--wa"
                  style={{ width: '100%', marginBottom: 12 }}
                >
                  💬 Ouvrir la conversation WhatsApp prioritaire
                </a>
              )}

              <button
                type="button"
                className="landing-btn landing-btn--ghost"
                style={{ width: '100%', fontSize: 14 }}
                onClick={() => {
                  setDone(false)
                  setBusiness('')
                  setContact('')
                  setPhone('')
                }}
              >
                Inscrire un autre commerce ou numéro
              </button>
            </div>
          ) : (
            <div>
              <div style={{ textAlign: 'center', marginBottom: 28 }}>
                <span className="landing-pill">ACTIVATION IMMÉDIATE</span>
                <h2 style={{ fontSize: 30, fontWeight: 900, margin: '10px 0 6px' }}>
                  {audience === 'vendor' ? 'Activez vos 15 livraisons offertes' : 'Rejoignez la flotte des livreurs WAZAP'}
                </h2>
                <p style={{ color: 'var(--wz-l-text-muted)', fontSize: 15 }}>
                  {audience === 'vendor'
                    ? 'Remplissez ce formulaire éclair. Aucune carte bancaire requise, sans engagement.'
                    : 'Recevez des courses dans votre commune sans abonnement.'}
                </p>
              </div>

              {audience === 'vendor' && (
                <div className="incentive-box">
                  <div className="incentive-box__icon">🎁</div>
                  <div>
                    <div className="incentive-box__title">Pack Découverte Réservé : 15 Livraisons Offertes</div>
                    <div className="incentive-box__sub">
                      Profitez de 15 courses sans débourser un centime. Sans engagement, sans carte bancaire.
                    </div>
                  </div>
                </div>
              )}

              {error && (
                <div style={{ background: 'rgba(239, 68, 68, 0.15)', border: '1px solid #ef4444', color: '#fca5a5', padding: '12px 16px', borderRadius: 10, fontSize: 14, marginBottom: 18 }}>
                  ⚠️ {error}
                </div>
              )}

              <label className="landing-label" htmlFor="business-name">
                {audience === 'vendor' ? 'Nom de votre commerce ou boutique *' : 'Votre prénom & nom complet *'}
              </label>
              <input
                id="business-name"
                className="landing-input"
                value={business}
                onChange={(e) => setBusiness(e.target.value)}
                placeholder={audience === 'vendor' ? 'Ex : Restaurant Chez Awa, Boutique Chic Abidjan' : 'Ex : Konan Kouassi Ibrahim'}
              />

              <label className="landing-label" htmlFor="commune-zone">
                Commune ou Quartier principal *
              </label>
              <select
                id="commune-zone"
                className="landing-input"
                value={zone}
                onChange={(e) => setZone(e.target.value)}
              >
                {Object.keys(COMMUNES).map((c) => (
                  <option key={c} value={c} style={{ background: '#0b1610', color: '#fff' }}>
                    {c}
                  </option>
                ))}
              </select>
              <div style={{ fontSize: 13, color: '#34d399', fontWeight: 700, marginTop: 8, display: 'flex', alignItems: 'center', gap: 6 }}>
                <span>🟢</span> {(COMMUNES[zone] || COMMUNES['Marcory']).ridersCount}+ livreurs certifiés actifs en ce moment à {zone}
              </div>

              <label className="landing-label" htmlFor="whatsapp-phone">
                Numéro WhatsApp joignable *
              </label>
              <input
                id="whatsapp-phone"
                className="landing-input"
                value={phone}
                onChange={(e) => setPhone(e.target.value)}
                placeholder="Ex : +225 07 01 02 03 04"
                inputMode="tel"
              />

              {audience === 'vendor' && (
                <>
                  <label className="landing-label" htmlFor="contact-person">
                    Nom du gérant ou responsable (facultatif)
                  </label>
                  <input
                    id="contact-person"
                    className="landing-input"
                    value={contact}
                    onChange={(e) => setContact(e.target.value)}
                    placeholder="Ex : Mme Awa Koné"
                  />
                </>
              )}

              <button
                type="button"
                className="landing-btn landing-btn--primary"
                style={{ width: '100%', marginTop: 24, padding: '16px 24px', fontSize: 16 }}
                disabled={busy}
                onClick={() => void submit()}
              >
                {busy ? 'Traitement de l’activation…' : '🚀 Valider et recevoir mes 15 courses gratuites'}
              </button>

              <div style={{ display: 'flex', justifyContent: 'center', gap: 16, marginTop: 14, fontSize: 12, color: 'var(--wz-l-text-dim)', flexWrap: 'wrap' }}>
                <span>🔒 100% Confidentiel</span>
                <span>•</span>
                <span>⚡ Activation en 5 min</span>
                <span>•</span>
                <span>💳 Zéro carte bancaire</span>
              </div>
            </div>
          )}
        </div>
      </section>

      {/* FAQ Accordion Section */}
      <section className="landing-container">
        <div className="section-head">
          <span className="landing-pill">QUESTIONS FRÉQUENTES</span>
          <h2 className="section-head__title">Tout ce que vous devez savoir</h2>
          <p className="section-head__sub">Des réponses limpides à vos interrogations avant de démarrer.</p>
        </div>

        <div className="faq-wrap">
          {FAQ_ITEMS.map((item) => (
            <details key={item.q} className="faq-item">
              <summary className="faq-summary">
                <span>{item.q}</span>
                <span style={{ fontSize: 18, color: '#00d66c' }}>▾</span>
              </summary>
              <div className="faq-content">{item.a}</div>
            </details>
          ))}
        </div>

        <div style={{ textAlign: 'center', marginTop: 32 }}>
          <Link
            to="/parrainage"
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: 8,
              padding: '12px 20px',
              borderRadius: 12,
              background: 'rgba(0, 214, 108, 0.1)',
              color: '#00d66c',
              border: '1px solid rgba(0, 214, 108, 0.25)',
              fontWeight: 700,
              fontSize: 14,
              textDecoration: 'none',
            }}
          >
            🎁 Déjà partenaire ? Parrainez un commerçant et gagnez +5 crédits →
          </Link>
        </div>
      </section>

      {/* Footer */}
      <footer style={{ marginTop: 80, borderTop: '1px solid var(--wz-l-border)', paddingTop: 36, textAlign: 'center' }}>
        <div className="landing-container">
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 10, marginBottom: 12 }}>
            <span style={{ fontSize: 20 }}>⚡</span>
            <span style={{ fontWeight: 900, fontSize: 18, color: '#fff' }}>WAZAP CÔTE D&apos;IVOIRE</span>
          </div>
          <p style={{ color: 'var(--wz-l-text-dim)', fontSize: 13, maxWidth: 520, margin: '0 auto 16px' }}>
            Plateforme de mise en relation express pour la livraison à Abidjan. Conforme à la réglementation ivoirienne des transports et du commerce électronique.
          </p>
          <div style={{ fontSize: 12, color: 'var(--wz-l-text-dim)' }}>
            © {new Date().getFullYear()} WAZAP · Fait avec passion pour le commerce abidjanais.
          </div>
        </div>
      </footer>

      {/* Sticky Bottom Bar on Mobile */}
      <div className="landing-sticky-bar">
        <a href="#inscription" className="landing-btn landing-btn--primary" style={{ flex: 1, padding: '12px 14px', fontSize: 14 }}>
          🚀 15 courses offertes
        </a>
        {waLink('Bonjour WAZAP 👋') && (
          <a
            href={waLink('Bonjour WAZAP 👋')!}
            target="_blank"
            rel="noreferrer"
            className="landing-btn landing-btn--wa"
            style={{ padding: '12px 16px', fontSize: 16 }}
            aria-label="Contacter sur WhatsApp"
          >
            💬
          </a>
        )}
      </div>
    </div>
  )
}
