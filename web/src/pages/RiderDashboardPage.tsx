import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import type { RiderDashboard } from '../api/types'
import BrandLogo from '../components/BrandLogo'
import { ErrorAlert, formatDateTime } from '../components/ui'
import '../styles/rider-dashboard.css'

const WHATSAPP_OFFICIAL = '2250544051972'
const ZONES_GRAND_ABIDJAN = [
  'Cocody',
  'Yopougon',
  'Marcory',
  'Abobo',
  'Plateau',
  'Koumassi',
  'Treichville',
  'Adjamé',
  'Port-Bouët',
  'Attécoubé',
]

export default function RiderDashboardPage() {
  const [dash, setDash] = useState<RiderDashboard | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [actionBusy, setActionBusy] = useState(false)
  const [copied, setCopied] = useState(false)

  const loadData = useCallback(async () => {
    try {
      setError('')
      const data = await api.get<RiderDashboard>('/riders/dashboard')
      setDash(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Impossible de charger votre tableau de bord livreur.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void loadData()
    // Auto-refresh toutes les 20 secondes pour capter les nouvelles courses assignées
    const interval = setInterval(() => void loadData(), 20000)
    return () => clearInterval(interval)
  }, [loadData])

  const toggleAvailability = async () => {
    if (!dash || actionBusy) return
    setActionBusy(true)
    try {
      await api.put(`/riders/${dash.id}/availability`, { isAvailable: !dash.isAvailable })
      setDash({ ...dash, isAvailable: !dash.isAvailable })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur de changement de disponibilité.')
    } finally {
      setActionBusy(false)
    }
  }

  const changeZone = async (newZone: string) => {
    if (!dash || actionBusy || dash.zone === newZone) return
    setActionBusy(true)
    try {
      await api.put(`/riders/${dash.id}/zone`, { zone: newZone })
      setDash({ ...dash, zone: newZone })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur de changement de zone.')
    } finally {
      setActionBusy(false)
    }
  }

  const copyReferral = () => {
    if (!dash) return
    void navigator.clipboard.writeText(dash.referralCode)
    setCopied(true)
    setTimeout(() => setCopied(false), 2500)
  }

  const referralShareUrl = dash
    ? `https://wa.me/?text=${encodeURIComponent(
        `Salut confrère livreur ! Rejoins le réseau WAZAP avec mon code parrain *${dash.referralCode}*. 0% de commission sur tes courses, 1 000 à 2 000 FCFA net direct pour toi et un smartphone neuf à gagner ! Inscris-toi ici : https://junioradon79gm-001-site1.jtempurl.com/app/livreurs`,
      )}`
    : '#'

  if (loading) {
    return (
      <div className="rd-page" style={{ display: 'flex', justifyContent: 'center', alignItems: 'center' }}>
        <div style={{ textAlign: 'center', padding: '4rem 1rem' }}>
          <div className="loading__spinner" style={{ margin: '0 auto 1.5rem', width: 40, height: 40 }} />
          <p style={{ color: '#00d66c', fontWeight: 600 }}>Chargement de votre cockpit livreur WAZAP…</p>
        </div>
      </div>
    )
  }

  if (!dash) {
    return (
      <div className="rd-page" style={{ padding: '2rem' }}>
        <div style={{ maxWidth: 600, margin: '2rem auto' }}>
          <ErrorAlert message={error || 'Profil livreur introuvable.'} />
        </div>
      </div>
    )
  }

  return (
    <div className="rd-page">
      {/* Header Livreur */}
      <header className="rd-header">
        <div className="rd-header__left">
          <BrandLogo variant="badge" size="md" />
          <div>
            <h1 className="rd-title">
              {dash.fullName || dash.username}
              {dash.isVerified ? (
                <span className="rd-badge-certified">🛡️ CERTIFIÉ WAZAP</span>
              ) : (
                <span className="rd-badge-pending">⏳ EN ATTENTE DE CONTRÔLE</span>
              )}
            </h1>
            <p className="rd-subtitle">
              Livreur Professionnel WAZAP · {dash.phoneNumber || 'Téléphone non renseigné'}
            </p>
          </div>
        </div>

        {/* 1-Tap Toggle Disponibilité */}
        <button
          className={`rd-dispo-btn ${dash.isAvailable ? 'rd-dispo-btn--online' : 'rd-dispo-btn--offline'}`}
          onClick={toggleAvailability}
          disabled={actionBusy}
          title="Touche pour changer de statut"
        >
          {dash.isAvailable ? '🟢 EN LIGNE (DISPO)' : '⚪ EN PAUSE (INDISPO)'}
        </button>
      </header>

      <main className="rd-container">
        {error && <ErrorAlert message={error} />}

        {/* Zone Selector */}
        <div className="rd-zone-bar">
          <div>
            <span style={{ fontSize: 13, color: 'var(--rd-text-muted)' }}>Commune d'intervention active : </span>
            <strong style={{ color: '#00d66c', fontSize: 15 }}>{dash.zone || 'Non définie'}</strong>
          </div>
          <div className="rd-zone-pills">
            {ZONES_GRAND_ABIDJAN.map((z) => (
              <button
                key={z}
                className={`rd-pill ${dash.zone === z ? 'rd-pill--active' : ''}`}
                onClick={() => changeZone(z)}
                disabled={actionBusy}
              >
                {z}
              </button>
            ))}
          </div>
        </div>

        {/* Course Active (si assignée) */}
        {dash.activeOrder && (
          <section className="rd-active-order">
            <div className="rd-active-order__header">
              <div>
                <span style={{ fontSize: 12, color: '#f59e0b', fontWeight: 700, textTransform: 'uppercase' }}>
                  🚨 COURSE EN COURS DE LIVRAISON
                </span>
                <h2 style={{ fontSize: 22, margin: '4px 0 0', fontWeight: 800 }}>
                  Course #{dash.activeOrder.code}
                </h2>
              </div>
              <div style={{ textAlign: 'right' }}>
                <span style={{ fontSize: 13, color: 'var(--rd-text-muted)' }}>Rémunération Livreur</span>
                <div style={{ fontSize: 20, color: '#00d66c', fontWeight: 800 }}>
                  {dash.activeOrder.deliveryFee.toLocaleString('fr-FR')} FCFA
                </div>
              </div>
            </div>

            <div className="rd-active-order__grid">
              {/* Point de retrait (Commerçant) */}
              <div className="rd-active-box">
                <div className="rd-active-box__title">
                  <span>🏢 POINT DE RETRAIT (VENDEUR)</span>
                </div>
                <div>
                  <strong>{dash.activeOrder.vendorName || 'Boutique Partenaire'}</strong>
                  <div style={{ fontSize: 13, color: 'var(--rd-text-muted)', marginTop: 4 }}>
                    📍 {dash.activeOrder.pickupAddress}
                  </div>
                </div>
                <div className="rd-active-actions">
                  {dash.activeOrder.vendorPhone && (
                    <>
                      <a
                        href={`https://wa.me/${dash.activeOrder.vendorPhone.replace(/\+/g, '')}?text=${encodeURIComponent(
                          `Bonjour, je suis votre livreur WAZAP pour la commande #${dash.activeOrder.code}. Je suis en route pour récupérer le colis.`,
                        )}`}
                        target="_blank"
                        rel="noreferrer"
                        className="rd-btn-action rd-btn-wa"
                      >
                        💬 WhatsApp Vendeur
                      </a>
                      <a href={`tel:${dash.activeOrder.vendorPhone}`} className="rd-btn-action rd-btn-call">
                        📞 Appeler
                      </a>
                    </>
                  )}
                </div>
              </div>

              {/* Point de livraison (Client final) */}
              <div className="rd-active-box">
                <div className="rd-active-box__title">
                  <span>🎯 POINT DE LIVRAISON (CLIENT)</span>
                </div>
                <div>
                  <strong>{dash.activeOrder.clientName || 'Client Destinataire'}</strong>
                  <div style={{ fontSize: 13, color: 'var(--rd-text-muted)', marginTop: 4 }}>
                    📍 {dash.activeOrder.deliveryAddress}
                  </div>
                  <div style={{ fontSize: 13, color: '#38bdf8', marginTop: 4 }}>
                    💳 Marchandise : {dash.activeOrder.amount.toLocaleString('fr-FR')} FCFA (Règlement via Scan QR Code)
                  </div>
                </div>
                <div className="rd-active-actions">
                  <a
                    href={`https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(
                      dash.activeOrder.deliveryAddress || '',
                    )}`}
                    target="_blank"
                    rel="noreferrer"
                    className="rd-btn-action rd-btn-maps"
                  >
                    🗺️ Itinéraire GPS
                  </a>
                  {dash.activeOrder.clientPhone && (
                    <>
                      <a
                        href={`https://wa.me/${dash.activeOrder.clientPhone.replace(/\+/g, '')}?text=${encodeURIComponent(
                          `Bonjour ${dash.activeOrder.clientName || ''}, je suis votre livreur WAZAP pour votre colis #${dash.activeOrder.code}. Préparez votre application Mobile Money (Wave/Orange/MTN) pour scanner le QR Code à mon arrivée 🛵`,
                        )}`}
                        target="_blank"
                        rel="noreferrer"
                        className="rd-btn-action rd-btn-wa"
                      >
                        💬 WhatsApp Client
                      </a>
                      <a href={`tel:${dash.activeOrder.clientPhone}`} className="rd-btn-action rd-btn-call">
                        📞 Appeler
                      </a>
                    </>
                  )}
                </div>
              </div>
            </div>

            <div style={{ background: 'rgba(0, 214, 108, 0.08)', padding: 12, borderRadius: 8, fontSize: 13, color: '#e2e8f0' }}>
              🛡️ <strong>Règle Inviolable Zéro Cash :</strong> Faites scanner le QR Code Universel WAZAP par le client à la livraison. Le vendeur est crédité en direct et votre course de {dash.activeOrder.deliveryFee.toLocaleString('fr-FR')} FCFA est garantie.
            </div>
          </section>
        )}

        {/* 4 KPIs Livreur */}
        <section className="rd-stats-grid">
          <div className="rd-stat-card">
            <span className="rd-stat-card__icon">🛵</span>
            <span className="rd-stat-card__label">Courses Aujourd'hui</span>
            <span className="rd-stat-card__val">{dash.deliveriesToday}</span>
            <span className="rd-stat-card__desc">
              {dash.deliveriesThisMonth} courses ce mois
            </span>
          </div>

          <div className="rd-stat-card">
            <span className="rd-stat-card__icon">💰</span>
            <span className="rd-stat-card__label">Gains Nets Estimés</span>
            <span className="rd-stat-card__val" style={{ color: '#00d66c' }}>
              {dash.totalEarningsEstimated.toLocaleString('fr-FR')} F
            </span>
            <span className="rd-stat-card__desc">0% commission prélevée</span>
          </div>

          <div className="rd-stat-card">
            <span className="rd-stat-card__icon">⭐</span>
            <span className="rd-stat-card__label">Note de Réputation</span>
            <span className="rd-stat-card__val" style={{ color: '#f59e0b' }}>
              {dash.ratingAverage ? `${dash.ratingAverage} / 5` : '5.0 / 5'}
            </span>
            <span className="rd-stat-card__desc">{dash.ratingCount} avis clients vérifiés</span>
          </div>

          <div className="rd-stat-card">
            <span className="rd-stat-card__icon">📦</span>
            <span className="rd-stat-card__label">Total Historique</span>
            <span className="rd-stat-card__val">{dash.totalDeliveries}</span>
            <span className="rd-stat-card__desc">Courses clôturées avec succès</span>
          </div>
        </section>

        {/* Challenge Ambassadeur WAZAP (Smartphone Neuf) */}
        <section className="rd-ambassador-card">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 10 }}>
            <div>
              <span style={{ fontSize: 12, color: '#c084fc', fontWeight: 700, textTransform: 'uppercase' }}>
                🏆 CHALLENGE LIVREURS WAZAP
              </span>
              <h3 style={{ margin: '4px 0 0', fontSize: 20, fontWeight: 800 }}>
                Gagnez {dash.programProgress?.rewardLabel || 'un Smartphone Neuf'}
              </h3>
            </div>
            <span
              style={{
                background: dash.programProgress?.rewardUnlocked ? '#00d66c' : 'rgba(255, 255, 255, 0.1)',
                color: dash.programProgress?.rewardUnlocked ? '#000' : '#fff',
                padding: '6px 14px',
                borderRadius: 20,
                fontSize: 13,
                fontWeight: 700,
              }}
            >
              {dash.programProgress?.rewardUnlocked
                ? '🎉 OBJECTIF ATTEINT !'
                : `${dash.programProgress?.conditionsMet ?? (dash.isVerified ? 1 : 0)}/3 conditions`}
            </span>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 16 }}>
            {/* Condition 1: Certification */}
            <div style={{ background: 'rgba(0, 0, 0, 0.3)', padding: 14, borderRadius: 10 }}>
              <div style={{ fontSize: 13, color: 'var(--rd-text-muted)' }}>Condition 1 : Identité</div>
              <strong style={{ color: dash.isVerified ? '#00d66c' : '#f59e0b' }}>
                {dash.isVerified ? '✅ CNI / Permis Vérifié' : '⏳ Pièce en attente'}
              </strong>
            </div>

            {/* Condition 2: Livraisons */}
            <div style={{ background: 'rgba(0, 0, 0, 0.3)', padding: 14, borderRadius: 10 }}>
              <div style={{ fontSize: 13, color: 'var(--rd-text-muted)' }}>
                Condition 2 : Courses ({dash.programProgress?.deliveries ?? dash.totalDeliveries} /{' '}
                {dash.programProgress?.deliveriesTarget ?? 50})
              </div>
              <div className="rd-progress-bar" style={{ marginTop: 8 }}>
                <div
                  className="rd-progress-fill"
                  style={{
                    width: `${Math.min(
                      100,
                      Math.round(
                        ((dash.programProgress?.deliveries ?? dash.totalDeliveries) /
                          (dash.programProgress?.deliveriesTarget || 50)) *
                          100,
                      ),
                    )}%`,
                  }}
                />
              </div>
            </div>

            {/* Condition 3: Parrainage */}
            <div style={{ background: 'rgba(0, 0, 0, 0.3)', padding: 14, borderRadius: 10 }}>
              <div style={{ fontSize: 13, color: 'var(--rd-text-muted)' }}>
                Condition 3 : Filleuls ({dash.programProgress?.validatedReferrals ?? dash.validatedReferrals} /{' '}
                {dash.programProgress?.referralsTarget ?? 5})
              </div>
              <div className="rd-progress-bar" style={{ marginTop: 8 }}>
                <div
                  className="rd-progress-fill"
                  style={{
                    width: `${Math.min(
                      100,
                      Math.round(
                        ((dash.programProgress?.validatedReferrals ?? dash.validatedReferrals) /
                          (dash.programProgress?.referralsTarget || 5)) *
                          100,
                      ),
                    )}%`,
                  }}
                />
              </div>
            </div>
          </div>

          {/* Parrainage 1-clic */}
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              flexWrap: 'wrap',
              gap: 12,
              borderTop: '1px solid rgba(255, 255, 255, 0.1)',
              paddingTop: 14,
            }}
          >
            <div>
              <span style={{ fontSize: 13, color: 'var(--rd-text-muted)' }}>Votre code parrain livreur : </span>
              <strong style={{ fontSize: 16, color: '#38bdf8', letterSpacing: 1 }}>{dash.referralCode}</strong>
            </div>
            <div style={{ display: 'flex', gap: 8 }}>
              <button className="rd-pill" onClick={copyReferral}>
                {copied ? '✅ Copié !' : '📋 Copier mon code'}
              </button>
              <a
                href={referralShareUrl}
                target="_blank"
                rel="noreferrer"
                className="rd-pill"
                style={{ background: '#25d366', color: '#000', textDecoration: 'none', border: 'none' }}
              >
                📲 Inviter sur WhatsApp
              </a>
            </div>
          </div>
        </section>

        {/* Historique des courses */}
        <section className="rd-table-card">
          <h3 style={{ margin: '0 0 16px', fontSize: 18, fontWeight: 700 }}>
            Historique Récent de vos Courses
          </h3>
          {dash.recentOrders.length === 0 ? (
            <p style={{ color: 'var(--rd-text-muted)', fontSize: 14 }}>
              Aucune course effectuée pour le moment. Activez votre statut 🟢 DISPO pour commencer à recevoir des livraisons !
            </p>
          ) : (
            <table className="rd-table">
              <thead>
                <tr>
                  <th>Code</th>
                  <th>Destinataire</th>
                  <th>Destination</th>
                  <th>Frais perçus</th>
                  <th>Statut</th>
                  <th>Date</th>
                </tr>
              </thead>
              <tbody>
                {dash.recentOrders.map((o) => (
                  <tr key={o.id}>
                    <td>
                      <strong>#{o.code}</strong>
                    </td>
                    <td>{o.clientName || 'Client WAZAP'}</td>
                    <td>{o.deliveryAddress || 'Non précisée'}</td>
                    <td style={{ color: '#00d66c', fontWeight: 700 }}>
                      {o.deliveryFee.toLocaleString('fr-FR')} FCFA
                    </td>
                    <td>
                      <span
                        style={{
                          fontSize: 12,
                          padding: '3px 8px',
                          borderRadius: 12,
                          background: o.status === 'Delivered' ? 'rgba(0, 214, 108, 0.15)' : 'rgba(255, 255, 255, 0.08)',
                          color: o.status === 'Delivered' ? '#00d66c' : '#cbd5e1',
                        }}
                      >
                        {o.status === 'Delivered' ? '✅ Livrée' : o.status}
                      </span>
                    </td>
                    <td style={{ color: 'var(--rd-text-muted)', fontSize: 12 }}>
                      {o.deliveredAt ? formatDateTime(o.deliveredAt) : 'En cours'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>

        {/* Accès rapide Bot WhatsApp */}
        <div style={{ textAlign: 'center', marginTop: 10 }}>
          <a
            href={`https://wa.me/${WHATSAPP_OFFICIAL}?text=DISPO`}
            target="_blank"
            rel="noreferrer"
            className="rd-btn-action rd-btn-wa"
            style={{ padding: '12px 24px', fontSize: 14, borderRadius: 30 }}
          >
            💬 Ouvrir le Bot WhatsApp WAZAP ({WHATSAPP_OFFICIAL})
          </a>
        </div>
      </main>
    </div>
  )
}
