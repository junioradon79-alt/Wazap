import { useCallback, useEffect, useRef, useState } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import type { ClientOrderStatus, ClientPaymentResponse } from '../api/types'
import BrandLogo from '../components/BrandLogo'
import '../styles/suivi.css'

const STATUS_DETAILS: Record<string, { label: string; sub: string; step: number }> = {
  PendingVendorConfirmation: {
    label: 'En attente du vendeur',
    sub: 'Le commerçant vérifie la disponibilité de vos articles.',
    step: 1,
  },
  VendorConfirmed: {
    label: 'Commande confirmée',
    sub: 'Le commerçant a validé vos articles. Vos repères de livraison sont requis.',
    step: 1,
  },
  AwaitingRiderAcceptance: {
    label: 'Recherche d’un livreur…',
    sub: 'Nous mobilisons les livreurs certifiés les plus proches à Abidjan.',
    step: 2,
  },
  RiderAssigned: {
    label: 'Livreur assigné',
    sub: 'Votre livreur se dirige vers le commerçant pour récupérer votre colis.',
    step: 3,
  },
  ReadyForPickup: {
    label: 'Colis prêt',
    sub: 'Le commerçant a emballé votre colis. Prêt pour le départ.',
    step: 3,
  },
  PickedUp: {
    label: 'Colis récupéré',
    sub: 'Le livreur a pris en charge votre colis et démarre la livraison.',
    step: 4,
  },
  InTransit: {
    label: 'En cours de livraison',
    sub: 'Votre livreur est en route vers votre adresse.',
    step: 4,
  },
  Delivered: {
    label: 'Colis livré avec succès ✓',
    sub: 'Votre commande a été remise en main propre. Merci pour votre confiance !',
    step: 5,
  },
  Cancelled: {
    label: 'Commande annulée',
    sub: 'Cette commande a été annulée.',
    step: 0,
  },
}

function calculateDistanceAndEta(
  lat1: number | null | undefined,
  lng1: number | null | undefined,
  lat2: number | null | undefined,
  lng2: number | null | undefined
) {
  if (lat1 == null || lng1 == null || lat2 == null || lng2 == null) return null
  const R = 6371
  const dLat = ((lat2 - lat1) * Math.PI) / 180
  const dLon = ((lng2 - lng1) * Math.PI) / 180
  const a =
    Math.sin(dLat / 2) * Math.sin(dLat / 2) +
    Math.cos((lat1 * Math.PI) / 180) *
      Math.cos((lat2 * Math.PI) / 180) *
      Math.sin(dLon / 2) *
      Math.sin(dLon / 2)
  const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a))
  const km = R * c
  const distanceText = km < 1 ? `${Math.round(km * 1000)} m` : `${km.toFixed(1)} km`
  const minutes = Math.max(2, Math.round((km / 22) * 60))
  const etaText = `~${minutes} min`
  return { distanceText, etaText }
}

const DEMO_ORDER: ClientOrderStatus = {
  id: 'demo',
  code: 'WZ8942',
  vendorName: 'Boutique Wax Élégance (Plateau)',
  status: 'InTransit',
  description: '2x Boubous brodés haut de gamme + 1 Foulard en soie',
  amount: 35000,
  deliveryFee: 1500,
  totalAmount: 36500,
  deliveryCode: '7492',
  riderPhone: '+2250700000002',
  needsCoordinates: false,
  hasCoordinates: true,
  address: 'Cocody Riviera Bonoumin, carrefour pharmacie Ste-Marie',
  riderAssigned: true,
  delivered: false,
  orderLines: [
    { productName: 'Boubou brodé grand modèle', quantity: 2, unitPrice: 15000, totalPrice: 30000 },
    { productName: 'Foulard soie assorti', quantity: 1, unitPrice: 5000, totalPrice: 5000 },
  ],
  payment: {
    status: 'Pending',
    amount: 36500,
    paymentLink: 'https://pay.wazap.ci/demo-wave',
  },
}

const DEMO_RIDER = {
  riderName: 'Amara Fofana',
  location: { latitude: 5.352, longitude: -3.985 },
}

export default function SuiviPage() {
  const { id } = useParams<{ id: string }>()
  const [order, setOrder] = useState<ClientOrderStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [lat, setLat] = useState<number | null>(null)
  const [lng, setLng] = useState<number | null>(null)
  const [address, setAddress] = useState('')
  const [geoErr, setGeoErr] = useState<string | null>(null)
  const [sending, setSending] = useState(false)
  const [sent, setSent] = useState(false)
  const [rider, setRider] = useState<{ riderName?: string; location?: { latitude: number; longitude: number } | null } | null>(null)
  const [paying, setPaying] = useState(false)
  const [payErr, setPayErr] = useState<string | null>(null)
  const [copiedKey, setCopiedKey] = useState<string | null>(null)
  const [lightboxOpen, setLightboxOpen] = useState(false)

  // Rating state
  const [ratingScore, setRatingScore] = useState<number>(5)
  const [ratingComment, setRatingComment] = useState('')
  const [ratingSubmitting, setRatingSubmitting] = useState(false)
  const [ratingDone, setRatingDone] = useState(false)
  const [ratingErr, setRatingErr] = useState<string | null>(null)

  // Rider QR validation state
  const [searchParams] = useSearchParams()
  const isRiderValidationMode = searchParams.get('valider') === '1'
  const paramCode = searchParams.get('code') || ''
  const [codeToValidate, setCodeToValidate] = useState(paramCode)
  const [validatingDelivery, setValidatingDelivery] = useState(false)
  const [validationSuccess, setValidationSuccess] = useState<string | null>(null)
  const [validationErr, setValidationErr] = useState<string | null>(null)

  useEffect(() => {
    if (paramCode) setCodeToValidate(paramCode)
  }, [paramCode])

  const timer = useRef<ReturnType<typeof setInterval> | null>(null)

  const isDemo = id === 'demo' || id === 'demo-livre'

  const fetchOrder = useCallback(async () => {
    if (id === 'demo' || id === 'demo-livre') {
      const isDelivered = id === 'demo-livre'
      setOrder({
        ...DEMO_ORDER,
        id,
        status: isDelivered ? 'Delivered' : 'InTransit',
        delivered: isDelivered,
        hasProofPhoto: isDelivered,
      })
      setSent(true)
      setRider(DEMO_RIDER)
      return
    }
    try {
      const o = await api.get<ClientOrderStatus>(`/client/orders/${id}`)
      setOrder(o)
      setSent(o.hasCoordinates)
      if (o.rating) {
        setRatingDone(true)
        setRatingScore(o.rating.score)
        setRatingComment(o.rating.comment || '')
      }
      const active = ['RiderAssigned', 'ReadyForPickup', 'PickedUp', 'InTransit'].includes(o.status)
      if (active) {
        try {
          const r = await api.get<{ riderName?: string; location?: { latitude: number; longitude: number } | null }>(
            `/client/orders/${id}/rider-location`
          )
          setRider(r)
        } catch {
          /* silencieux */
        }
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Erreur lors du chargement de la commande')
    }
  }, [id])

  useEffect(() => {
    fetchOrder()
    return () => {
      if (timer.current) clearInterval(timer.current)
    }
  }, [fetchOrder])

  const startPolling = useCallback(() => {
    if (timer.current) clearInterval(timer.current)
    timer.current = setInterval(async () => {
      try {
        const o = await api.get<ClientOrderStatus>(`/client/orders/${id}`)
        setOrder(o)
        if (o.delivered && timer.current) clearInterval(timer.current)
        const active = ['RiderAssigned', 'ReadyForPickup', 'PickedUp', 'InTransit'].includes(o.status)
        if (active) {
          try {
            const r = await api.get<{ riderName?: string; location?: { latitude: number; longitude: number } | null }>(
              `/client/orders/${id}/rider-location`
            )
            setRider(r)
          } catch {
            /* silencieux */
          }
        }
      } catch {
        /* silencieux */
      }
    }, 5000)
  }, [id])

  const tracking = Boolean(order?.hasCoordinates) && !order?.delivered && order?.status !== 'Cancelled'

  useEffect(() => {
    if (!tracking) return
    startPolling()
    return () => {
      if (timer.current) clearInterval(timer.current)
    }
  }, [tracking, startPolling])

  const copyText = (text: string, key: string) => {
    if (navigator.clipboard) {
      navigator.clipboard.writeText(text)
    }
    setCopiedKey(key)
    setTimeout(() => setCopiedKey(null), 2500)
  }

  const useGeo = () => {
    setGeoErr(null)
    if (!navigator.geolocation) {
      setGeoErr('Géolocalisation non supportée par votre navigateur — veuillez saisir votre repère.')
      return
    }
    navigator.geolocation.getCurrentPosition(
      (p) => {
        setLat(p.coords.latitude)
        setLng(p.coords.longitude)
      },
      () => setGeoErr('Position impossible à récupérer. Décrivez votre carrefour ou quartier ci-dessous.')
    )
  }

  const submitCoordinates = async () => {
    if (lat == null || lng == null) {
      setGeoErr('Veuillez activer votre position GPS avant de valider.')
      return
    }
    setSending(true)
    setGeoErr(null)
    try {
      await api.post<{ status: string }>(`/client/orders/${id}/coordinates`, {
        latitude: lat,
        longitude: lng,
        address: address || null,
      })
      setSent(true)
      await fetchOrder()
      startPolling()
    } catch (e) {
      setGeoErr(e instanceof Error ? e.message : 'Erreur d’enregistrement des coordonnées')
    } finally {
      setSending(false)
    }
  }

  const submitRating = async () => {
    if (!id) return
    setRatingSubmitting(true)
    setRatingErr(null)
    if (isDemo) {
      setTimeout(() => {
        setRatingDone(true)
        setRatingSubmitting(false)
      }, 400)
      return
    }
    try {
      await api.post(`/client/orders/${id}/rate`, {
        score: ratingScore,
        comment: ratingComment.trim() || null,
      })
      setRatingDone(true)
      await fetchOrder()
    } catch (e) {
      setRatingErr(e instanceof Error ? e.message : 'Erreur lors de l’envoi de votre avis')
    } finally {
      setRatingSubmitting(false)
    }
  }

  const pay = async () => {
    if (isDemo) {
      window.open('https://pay.wazap.ci/demo-wave', '_blank')
      return
    }
    setPaying(true)
    setPayErr(null)
    try {
      await api.post<ClientPaymentResponse>(`/client/orders/${id}/pay`)
      await fetchOrder()
    } catch (e) {
      setPayErr(e instanceof Error ? e.message : 'Erreur d’initialisation du paiement')
    } finally {
      setPaying(false)
    }
  }

  const handleValidateDelivery = async () => {
    if (!id || !codeToValidate) return
    setValidatingDelivery(true)
    setValidationErr(null)
    setValidationSuccess(null)
    try {
      if (isDemo) {
        setValidationSuccess('Livraison validée avec succès ! (Mode démo)')
        if (order) {
          setOrder({ ...order, status: 'Delivered', delivered: true })
        }
        return
      }
      await api.post(`/client/orders/${id}/validate-delivery`, { code: codeToValidate })
      setValidationSuccess('Livraison validée avec succès ! Le vendeur a été notifié pour votre règlement.')
      await fetchOrder()
    } catch (e) {
      setValidationErr(e instanceof Error ? e.message : 'Code PIN incorrect ou erreur de validation.')
    } finally {
      setValidatingDelivery(false)
    }
  }

  if (error) {
    return (
      <div className="suivi-page-wrapper">
        <div className="suivi-container">
          <div className="suivi-error-msg" style={{ padding: '24px', textAlign: 'center', marginTop: '40px' }}>
            <h3 style={{ margin: '0 0 8px', fontSize: '18px' }}>⚠️ Impossible de charger la commande</h3>
            <p style={{ margin: 0, fontSize: '14px' }}>{error}</p>
          </div>
        </div>
      </div>
    )
  }

  if (!order) {
    return (
      <div className="suivi-page-wrapper">
        <div className="suivi-container" style={{ textAlign: 'center', paddingTop: '100px' }}>
          <div className="suivi-live-dot" style={{ margin: '0 auto 16px', width: '20px', height: '20px' }} />
          <p style={{ color: 'var(--suivi-text-muted)', fontSize: '16px', fontWeight: 600 }}>
            Connexion au suivi WAZAP en direct…
          </p>
        </div>
      </div>
    )
  }

  const statusMeta = STATUS_DETAILS[order.status] || {
    label: order.status,
    sub: 'Statut de livraison en cours de mise à jour.',
    step: 2,
  }

  const currentStep = order.delivered ? 5 : order.status === 'Cancelled' ? 0 : statusMeta.step

  const distanceInfo = calculateDistanceAndEta(
    lat ?? 5.3484, // Abidjan fallback lat
    lng ?? -4.0175,
    rider?.location?.latitude,
    rider?.location?.longitude
  )

  const quickReviewTags = ['⚡ Rapide & efficace', '👍 Très poli', '📦 Colis impeccable', '⏰ Ponctuel']

  const proofPhotoUrl = `/api/client/orders/${order.id}/proof-photo`

  return (
    <div className="suivi-page-wrapper">
      <div className="suivi-container">
        {/* TOP BAR */}
        <header className="suivi-topbar">
          <div className="suivi-brand" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <BrandLogo size="sm" variant="inline" showTagline={false} />
            <span style={{ color: 'var(--suivi-text-muted)', fontWeight: 600, fontSize: '13px', background: 'rgba(255, 255, 255, 0.06)', padding: '3px 8px', borderRadius: '6px' }}>
              Suivi
            </span>
          </div>
          <div className="suivi-live-tag">
            <span className="suivi-live-dot" />
            <span>En Direct</span>
          </div>
        </header>

        {/* RIDER VALIDATION BANNER (when accessed with ?valider=1) */}
        {isRiderValidationMode && !order.delivered && (
          <section className="suivi-rider-validation-card" style={{
            background: 'linear-gradient(135deg, rgba(245, 158, 11, 0.15), rgba(0, 214, 108, 0.12))',
            border: '2px solid var(--suivi-gold)',
            borderRadius: '16px',
            padding: '20px',
            marginBottom: '20px',
            textAlign: 'center'
          }}>
            <div style={{ fontSize: '26px', marginBottom: '6px' }}>🛵⚡</div>
            <h3 style={{ margin: '0 0 6px', fontSize: '18px', color: '#fff', fontWeight: 800 }}>
              Espace Livreur — Validation de Remise
            </h3>
            <p style={{ margin: '0 0 14px', fontSize: '13px', color: 'var(--suivi-text-muted)' }}>
              Vous avez scanné le QR Code client. Confirmez le code PIN pour clôturer la livraison et déclencher le règlement de votre course.
            </p>

            <div style={{ display: 'flex', justifyContent: 'center', gap: '8px', marginBottom: '14px' }}>
              <input
                type="text"
                maxLength={4}
                value={codeToValidate}
                onChange={(e) => setCodeToValidate(e.target.value.trim())}
                placeholder="Code à 4 chiffres"
                style={{
                  width: '180px',
                  textAlign: 'center',
                  fontSize: '22px',
                  fontWeight: 800,
                  letterSpacing: '6px',
                  padding: '10px',
                  borderRadius: '10px',
                  border: '1px solid rgba(255,255,255,0.3)',
                  background: 'rgba(0,0,0,0.4)',
                  color: '#fff'
                }}
              />
            </div>

            {validationErr && (
              <div style={{ color: '#ef4444', fontSize: '13px', marginBottom: '12px', fontWeight: 600 }}>
                ⚠️ {validationErr}
              </div>
            )}

            {validationSuccess && (
              <div style={{ color: 'var(--suivi-emerald)', fontSize: '14px', fontWeight: 700, marginBottom: '12px' }}>
                ✓ {validationSuccess}
              </div>
            )}

            <button
              type="button"
              className="suivi-btn-validate"
              disabled={validatingDelivery || codeToValidate.length < 4}
              onClick={handleValidateDelivery}
              style={{ width: '100%', maxWidth: '320px', margin: '0 auto', display: 'block' }}
            >
              {validatingDelivery ? 'Validation en cours…' : 'Valider la livraison effective ✓'}
            </button>
          </section>
        )}

        {/* HERO STATUS CARD */}
        <section className="suivi-hero-card">
          <div className="suivi-hero-top">
            <div className="suivi-order-num">
              <span>COMMANDE #{order.code}</span>
              <button
                type="button"
                className="suivi-copy-btn"
                onClick={() => copyText(order.code, 'code')}
              >
                {copiedKey === 'code' ? 'Copié !' : 'Copier'}
              </button>
            </div>
            {((order.amount ?? 0) > 0 || (order.deliveryFee ?? 0) > 0) && (
              <div style={{ display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
                <span
                  title="Prix de la marchandise vendue"
                  style={{
                    background: 'rgba(0, 214, 108, 0.12)',
                    color: 'var(--suivi-emerald)',
                    fontWeight: 700,
                    fontSize: '12px',
                    padding: '4px 9px',
                    borderRadius: '8px',
                  }}
                >
                  📦 {(order.amount ?? 0).toLocaleString('fr-FR')} F
                </span>
                <span
                  title="Frais de livraison dus au livreur"
                  style={{
                    background: 'rgba(245, 158, 11, 0.15)',
                    color: 'var(--suivi-gold)',
                    fontWeight: 700,
                    fontSize: '12px',
                    padding: '4px 9px',
                    borderRadius: '8px',
                  }}
                >
                  🛵 {(order.deliveryFee ?? 1000).toLocaleString('fr-FR')} F
                </span>
              </div>
            )}
          </div>

          <h1 className="suivi-hero-status">{order.delivered ? 'Livré ✓' : statusMeta.label}</h1>
          <p className="suivi-hero-sub">{statusMeta.sub}</p>

          {/* ACTION BUTTONS: WhatsApp Share & Quick Call */}
          <div style={{ display: 'flex', gap: '10px', marginTop: '16px', flexWrap: 'wrap' }}>
            <a
              href={`https://wa.me/?text=${encodeURIComponent(
                `Bonjour ! Suivez ma commande #${order.code || order.id} en direct sur WAZAP ici :\n${typeof window !== 'undefined' ? window.location.href : ''}\nCode secret de remise : ${order.deliveryCode || 'fourni à l\'arrivée'}`
              )}`}
              target="_blank"
              rel="noreferrer"
              className="suivi-btn-nav"
              style={{
                background: 'linear-gradient(135deg, #25D366, #128C7E)',
                color: '#fff',
                textDecoration: 'none',
                flex: 1,
                minWidth: '200px',
                textAlign: 'center',
                padding: '10px 16px',
                borderRadius: '10px',
                fontWeight: 700,
                fontSize: '13px',
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                gap: '8px',
                boxShadow: '0 4px 14px rgba(37, 211, 102, 0.35)',
              }}
            >
              <span>📱 Partager le suivi sur WhatsApp</span>
            </a>

            {order.riderPhone && !order.delivered && (
              <a
                href={`tel:${order.riderPhone}`}
                className="suivi-btn-nav"
                style={{
                  background: 'rgba(255, 255, 255, 0.1)',
                  color: '#fff',
                  border: '1px solid rgba(255, 255, 255, 0.2)',
                  textDecoration: 'none',
                  padding: '10px 16px',
                  borderRadius: '10px',
                  fontWeight: 700,
                  fontSize: '13px',
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  gap: '8px',
                }}
              >
                <span>📞 Appeler le livreur</span>
              </a>
            )}
          </div>

          {/* STEPPER TIMELINE */}
          {order.status !== 'Cancelled' && (
            <div className="suivi-stepper">
              {/* Step 1 */}
              <div className="suivi-step-row">
                <div className={`suivi-step-line ${currentStep > 1 ? 'completed' : ''}`} />
                <div className={`suivi-step-node ${currentStep > 1 ? 'completed' : currentStep === 1 ? 'active' : 'pending'}`}>
                  {currentStep > 1 ? '✓' : '1'}
                </div>
                <div className="suivi-step-content">
                  <div className="suivi-step-title">Commande confirmée</div>
                  <div className="suivi-step-desc">Vendeur : {order.vendorName || 'Boutique'}</div>
                </div>
              </div>

              {/* Step 2 */}
              <div className="suivi-step-row">
                <div className={`suivi-step-line ${currentStep > 2 ? 'completed' : ''}`} />
                <div className={`suivi-step-node ${currentStep > 2 ? 'completed' : currentStep === 2 ? 'active' : 'pending'}`}>
                  {currentStep > 2 ? '✓' : '2'}
                </div>
                <div className="suivi-step-content">
                  <div className="suivi-step-title">Adresse & Repères GPS</div>
                  <div className="suivi-step-desc">
                    {order.hasCoordinates ? (order.address || 'Coordonnées validées') : 'Validation client requise'}
                  </div>
                </div>
              </div>

              {/* Step 3 */}
              <div className="suivi-step-row">
                <div className={`suivi-step-line ${currentStep > 3 ? 'completed' : ''}`} />
                <div className={`suivi-step-node ${currentStep > 3 ? 'completed' : currentStep === 3 ? 'active' : 'pending'}`}>
                  {currentStep > 3 ? '✓' : '3'}
                </div>
                <div className="suivi-step-content">
                  <div className="suivi-step-title">Livreur assigné</div>
                  <div className="suivi-step-desc">
                    {rider?.riderName || order.riderPhone ? `${rider?.riderName || 'Livreur'} en route` : 'Recherche automatique…'}
                  </div>
                </div>
              </div>

              {/* Step 4 */}
              <div className="suivi-step-row">
                <div className={`suivi-step-line ${currentStep > 4 ? 'completed' : ''}`} />
                <div className={`suivi-step-node ${currentStep > 4 ? 'completed' : currentStep === 4 ? 'active' : 'pending'}`}>
                  {currentStep > 4 ? '✓' : '4'}
                </div>
                <div className="suivi-step-content">
                  <div className="suivi-step-title">Colis récupéré & en route</div>
                  <div className="suivi-step-desc">Acheminement vers votre point de livraison</div>
                </div>
              </div>

              {/* Step 5 */}
              <div className="suivi-step-row">
                <div className={`suivi-step-node ${currentStep === 5 ? 'completed' : 'pending'}`}>
                  {currentStep === 5 ? '✓' : '5'}
                </div>
                <div className="suivi-step-content">
                  <div className="suivi-step-title">Colis livré</div>
                  <div className="suivi-step-desc">Remise sécurisée en main propre</div>
                </div>
              </div>
            </div>
          )}
        </section>

        {/* ONBOARDING COORDINATES FORM (when needed) */}
        {order.needsCoordinates && !order.hasCoordinates && !sent && (
          <section className="suivi-geo-card">
            <h2 style={{ margin: '0 0 8px', fontSize: '18px', fontWeight: 800 }}>
              📍 Où souhaitez-vous être livré ?
            </h2>
            <p style={{ margin: '0 0 16px', fontSize: '13px', color: 'var(--suivi-text-muted)', lineHeight: 1.4 }}>
              Activez votre position GPS en 1 clic pour que le livreur vienne directement à votre porte, même sans nom de rue.
            </p>

            <button type="button" className="suivi-btn-geo" onClick={useGeo}>
              <span>📍 Détecter ma position GPS exacte</span>
            </button>

            {lat != null && lng != null && (
              <div
                style={{
                  background: 'rgba(0, 214, 108, 0.12)',
                  color: 'var(--suivi-emerald)',
                  borderRadius: '10px',
                  padding: '10px 12px',
                  fontSize: '13px',
                  fontWeight: 600,
                  marginTop: '12px',
                  border: '1px solid rgba(0, 214, 108, 0.3)',
                }}
              >
                ✓ GPS détecté : {lat.toFixed(5)}, {lng.toFixed(5)}
              </div>
            )}

            <label style={{ display: 'block', fontSize: '13px', fontWeight: 700, marginTop: '16px', color: '#fff' }}>
              Repère ou quartier (facultatif mais recommandé) :
            </label>
            <input
              className="suivi-input"
              value={address}
              onChange={(e) => setAddress(e.target.value)}
              placeholder="Ex : Marcory, rue Princesse, face à la pharmacie…"
            />

            {geoErr && <div className="suivi-error-msg">{geoErr}</div>}

            <button
              type="button"
              className="suivi-btn-validate"
              disabled={sending}
              onClick={submitCoordinates}
              style={{ opacity: sending ? 0.7 : 1 }}
            >
              {sending ? 'Recherche des livreurs…' : 'Confirmer et lancer la livraison ⚡'}
            </button>
          </section>
        )}

        {/* SECRET DELIVERY PIN (COLIS SÛR) */}
        {order.deliveryCode && !order.delivered && (
          <section className="suivi-pin-card">
            <div className="suivi-pin-header">
              <div className="suivi-pin-title">
                <span>🔒 Code Secret de Remise</span>
              </div>
              <button
                type="button"
                className="suivi-copy-btn"
                style={{ background: 'rgba(247, 201, 72, 0.15)', borderColor: 'var(--suivi-gold)', color: 'var(--suivi-gold)' }}
                onClick={() => copyText(order.deliveryCode!, 'pin')}
              >
                {copiedKey === 'pin' ? 'Copié !' : 'Copier le code'}
              </button>
            </div>

            <div className="suivi-pin-boxes">
              {order.deliveryCode.split('').map((digit, idx) => (
                <div key={idx} className="suivi-pin-digit">
                  {digit}
                </div>
              ))}
            </div>

            {/* QR Code pour scan direct par le livreur */}
            <div style={{ textAlign: 'center', margin: '18px 0 14px' }}>
              <div style={{ fontSize: '12px', color: 'var(--suivi-text-muted)', marginBottom: '8px' }}>
                📷 Ou faites scanner ce QR Code par votre livreur à l’arrivée :
              </div>
              <div style={{ display: 'inline-block', padding: '10px', background: '#fff', borderRadius: '12px', boxShadow: '0 4px 16px rgba(0,0,0,0.35)' }}>
                <img
                  src={order.qrUrl || `/api/client/orders/${order.id}/qr`}
                  alt="QR Code de livraison"
                  style={{ width: '150px', height: '150px', display: 'block' }}
                />
              </div>
            </div>

            <div className="suivi-pin-note">
              <strong>Garantie Colis Sûr :</strong> Ne montrez ce QR Code ou ne communiquez ce code PIN à votre livreur qu’au moment précis où il vous remet le colis en main propre.
            </div>
          </section>
        )}

        {/* RADAR & LIVE MAP CARD (when rider location is available) */}
        {rider && (
          <section className="suivi-map-card">
            <div className="suivi-radar-header">
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <span style={{ fontSize: '18px' }}>🛵</span>
                <span style={{ fontWeight: 800, fontSize: '15px' }}>{rider.riderName || 'Livreur en approche'}</span>
              </div>
              {distanceInfo && (
                <div className="suivi-radar-badge">
                  <span>📍 {distanceInfo.distanceText}</span>
                  <span>•</span>
                  <span>{distanceInfo.etaText}</span>
                </div>
              )}
            </div>

            {rider.location ? (
              <>
                <div className="suivi-map-frame-box">
                  <iframe
                    title="Carte de livraison"
                    src={`https://www.openstreetmap.org/export/embed.html?bbox=${rider.location.longitude - 0.01}%2C${rider.location.latitude - 0.01}%2C${rider.location.longitude + 0.01}%2C${rider.location.latitude + 0.01}&layer=mapnik&marker=${rider.location.latitude}%2C${rider.location.longitude}`}
                    loading="lazy"
                  />
                </div>

                <div className="suivi-map-actions">
                  <a
                    href={`https://www.google.com/maps/search/?api=1&query=${rider.location.latitude},${rider.location.longitude}`}
                    target="_blank"
                    rel="noreferrer"
                    className="suivi-btn-nav suivi-btn-gmaps"
                  >
                    🗺️ Google Maps
                  </a>
                  <a
                    href={`https://waze.com/ul?ll=${rider.location.latitude},${rider.location.longitude}&navigate=yes`}
                    target="_blank"
                    rel="noreferrer"
                    className="suivi-btn-nav suivi-btn-waze"
                  >
                    🚗 Waze
                  </a>
                </div>
              </>
            ) : (
              <p style={{ margin: 0, fontSize: '13px', color: 'var(--suivi-text-muted)' }}>
                Position GPS en cours de synchronisation avec le smartphone du livreur…
              </p>
            )}
          </section>
        )}

        {/* RIDER PROFILE & TIP CARD */}
        {(rider?.riderName || order.riderPhone) && !order.delivered && (
          <section className="suivi-rider-card">
            <div className="suivi-rider-row">
              <div className="suivi-rider-info">
                <div className="suivi-rider-avatar">🛵</div>
                <div>
                  <h3 className="suivi-rider-name">{rider?.riderName || 'Livreur WAZAP'}</h3>
                  <div className="suivi-rider-badge">Identité Vérifiée ⚡</div>
                </div>
              </div>
            </div>

            <div className="suivi-rider-actions">
              {order.riderPhone && (
                <>
                  <a href={`tel:${order.riderPhone}`} className="suivi-btn-contact suivi-btn-call">
                    📞 Appeler
                  </a>
                  <a
                    href={`https://wa.me/${order.riderPhone.replace(/\D/g, '')}`}
                    target="_blank"
                    rel="noreferrer"
                    className="suivi-btn-contact suivi-btn-wa"
                  >
                    💬 WhatsApp
                  </a>
                </>
              )}
            </div>

            {order.riderPhone && (
              <div className="suivi-tip-box">
                <div className="suivi-tip-header">
                  <span>💚 Pourboire au livreur (Wave / OM)</span>
                  <button
                    type="button"
                    className="suivi-copy-btn"
                    onClick={() => copyText(order.riderPhone!, 'tip')}
                  >
                    {copiedKey === 'tip' ? 'Copié !' : 'Copier numéro'}
                  </button>
                </div>
                <div>Un geste apprécié pour encourager votre livreur sur la route.</div>
                <div className="suivi-tip-phone">
                  <span>Numéro Mobile Money :</span>
                  <span>{order.riderPhone}</span>
                </div>
              </div>
            )}
          </section>
        )}

        {/* PROOF PHOTO CARD (When delivered or photo exists) */}
        {order.hasProofPhoto && (
          <section className="suivi-proof-card">
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
              <span style={{ fontWeight: 800, fontSize: '14px', color: 'var(--suivi-emerald)' }}>
                📸 Preuve Photo de Livraison
              </span>
              <span style={{ fontSize: '12px', color: 'var(--suivi-text-muted)' }}>Garantie Colis Sûr</span>
            </div>

            <div className="suivi-proof-thumb-box" onClick={() => setLightboxOpen(true)}>
              <img
                src={proofPhotoUrl}
                alt="Preuve de livraison prise par le livreur"
                loading="lazy"
                onError={(e) => {
                  (e.target as HTMLElement).style.display = 'none'
                }}
              />
              <div className="suivi-proof-badge">🔍 Agrandir la photo</div>
            </div>
          </section>
        )}

        {/* LIGHTBOX MODAL */}
        {lightboxOpen && (
          <div className="suivi-modal-overlay" onClick={() => setLightboxOpen(false)}>
            <div className="suivi-modal-content" onClick={(e) => e.stopPropagation()}>
              <button
                type="button"
                className="suivi-modal-close"
                onClick={() => setLightboxOpen(false)}
                aria-label="Fermer la photo"
              >
                ✕
              </button>
              <img src={proofPhotoUrl} alt="Preuve de livraison plein écran" />
            </div>
          </div>
        )}

        {/* POST-DELIVERY 5-STAR RATING WIDGET */}
        {order.delivered && (
          <section className="suivi-rating-card">
            <h3 style={{ margin: 0, fontSize: '16px', fontWeight: 800, textAlign: 'center', color: '#fff' }}>
              {ratingDone ? '⭐ Merci pour votre avis !' : '⭐ Notez votre livraison'}
            </h3>
            <p style={{ margin: '4px 0 14px', fontSize: '13px', textAlign: 'center', color: 'var(--suivi-text-muted)' }}>
              {ratingDone
                ? 'Votre retour aide nos livreurs certifiés à maintenir un service exemplaire.'
                : 'Comment s’est déroulée votre course avec le livreur ?'}
            </p>

            <div className="suivi-rating-stars">
              {[1, 2, 3, 4, 5].map((star) => (
                <button
                  key={star}
                  type="button"
                  className={`suivi-star-btn ${star <= ratingScore ? 'suivi-star-gold' : ''}`}
                  disabled={ratingDone || ratingSubmitting}
                  onClick={() => setRatingScore(star)}
                  aria-label={`${star} étoile${star > 1 ? 's' : ''}`}
                >
                  {star <= ratingScore ? '⭐' : '☆'}
                </button>
              ))}
            </div>

            {!ratingDone && (
              <>
                <div className="suivi-chips">
                  {quickReviewTags.map((tag) => (
                    <button
                      key={tag}
                      type="button"
                      className={`suivi-chip ${ratingComment.includes(tag) ? 'selected' : ''}`}
                      onClick={() => {
                        if (ratingComment.includes(tag)) {
                          setRatingComment((prev) => prev.replace(tag, '').trim())
                        } else {
                          setRatingComment((prev) => (prev ? `${prev} • ${tag}` : tag))
                        }
                      }}
                    >
                      {tag}
                    </button>
                  ))}
                </div>

                <textarea
                  className="suivi-rating-input"
                  placeholder="Un commentaire pour le livreur ou l'équipe Wazap ? (facultatif)"
                  value={ratingComment}
                  onChange={(e) => setRatingComment(e.target.value)}
                />

                {ratingErr && <div className="suivi-error-msg">{ratingErr}</div>}

                <button
                  type="button"
                  className="suivi-btn-submit-rating"
                  disabled={ratingSubmitting}
                  onClick={submitRating}
                  style={{ opacity: ratingSubmitting ? 0.7 : 1 }}
                >
                  {ratingSubmitting ? 'Envoi…' : 'Envoyer mon avis ⭐'}
                </button>
              </>
            )}

            {ratingDone && ratingComment && (
              <p
                style={{
                  margin: '10px 0 0',
                  fontSize: '13px',
                  fontStyle: 'italic',
                  color: 'var(--suivi-text-muted)',
                  textAlign: 'center',
                }}
              >
                « {ratingComment} »
              </p>
            )}
          </section>
        )}

        {/* ORDER DETAILS SUMMARY */}
        <section className="suivi-details-card">
          <div className="suivi-details-header">Détails de la commande</div>

          <div className="suivi-details-row">
            <span className="suivi-details-label">Vendeur</span>
            <span className="suivi-details-val">{order.vendorName || 'Boutique Partenaire'}</span>
          </div>

          {order.description && (
            <div className="suivi-details-row">
              <span className="suivi-details-label">Contenu</span>
              <span className="suivi-details-val">{order.description}</span>
            </div>
          )}

          {order.orderLines && order.orderLines.length > 0 && (
            <div style={{ margin: '8px 0', padding: '8px 0', borderTop: '1px solid rgba(255,255,255,0.05)' }}>
              {order.orderLines.map((line, idx) => (
                <div key={idx} className="suivi-details-row" style={{ fontSize: '12px' }}>
                  <span className="suivi-details-label">
                    {line.quantity}x {line.productName}
                  </span>
                  <span className="suivi-details-val">{line.totalPrice.toLocaleString('fr-FR')} FCFA</span>
                </div>
              ))}
            </div>
          )}

          {order.address && (
            <div className="suivi-details-row">
              <span className="suivi-details-label">Point de livraison</span>
              <span className="suivi-details-val">📍 {order.address}</span>
            </div>
          )}

          <div style={{ marginTop: 12, paddingTop: 10, borderTop: '1px solid rgba(255,255,255,0.08)' }}>
            <div className="suivi-details-row" style={{ fontSize: '13px' }}>
              <span className="suivi-details-label">📦 Marchandise</span>
              <span className="suivi-details-val" style={{ fontWeight: 600 }}>{(order.amount ?? 0).toLocaleString('fr-FR')} FCFA</span>
            </div>
            <div className="suivi-details-row" style={{ fontSize: '13px' }}>
              <span className="suivi-details-label">🛵 Frais de livraison (au livreur)</span>
              <span className="suivi-details-val" style={{ color: 'var(--suivi-gold)', fontWeight: 600 }}>{(order.deliveryFee ?? 1000).toLocaleString('fr-FR')} FCFA</span>
            </div>
            <div className="suivi-details-row" style={{ borderBottom: 'none', paddingTop: '8px' }}>
              <span className="suivi-details-label" style={{ fontWeight: 800, color: '#fff', fontSize: '14px' }}>
                Total à régler
              </span>
              <span className="suivi-details-val" style={{ color: 'var(--suivi-emerald)', fontWeight: 800, fontSize: '16px' }}>
                {(order.totalAmount ?? ((order.amount ?? 0) + (order.deliveryFee ?? 1000))).toLocaleString('fr-FR')} FCFA
              </span>
            </div>
          </div>
        </section>

        {/* PAYMENT CARD */}
        {order.payment && order.payment.status !== 'Completed' && (
          <section className="suivi-payment-card">
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <span style={{ fontWeight: 800, fontSize: '15px' }}>💳 Règlement de la commande</span>
              <span style={{ color: 'var(--suivi-gold)', fontWeight: 800, fontSize: '15px' }}>
                {order.payment.amount.toLocaleString('fr-FR')} FCFA
              </span>
            </div>

            {order.payment.paymentLink ? (
              <a href={order.payment.paymentLink} target="_blank" rel="noreferrer" className="suivi-btn-pay">
                Payer par Mobile Money (Wave / Orange / MTN)
              </a>
            ) : (
              <button
                type="button"
                className="suivi-btn-pay"
                disabled={paying}
                onClick={pay}
                style={{ opacity: paying ? 0.7 : 1 }}
              >
                {paying ? 'Connexion à l’opérateur…' : 'Payer par Mobile Money (Wave / OM)'}
              </button>
            )}

            <p style={{ margin: '10px 0 0', fontSize: '12px', color: 'var(--suivi-text-muted)', textAlign: 'center' }}>
              Règlement en espèces à la livraison : {(order.amount ?? 0).toLocaleString('fr-FR')} F (marchandise) + {(order.deliveryFee ?? 1000).toLocaleString('fr-FR')} F (course au livreur) = <strong>{(order.totalAmount ?? ((order.amount ?? 0) + (order.deliveryFee ?? 1000))).toLocaleString('fr-FR')} FCFA</strong>.
            </p>
            {payErr && <div className="suivi-error-msg">{payErr}</div>}
          </section>
        )}

        {order.payment?.status === 'Completed' && (
          <div className="suivi-pay-done">
            ✅ Commande payée par Mobile Money — Aucun frais supplémentaire à remettre au livreur.
          </div>
        )}
      </div>
    </div>
  )
}
