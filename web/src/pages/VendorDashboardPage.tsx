import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import type { PackDto, PaymentResponse, VendorDashboard, VendorOrderItem } from '../api/types'
import BrandLogo from '../components/BrandLogo'
import { ErrorAlert, formatDateTime, shortId } from '../components/ui'
import '../styles/vendor-dashboard.css'

/* ─── Constants ─────────────────────────────────────────── */
const WHATSAPP_BOT = '2250787119520'
const TRACKING_BASE = typeof window !== 'undefined' ? `${window.location.origin}/app/suivi` : 'https://wazap-api.onrender.com/app/suivi'

const STATUS_FR: Record<string, string> = {
  PendingVendorConfirmation: 'Attente vendeur',
  VendorConfirmed: 'Confirmée',
  AwaitingRiderAcceptance: 'Recherche livreur',
  RiderAssigned: 'Livreur assigné',
  ReadyForPickup: 'Prête au pickup',
  PickedUp: 'Récupérée',
  InTransit: 'En livraison',
  Delivered: 'Livrée',
  Cancelled: 'Annulée',
}

const ACTIVE_STATUSES = new Set([
  'PendingVendorConfirmation', 'VendorConfirmed', 'AwaitingRiderAcceptance',
  'RiderAssigned', 'ReadyForPickup', 'PickedUp', 'InTransit',
])

type OrderFilter = 'all' | 'active' | 'delivered' | 'cancelled'

interface NewOrderForm {
  clientName: string
  clientWhatsAppNumber: string
  description: string
  amount: string
  deliveryFee: string
  address: string
}

const EMPTY_ORDER_FORM: NewOrderForm = {
  clientName: '',
  clientWhatsAppNumber: '',
  description: '',
  amount: '',
  deliveryFee: '1000',
  address: '',
}

const waLink = (msg: string) =>
  `https://wa.me/${WHATSAPP_BOT}?text=${encodeURIComponent(msg)}`

function waTrackShare(code: string, orderId: string, clientName?: string | null) {
  const url = `${TRACKING_BASE}/${orderId}`
  const msg = `Bonjour ${clientName ?? ''}👋 Votre commande #${code} est en cours. Suivez votre colis en temps réel ici :\n${url}\nPrésentez ce code au livreur à la livraison.`
  return `https://wa.me/?text=${encodeURIComponent(msg)}`
}

function statusBadgeClass(status: string) {
  if (status === 'Delivered') return 'vd-badge--delivered'
  if (status === 'Cancelled') return 'vd-badge--cancelled'
  if (ACTIVE_STATUSES.has(status)) return 'vd-badge--transit'
  return 'vd-badge--pending'
}

function statusDot(status: string) {
  if (status === 'Delivered') return '✅'
  if (status === 'Cancelled') return '❌'
  if (['InTransit', 'PickedUp'].includes(status)) return '🛵'
  if (status === 'AwaitingRiderAcceptance') return '🔍'
  return '⏳'
}

/* ─── Badge ─────────────────────────────────────────────── */
function VdBadge({ status }: { status: string }) {
  return (
    <span className={`vd-badge ${statusBadgeClass(status)}`}>
      {statusDot(status)} {STATUS_FR[status] ?? status}
    </span>
  )
}

/* ─── Main Component ────────────────────────────────────── */
export default function VendorDashboardPage() {
  const [dash, setDash] = useState<VendorDashboard | null>(null)
  const [packs, setPacks] = useState<PackDto[]>([])
  const [error, setError] = useState('')
  const [filter, setFilter] = useState<OrderFilter>('all')
  const [copiedKey, setCopiedKey] = useState<string | null>(null)

  /* Modals */
  const [showOrder, setShowOrder] = useState(false)
  const [showRecharge, setShowRecharge] = useState(false)
  const [settlingOrder, setSettlingOrder] = useState<VendorOrderItem | null>(null)

  /* Order confirmation */
  const [confirmingId, setConfirmingId] = useState<string | null>(null)
  const [confirmMsg, setConfirmMsg] = useState<string | null>(null)

  /* New order */
  const [orderForm, setOrderForm] = useState<NewOrderForm>(EMPTY_ORDER_FORM)
  const [orderBusy, setOrderBusy] = useState(false)
  const [orderSuccess, setOrderSuccess] = useState<{ id: string; code: string; clientName: string } | null>(null)
  const [orderError, setOrderError] = useState('')

  /* Recharge */
  const [selectedPack, setSelectedPack] = useState<string>('')
  const [payMethod, setPayMethod] = useState<string>('Wave')
  const [rechargeBusy, setRechargeBusy] = useState(false)
  const [rechargeSuccess, setRechargeSuccess] = useState<PaymentResponse | null>(null)
  const [rechargeError, setRechargeError] = useState('')

  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  /* ─── Load ───────────────────────────────────────────── */
  const load = useCallback(async () => {
    try {
      const [d, p] = await Promise.all([
        api.get<VendorDashboard>('/vendors/dashboard'),
        api.get<PackDto[]>('/packs'),
      ])
      setDash(d)
      setPacks(p)
      setError('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur de chargement.')
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  /* ─── Copy helper ────────────────────────────────────── */
  const copyText = (text: string, key: string) => {
    if (navigator.clipboard) void navigator.clipboard.writeText(text)
    setCopiedKey(key)
    if (toastTimer.current) clearTimeout(toastTimer.current)
    toastTimer.current = setTimeout(() => setCopiedKey(null), 2200)
  }

  /* ─── Confirm order ──────────────────────────────────── */
  const confirmOrder = async (orderId: string) => {
    setConfirmingId(orderId)
    setError('')
    try {
      await api.post(`/vendors/orders/${orderId}/confirm`)
      setConfirmMsg('Commande confirmée avec succès ! Recherche des livreurs déclenchée.')
      await load()
      setTimeout(() => setConfirmMsg(null), 5000)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur lors de la confirmation.')
    } finally {
      setConfirmingId(null)
    }
  }

  /* ─── Order creation ─────────────────────────────────── */
  const submitOrder = async () => {
    setOrderError('')
    if (!orderForm.clientName.trim() || !orderForm.clientWhatsAppNumber.trim() || !orderForm.description.trim()) {
      setOrderError('Nom, téléphone et description sont obligatoires.')
      return
    }
    setOrderBusy(true)
    try {
      const body = {
        clientName: orderForm.clientName.trim(),
        clientWhatsAppNumber: orderForm.clientWhatsAppNumber.trim(),
        vendorWhatsAppNumber: dash?.phoneNumber ?? '',
        description: orderForm.description.trim(),
        amount: parseFloat(orderForm.amount) || 0,
        deliveryFee: parseFloat(orderForm.deliveryFee) || 1000,
      }
      const res = await api.post<{ id: string; code?: string }>('/orders', body)
      const code = res.code ?? shortId(res.id)
      setOrderSuccess({ id: res.id, code, clientName: orderForm.clientName })
      setOrderForm(EMPTY_ORDER_FORM)
      await load()
    } catch (err) {
      setOrderError(err instanceof Error ? err.message : 'Erreur lors de la création.')
    } finally {
      setOrderBusy(false)
    }
  }

  /* ─── Recharge ───────────────────────────────────────── */
  const submitRecharge = async () => {
    setRechargeError('')
    if (!selectedPack) {
      setRechargeError('Choisissez un pack.')
      return
    }
    if (!dash) return
    setRechargeBusy(true)
    try {
      const res = await api.post<PaymentResponse>('/packs/buy', { vendorId: dash.id, packName: selectedPack })
      setRechargeSuccess(res)
      await load()
    } catch (err) {
      setRechargeError(err instanceof Error ? err.message : 'Paiement impossible.')
    } finally {
      setRechargeBusy(false)
    }
  }

  /* ─── Payment request for client cash-on-delivery ───── */
  const requestClientPayment = async (orderId: string, orderCode: string) => {
    try {
      const res = await api.post<{ paymentLink: string | null }>(`/vendors/orders/${orderId}/pay`, {})
      if (res.paymentLink) {
        window.open(res.paymentLink, '_blank')
      } else {
        copyText(`Lien de suivi #${orderCode}`, `pay-${orderId}`)
      }
    } catch {
      /* silencieux */
    }
  }

  /* ─── Filtered orders ────────────────────────────────── */
  const filteredOrders = (dash?.recentOrders ?? []).filter((o: VendorOrderItem) => {
    if (filter === 'active') return ACTIVE_STATUSES.has(o.status)
    if (filter === 'delivered') return o.status === 'Delivered'
    if (filter === 'cancelled') return o.status === 'Cancelled'
    return true
  })

  /* ─── Credit wallet data ─────────────────────────────── */
  const credits = dash?.credits ?? 0
  const lowCredits = credits <= 5
  const maxCredits = packs.length > 0 ? Math.max(...packs.map((p) => p.credits)) : 1000
  const creditFill = Math.min(100, (credits / Math.max(maxCredits, 1)) * 100)

  /* ─── Initials for avatar ────────────────────────────── */
  const initials = (name?: string | null) =>
    name
      ? name
          .split(' ')
          .map((w) => w[0])
          .slice(0, 2)
          .join('')
          .toUpperCase()
      : '🏪'

  /* ─── Loading / Error state ─────────────────────────── */
  if (error) return <ErrorAlert message={error} onRetry={() => void load()} />
  if (!dash)
    return (
      <div className="vd-page" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', minHeight: '60vh' }}>
        <div style={{ textAlign: 'center' }}>
          <span className="vd-spinner" style={{ width: 32, height: 32, borderWidth: 3 }} />
          <p style={{ color: '#7d8590', marginTop: 16, fontWeight: 600 }}>Chargement de votre espace…</p>
        </div>
      </div>
    )

  /* ─── Render ─────────────────────────────────────────── */
  return (
    <div className="vd-page">
      {/* ── HEADER ── */}
      <div className="vd-header">
        <div className="vd-header__left">
          <div className="vd-header__avatar">{initials(dash.username)}</div>
          <div className="vd-header__info">
            <h1 className="vd-header__name">{dash.username}</h1>
            <div className="vd-header__meta">
              {dash.zone && <><span>📍 {dash.zone}</span><span className="vd-header__meta-sep" /></>}
              {dash.phoneNumber && <span>📱 +{dash.phoneNumber}</span>}
              <span style={{ color: 'var(--vd-emerald)', fontWeight: 700, fontSize: 11, background: 'rgba(0,214,108,0.1)', padding: '2px 8px', borderRadius: 5 }}>
                ✓ Marchand Vérifié
              </span>
            </div>
          </div>
        </div>

        <div className="vd-header__actions">
          <button
            id="btn-dispatch"
            type="button"
            className="vd-btn vd-btn--primary"
            onClick={() => { setShowOrder(true); setOrderSuccess(null); setOrderError('') }}
          >
            🚀 Expédier un colis
          </button>
          <button
            id="btn-recharge"
            type="button"
            className="vd-btn vd-btn--amber"
            onClick={() => { setShowRecharge(true); setRechargeSuccess(null); setRechargeError('') }}
          >
            💳 Recharger
          </button>
          <a
            href="/app/catalogue"
            className="vd-btn vd-btn--secondary"
            id="btn-catalogue"
          >
            📦 Mon Catalogue
          </a>
          <a
            href={waLink('Bonjour WAZAP ! Je veux passer une commande LIVRAISON.')}
            target="_blank"
            rel="noreferrer"
            className="vd-btn vd-btn--secondary"
            id="btn-wa-bot"
          >
            💬 Bot WhatsApp
          </a>
          <div style={{ marginLeft: 8 }}>
            <BrandLogo size="sm" variant="badge" />
          </div>
        </div>
      </div>

      {/* ── BODY ── */}
      <div className="vd-body">

        {/* ── CREDIT WALLET ── */}
        <div className="vd-wallet">
          <div>
            <div className="vd-wallet__label">💳 Solde de crédits</div>
            <div className="vd-wallet__balance">{credits.toLocaleString()}</div>
            <div className="vd-wallet__sublabel">crédits disponibles · 1 course = 1 crédit</div>
            {lowCredits && (
              <div className="vd-wallet__warning">
                ⚠️ Crédits faibles — rechargez pour continuer à expédier
              </div>
            )}
            <div className="vd-wallet__credit-bar">
              <div className="vd-wallet__credit-fill" style={{ width: `${creditFill}%` }} />
            </div>
          </div>
          <button
            type="button"
            className="vd-btn vd-btn--amber"
            onClick={() => { setShowRecharge(true); setRechargeSuccess(null); setRechargeError('') }}
          >
            + Recharger maintenant
          </button>
        </div>

        {/* ── KPI GRID ── */}
        <div className="vd-kpi-grid" style={{ marginTop: 20 }}>
          <div className="vd-kpi vd-kpi--amber" id="kpi-active">
            <span className="vd-kpi__icon">🛵</span>
            <div className="vd-kpi__label">Courses en cours</div>
            <div className="vd-kpi__value">
              {dash.inProgressOrders > 0 && <span className="vd-kpi__pulse" />}
              {dash.inProgressOrders}
            </div>
            <div className="vd-kpi__sub">En attente ou en livraison</div>
          </div>

          <div className="vd-kpi vd-kpi--green" id="kpi-delivered">
            <span className="vd-kpi__icon">🎉</span>
            <div className="vd-kpi__label">Livrées ce mois</div>
            <div className="vd-kpi__value">{dash.deliveredThisMonth}</div>
            <div className="vd-kpi__sub">Cours livrées avec succès</div>
          </div>

          <div className="vd-kpi vd-kpi--blue" id="kpi-revenue">
            <span className="vd-kpi__icon">💰</span>
            <div className="vd-kpi__label">CA mensuel</div>
            <div className="vd-kpi__value">{dash.monthlyRevenue.toLocaleString()}</div>
            <div className="vd-kpi__sub">FCFA encaissés</div>
          </div>

          <div className="vd-kpi vd-kpi--green" id="kpi-rate">
            <span className="vd-kpi__icon">✅</span>
            <div className="vd-kpi__label">Taux livraison</div>
            <div className="vd-kpi__value">{Math.round(dash.deliveryRate * 100)}<span style={{ fontSize: 20 }}>%</span></div>
            <div className="vd-kpi__sub">
              <div style={{ height: 4, background: 'rgba(255,255,255,0.08)', borderRadius: 2, marginTop: 4 }}>
                <div style={{ height: '100%', width: `${Math.round(dash.deliveryRate * 100)}%`, background: 'var(--vd-emerald)', borderRadius: 2 }} />
              </div>
            </div>
          </div>
        </div>

        {/* ── RECENT ORDERS ── */}
        <div className="vd-section" id="section-orders">
          <div className="vd-section__header">
            <div>
              <h2 className="vd-section__title">🧾 Vos courses</h2>
              <p className="vd-section__sub">{dash.recentOrders.length} dernières commandes · Panier moyen {dash.averageBasket.toLocaleString()} F</p>
            </div>
            <button
              type="button"
              className="vd-btn vd-btn--primary vd-btn--sm"
              id="btn-new-order-section"
              onClick={() => { setShowOrder(true); setOrderSuccess(null); setOrderError('') }}
            >
              + Nouvelle course
            </button>
          </div>

          <div className="vd-table-card">
            <div className="vd-filter-bar">
              {(['all', 'active', 'delivered', 'cancelled'] as OrderFilter[]).map((f) => (
                <button
                  key={f}
                  type="button"
                  className={`vd-filter-tab${filter === f ? ' vd-filter-tab--active' : ''}`}
                  onClick={() => setFilter(f)}
                  id={`filter-${f}`}
                >
                  {f === 'all' ? '📋 Toutes' : f === 'active' ? '🔴 En cours' : f === 'delivered' ? '✅ Livrées' : '❌ Annulées'}
                </button>
              ))}
            </div>

            {confirmMsg && (
              <div style={{ background: 'var(--vd-emerald-dim)', border: '1px solid var(--vd-border-em)', borderRadius: 10, padding: '12px 16px', color: 'var(--vd-emerald)', fontSize: 13, fontWeight: 600, marginBottom: 16 }}>
                ✓ {confirmMsg}
              </div>
            )}

            {filteredOrders.length === 0 ? (
              <div className="vd-empty">
                <span className="vd-empty__icon">📦</span>
                {filter === 'all'
                  ? 'Aucune course pour le moment — lancez votre première livraison !'
                  : 'Aucune course dans ce filtre.'}
              </div>
            ) : (
              <div style={{ overflowX: 'auto' }}>
                <table className="vd-table">
                  <thead>
                    <tr>
                      <th>Course</th>
                      <th>Client</th>
                      <th>Marchandise</th>
                      <th>Livraison</th>
                      <th>Description</th>
                      <th>Statut</th>
                      <th>Date</th>
                      <th>Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredOrders.map((o) => (
                      <tr key={o.id}>
                        <td><span className="vd-order-code">#{o.code ?? shortId(o.id)}</span></td>
                        <td style={{ fontWeight: 600 }}>{o.clientName ?? '—'}</td>
                        <td>
                          <span style={{ fontWeight: 700, color: 'var(--vd-emerald)', fontSize: 13 }}>
                            {(o.amount ?? 0).toLocaleString()} F
                          </span>
                        </td>
                        <td>
                          <span style={{ fontWeight: 600, color: 'var(--vd-amber)', fontSize: 13 }}>
                            {(o.deliveryFee ?? 1000).toLocaleString()} F
                          </span>
                        </td>
                        <td style={{ maxWidth: 160, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: 'var(--vd-text-muted)' }}>
                          {o.description}
                        </td>
                        <td><VdBadge status={o.status} /></td>
                        <td style={{ fontSize: 12, color: 'var(--vd-text-muted)', whiteSpace: 'nowrap' }}>{formatDateTime(o.createdAt)}</td>
                        <td>
                          <div className="vd-table__actions">
                            {/* ⚡ Confirmer la commande (1-click) */}
                            {o.status === 'PendingVendorConfirmation' && (
                              <button
                                type="button"
                                className="vd-btn vd-btn--sm"
                                style={{ background: 'var(--vd-emerald)', color: '#000', fontWeight: 800 }}
                                title="Confirmer et déclencher la recherche des livreurs"
                                disabled={confirmingId === o.id}
                                onClick={() => void confirmOrder(o.id)}
                              >
                                {confirmingId === o.id ? '…' : '⚡ Confirmer'}
                              </button>
                            )}

                            {/* 💳 Régler le coursier après livraison */}
                            {o.status === 'Delivered' && (
                              <button
                                type="button"
                                className="vd-btn vd-btn--secondary vd-btn--sm"
                                style={{ borderColor: 'var(--vd-amber)', color: 'var(--vd-amber)', fontWeight: 700 }}
                                title="Régler les frais de course au livreur"
                                onClick={() => setSettlingOrder(o)}
                              >
                                💳 Régler coursier
                              </button>
                            )}

                            <a
                              href={`${TRACKING_BASE}/${o.id}`}
                              target="_blank"
                              rel="noreferrer"
                              className="vd-btn vd-btn--ghost vd-btn--sm"
                              title="Suivi en direct"
                            >
                              📍 Suivi
                            </a>
                            <a
                              href={waTrackShare(o.code ?? shortId(o.id), o.id, o.clientName)}
                              target="_blank"
                              rel="noreferrer"
                              className="vd-btn vd-btn--wa vd-btn--sm"
                              title="Partager le suivi"
                            >
                              📲 Partager
                            </a>
                            {ACTIVE_STATUSES.has(o.status) && o.status !== 'PendingVendorConfirmation' && (
                              <button
                                type="button"
                                className="vd-btn vd-btn--secondary vd-btn--sm"
                                title="Demander paiement client"
                                onClick={() => void requestClientPayment(o.id, o.code ?? shortId(o.id))}
                              >
                                💳 Payer
                              </button>
                            )}
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>

        {/* ── TOP CLIENTS ── */}
        {dash.topClients.length > 0 && (
          <div className="vd-section" id="section-clients">
            <div className="vd-section__header">
              <div>
                <h2 className="vd-section__title">🏆 Meilleurs clients</h2>
                <p className="vd-section__sub">Fidélisez vos clients les plus actifs</p>
              </div>
            </div>
            <div className="vd-clients-grid">
              {dash.topClients.map((c, i) => {
                const ranks = ['🥇', '🥈', '🥉']
                return (
                  <div key={i} className="vd-client-card">
                    <div className={`vd-client-card__rank vd-client-card__rank--${i < 3 ? i + 1 : 'other'}`}>
                      {i < 3 ? ranks[i] : i + 1}
                    </div>
                    <div style={{ flex: 1 }}>
                      <div className="vd-client-card__name">{c.clientName}</div>
                      <div className="vd-client-card__stats">{c.orderCount} commande{c.orderCount > 1 ? 's' : ''}</div>
                    </div>
                    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 6 }}>
                      <div className="vd-client-card__spent">{c.totalSpent.toLocaleString()} F</div>
                      <a
                        href={waLink(`Bonjour ! 😊 Merci pour votre fidélité. Profitez d'un bon de livraison offert pour votre prochaine commande !`)}
                        target="_blank"
                        rel="noreferrer"
                        className="vd-btn vd-btn--ghost vd-btn--sm"
                      >
                        💬 Fidéliser
                      </a>
                    </div>
                  </div>
                )
              })}
            </div>
          </div>
        )}

        {/* ── REFERRAL + HOW TO ORDER ── */}
        <div className="vd-two-col" style={{ marginTop: 28 }}>
          {/* Referral */}
          <div className="vd-referral-card" id="section-referral">
            <div>
              <div style={{ fontSize: 13, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '1px', color: 'var(--vd-purple)', marginBottom: 4 }}>
                🎁 Programme Parrainage
              </div>
              <p style={{ fontSize: 14, color: 'var(--vd-text)', fontWeight: 700, margin: '0 0 4px' }}>
                +5 crédits offerts par commerçant parrainé
              </p>
              <p style={{ fontSize: 12, color: 'var(--vd-text-muted)', margin: 0 }}>
                Partagez votre code et recevez des crédits dès que votre filleul est activé.
              </p>

              <div className="vd-referral-code-box">
                <span className="vd-referral-code">{dash.referralCode}</span>
                <button
                  type="button"
                  className="vd-btn vd-btn--secondary vd-btn--sm"
                  id="btn-copy-referral"
                  onClick={() => copyText(dash.referralCode, 'referral')}
                >
                  {copiedKey === 'referral' ? '✅ Copié !' : '📋 Copier'}
                </button>
                <a
                  href={waLink(`Bonjour ! Je vous recommande WAZAP — livraison WhatsApp à Abidjan. 15 premières courses GRATUITES ! Code parrainage : ${dash.referralCode}`)}
                  target="_blank"
                  rel="noreferrer"
                  className="vd-btn vd-btn--wa vd-btn--sm"
                  id="btn-share-referral"
                >
                  📤 Partager
                </a>
              </div>
            </div>

            <div className="vd-referral-stats">
              <div className="vd-referral-stat">
                <div className="vd-referral-stat__value">{dash.totalReferrals}</div>
                <div className="vd-referral-stat__label">Filleuls<br />inscrits</div>
              </div>
              <div className="vd-referral-stat">
                <div className="vd-referral-stat__value">+{dash.referralCreditsEarned}</div>
                <div className="vd-referral-stat__label">Crédits<br />gagnés</div>
              </div>
            </div>
          </div>

          {/* How to order */}
          <div className="vd-panel" id="section-how">
            <h2 className="vd-section__title" style={{ marginBottom: 16 }}>📲 Comment expédier ?</h2>
            <ol style={{ paddingLeft: 20, lineHeight: 2, fontSize: 13, color: 'var(--vd-text)', margin: 0 }}>
              <li>Cliquez sur <strong style={{ color: 'var(--vd-emerald)' }}>🚀 Expédier un colis</strong> en haut</li>
              <li>Renseignez le nom & WhatsApp de votre client</li>
              <li>Décrivez le colis et le montant à encaisser</li>
              <li>Validez — un livreur proche est contacté automatiquement</li>
              <li>Partagez le lien de suivi à votre client en 1 clic 📲</li>
            </ol>
            <div style={{ marginTop: 20, padding: '12px 16px', background: 'var(--vd-emerald-dim)', borderRadius: 10, border: '1px solid var(--vd-border-em)' }}>
              <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--vd-emerald)', marginBottom: 4 }}>
                💡 Ou directement sur WhatsApp
              </div>
              <div style={{ fontSize: 12, color: 'var(--vd-text-muted)', fontStyle: 'italic' }}>
                « LIVRAISON [produit] à [quartier], pour [Client] »
              </div>
              <a
                href={waLink('Bonjour WAZAP ! Je veux passer une commande LIVRAISON.')}
                target="_blank"
                rel="noreferrer"
                className="vd-btn vd-btn--wa"
                style={{ marginTop: 12, width: '100%', justifyContent: 'center' }}
              >
                💬 Ouvrir le bot WhatsApp
              </a>
            </div>
          </div>
        </div>

      </div>

      {/* ═══════════════════════════════════════════════════
          MODAL : Expédier un colis
      ═══════════════════════════════════════════════════ */}
      {showOrder && (
        <div className="vd-overlay" onClick={(e) => e.target === e.currentTarget && setShowOrder(false)}>
          <div className="vd-modal" id="modal-order" role="dialog" aria-modal="true">
            <div className="vd-modal__header">
              <h2 className="vd-modal__title">🚀 Nouvelle livraison</h2>
              <button type="button" className="vd-modal__close" onClick={() => setShowOrder(false)} aria-label="Fermer">✕</button>
            </div>

            <div className="vd-modal__body">
              {orderSuccess ? (
                <div className="vd-success-banner">
                  <span className="vd-success-banner__icon">🎉</span>
                  <div className="vd-success-banner__title">Commande #{orderSuccess.code} créée !</div>
                  <p style={{ fontSize: 13, color: 'var(--vd-text-muted)', margin: '4px 0 12px' }}>
                    Un livreur proche de votre boutique a été contacté automatiquement.
                  </p>
                  <div className="vd-success-banner__tracking">
                    {TRACKING_BASE}/{orderSuccess.id}
                  </div>
                  <div style={{ display: 'flex', gap: 10, justifyContent: 'center', flexWrap: 'wrap', marginTop: 4 }}>
                    <a
                      href={waTrackShare(orderSuccess.code, orderSuccess.id, orderSuccess.clientName)}
                      target="_blank"
                      rel="noreferrer"
                      className="vd-btn vd-btn--wa"
                      id="btn-share-tracking"
                    >
                      📲 Partager le suivi à {orderSuccess.clientName}
                    </a>
                    <button
                      type="button"
                      className="vd-btn vd-btn--secondary"
                      onClick={() => {
                        setOrderSuccess(null)
                        setOrderForm(EMPTY_ORDER_FORM)
                      }}
                    >
                      + Nouvelle course
                    </button>
                  </div>
                </div>
              ) : (
                <>
                  {orderError && (
                    <div style={{ background: 'var(--vd-red-dim)', border: '1px solid rgba(239,68,68,0.3)', borderRadius: 10, padding: '12px 16px', color: 'var(--vd-red)', fontSize: 13, marginBottom: 16 }}>
                      ⚠️ {orderError}
                    </div>
                  )}

                  <div className="vd-field">
                    <label htmlFor="order-client-name">Nom du destinataire *</label>
                    <input
                      id="order-client-name"
                      type="text"
                      value={orderForm.clientName}
                      onChange={(e) => setOrderForm((f) => ({ ...f, clientName: e.target.value }))}
                      placeholder="Ex. Koné Fatou"
                    />
                  </div>

                  <div className="vd-field">
                    <label htmlFor="order-client-phone">WhatsApp du destinataire *</label>
                    <input
                      id="order-client-phone"
                      type="tel"
                      value={orderForm.clientWhatsAppNumber}
                      onChange={(e) => setOrderForm((f) => ({ ...f, clientWhatsAppNumber: e.target.value }))}
                      placeholder="+225 07 00 00 00 00"
                    />
                  </div>

                  <div className="vd-field">
                    <label htmlFor="order-description">Description du colis *</label>
                    <textarea
                      id="order-description"
                      rows={3}
                      value={orderForm.description}
                      onChange={(e) => setOrderForm((f) => ({ ...f, description: e.target.value }))}
                      placeholder="Ex. 2 Attiéké poisson braisé, 1 jus d'ananas…"
                      style={{ resize: 'vertical' }}
                    />
                  </div>

                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 14 }}>
                    <div className="vd-field">
                      <label htmlFor="order-amount">Prix marchandise (FCFA) *</label>
                      <input
                        id="order-amount"
                        type="number"
                        min={0}
                        value={orderForm.amount}
                        onChange={(e) => setOrderForm((f) => ({ ...f, amount: e.target.value }))}
                        placeholder="Ex. 5000"
                      />
                      <span style={{ fontSize: 11, color: 'var(--vd-text-muted)', marginTop: 2, display: 'block' }}>Montant revenant au vendeur</span>
                    </div>

                    <div className="vd-field">
                      <label htmlFor="order-delivery-fee">Frais de course livreur (FCFA) *</label>
                      <input
                        id="order-delivery-fee"
                        type="number"
                        min={500}
                        step={500}
                        value={orderForm.deliveryFee}
                        onChange={(e) => setOrderForm((f) => ({ ...f, deliveryFee: e.target.value }))}
                        placeholder="Ex. 1000"
                      />
                      <div style={{ display: 'flex', gap: 4, marginTop: 4 }}>
                        {['1000', '1500', '2000'].map((fee) => (
                          <button
                            key={fee}
                            type="button"
                            style={{
                              fontSize: 11,
                              padding: '2px 8px',
                              borderRadius: 4,
                              background: orderForm.deliveryFee === fee ? 'var(--vd-amber)' : 'rgba(255,255,255,0.06)',
                              color: orderForm.deliveryFee === fee ? '#000' : 'var(--vd-text-muted)',
                              fontWeight: orderForm.deliveryFee === fee ? 800 : 500,
                              border: 'none',
                              cursor: 'pointer',
                            }}
                            onClick={() => setOrderForm((f) => ({ ...f, deliveryFee: fee }))}
                          >
                            {fee} F
                          </button>
                        ))}
                      </div>
                    </div>
                  </div>

                  <div className="vd-field">
                    <label htmlFor="order-address">Repère de livraison</label>
                    <input
                      id="order-address"
                      type="text"
                      value={orderForm.address}
                      onChange={(e) => setOrderForm((f) => ({ ...f, address: e.target.value }))}
                      placeholder="Ex. Marcory Zone 4, Rue 12 face pharmacie"
                    />
                  </div>

                  {/* Aperçu du total client */}
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', background: 'rgba(0, 214, 108, 0.08)', border: '1px solid rgba(0, 214, 108, 0.2)', borderRadius: 10, padding: '10px 14px', fontSize: 13 }}>
                    <span style={{ color: 'var(--vd-text-muted)' }}>Total à payer par le client :</span>
                    <strong style={{ color: 'var(--vd-emerald)', fontSize: 15 }}>
                      {((parseFloat(orderForm.amount) || 0) + (parseFloat(orderForm.deliveryFee) || 1000)).toLocaleString()} FCFA
                    </strong>
                  </div>

                  <div style={{ background: 'rgba(255,255,255,0.03)', borderRadius: 10, padding: '12px 16px', fontSize: 12, color: 'var(--vd-text-muted)', marginTop: 4 }}>
                    ⚡ 1 crédit sera débité au moment où un livreur accepte la course.<br />
                    <strong style={{ color: 'var(--vd-text)' }}>Solde actuel : {credits} crédit{credits > 1 ? 's' : ''}</strong>
                  </div>
                </>
              )}
            </div>

            {!orderSuccess && (
              <div className="vd-modal__footer">
                <button type="button" className="vd-btn vd-btn--secondary" onClick={() => setShowOrder(false)}>
                  Annuler
                </button>
                <button
                  type="button"
                  id="btn-submit-order"
                  className="vd-btn vd-btn--primary"
                  onClick={() => void submitOrder()}
                  disabled={orderBusy || credits === 0}
                >
                  {orderBusy ? <><span className="vd-spinner" /> Envoi…</> : '🚀 Lancer la livraison'}
                </button>
              </div>
            )}
          </div>
        </div>
      )}

      {/* ═══════════════════════════════════════════════════
          MODAL : Recharger les crédits
      ═══════════════════════════════════════════════════ */}
      {showRecharge && (
        <div className="vd-overlay" onClick={(e) => e.target === e.currentTarget && setShowRecharge(false)}>
          <div className="vd-modal vd-modal--wide" id="modal-recharge" role="dialog" aria-modal="true">
            <div className="vd-modal__header">
              <h2 className="vd-modal__title">💳 Recharger mes crédits</h2>
              <button type="button" className="vd-modal__close" onClick={() => setShowRecharge(false)} aria-label="Fermer">✕</button>
            </div>

            <div className="vd-modal__body">
              {rechargeSuccess ? (
                <div className="vd-success-banner">
                  <span className="vd-success-banner__icon">✅</span>
                  <div className="vd-success-banner__title">
                    {rechargeSuccess.success ? 'Paiement confirmé !' : 'Paiement initié !'}
                  </div>
                  <p style={{ fontSize: 13, color: 'var(--vd-text-muted)', margin: '6px 0 12px' }}>
                    {rechargeSuccess.message}
                  </p>
                  {rechargeSuccess.paymentLink && (
                    <a href={rechargeSuccess.paymentLink} target="_blank" rel="noreferrer" className="vd-btn vd-btn--amber">
                      💳 Finaliser le paiement
                    </a>
                  )}
                </div>
              ) : (
                <>
                  {rechargeError && (
                    <div style={{ background: 'var(--vd-red-dim)', border: '1px solid rgba(239,68,68,0.3)', borderRadius: 10, padding: '12px 16px', color: 'var(--vd-red)', fontSize: 13, marginBottom: 16 }}>
                      ⚠️ {rechargeError}
                    </div>
                  )}

                  <p style={{ fontSize: 13, color: 'var(--vd-text-muted)', marginBottom: 16 }}>
                    Choisissez le pack qui correspond à votre volume de livraisons.
                  </p>

                  <div className="vd-pack-grid" id="pack-grid">
                    {packs.map((p) => {
                      const costPer = p.credits > 0 ? Math.round(p.price / p.credits) : 0
                      const isBest = p.name === 'Grand'
                      return (
                        <div
                          key={p.name}
                          id={`pack-${p.name}`}
                          className={`vd-pack-card${selectedPack === p.name ? ' vd-pack-card--selected' : ''}`}
                          onClick={() => setSelectedPack(p.name)}
                        >
                          {isBest && <span className="vd-pack-card__badge">⭐ Populaire</span>}
                          <div className="vd-pack-card__name">{p.name}</div>
                          <div className="vd-pack-card__credits">{p.credits}</div>
                          <div className="vd-pack-card__credits-label">crédits</div>
                          <div className="vd-pack-card__price">{p.price.toLocaleString()} F</div>
                          <div className="vd-pack-card__cost-per">{costPer} F / course</div>
                        </div>
                      )
                    })}
                  </div>

                  <div style={{ marginTop: 24 }}>
                    <div style={{ fontSize: 12, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.7px', color: 'var(--vd-text-muted)', marginBottom: 12 }}>
                      Moyen de paiement Mobile Money
                    </div>
                    <div className="vd-pay-methods" id="pay-methods">
                      {[
                        { id: 'Wave', icon: '🌊', label: 'Wave' },
                        { id: 'OrangeMoney', icon: '🟠', label: 'Orange Money' },
                        { id: 'MTN', icon: '🟡', label: 'MTN MoMo' },
                        { id: 'Moov', icon: '🔵', label: 'Moov Money' },
                      ].map((m) => (
                        <div
                          key={m.id}
                          id={`pay-method-${m.id}`}
                          className={`vd-pay-method${payMethod === m.id ? ' vd-pay-method--selected' : ''}`}
                          onClick={() => setPayMethod(m.id)}
                        >
                          <span className="vd-pay-method__icon">{m.icon}</span>
                          <div className="vd-pay-method__name">{m.label}</div>
                        </div>
                      ))}
                    </div>
                  </div>

                  {selectedPack && (
                    <div style={{ padding: '14px 18px', background: 'var(--vd-emerald-dim)', border: '1px solid var(--vd-border-em)', borderRadius: 10, fontSize: 13 }}>
                      <strong style={{ color: 'var(--vd-emerald)' }}>Récapitulatif</strong>
                      <div style={{ marginTop: 6, color: 'var(--vd-text)' }}>
                        Pack <strong>{selectedPack}</strong> — {packs.find((p) => p.name === selectedPack)?.credits} crédits via <strong>{payMethod}</strong> = <strong style={{ color: 'var(--vd-emerald)' }}>{packs.find((p) => p.name === selectedPack)?.price.toLocaleString()} F CFA</strong>
                      </div>
                    </div>
                  )}
                </>
              )}
            </div>

            {!rechargeSuccess && (
              <div className="vd-modal__footer">
                <button type="button" className="vd-btn vd-btn--secondary" onClick={() => setShowRecharge(false)}>
                  Annuler
                </button>
                <button
                  type="button"
                  id="btn-submit-recharge"
                  className="vd-btn vd-btn--amber"
                  onClick={() => void submitRecharge()}
                  disabled={rechargeBusy || !selectedPack}
                >
                  {rechargeBusy ? <><span className="vd-spinner" /> Paiement…</> : '💳 Payer maintenant'}
                </button>
              </div>
            )}
          </div>
        </div>
      )}

      {/* ═══════════════════════════════════════════════════
          MODAL : Régler le coursier (Mobile Money)
      ═══════════════════════════════════════════════════ */}
      {settlingOrder && (
        <div className="vd-overlay" onClick={(e) => e.target === e.currentTarget && setSettlingOrder(null)}>
          <div className="vd-modal" id="modal-settle-rider" role="dialog" aria-modal="true" style={{ maxWidth: 480 }}>
            <div className="vd-modal__header">
              <h2 className="vd-modal__title">💳 Règlement de la course</h2>
              <button type="button" className="vd-modal__close" onClick={() => setSettlingOrder(null)} aria-label="Fermer">✕</button>
            </div>

            <div className="vd-modal__body">
              <div style={{ background: 'var(--vd-surface-2)', border: '1px solid var(--vd-border)', borderRadius: 12, padding: '16px', marginBottom: 20 }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
                  <span style={{ fontSize: 13, color: 'var(--vd-text-muted)' }}>Course terminée</span>
                  <span className="vd-order-code">#{settlingOrder.code}</span>
                </div>
                <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--vd-text)', marginBottom: 4 }}>
                  {settlingOrder.description}
                </div>
                <div style={{ fontSize: 12, color: 'var(--vd-text-muted)', marginBottom: 12 }}>
                  Client : {settlingOrder.clientName ?? 'Client WAZAP'}
                </div>

                {/* Distinction nette des 2 montants */}
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10, paddingTop: 12, borderTop: '1px solid rgba(255,255,255,0.06)' }}>
                  <div style={{ background: 'rgba(0, 214, 108, 0.08)', borderRadius: 8, padding: '10px 12px', border: '1px solid rgba(0, 214, 108, 0.2)' }}>
                    <div style={{ fontSize: 11, color: 'var(--vd-text-muted)', textTransform: 'uppercase', fontWeight: 700 }}>📦 Marchandise</div>
                    <div style={{ fontSize: 16, fontWeight: 800, color: 'var(--vd-emerald)', marginTop: 2 }}>
                      {(settlingOrder.amount ?? 0).toLocaleString()} FCFA
                    </div>
                    <div style={{ fontSize: 11, color: 'var(--vd-text-muted)', marginTop: 2 }}>Revenant au vendeur</div>
                  </div>
                  <div style={{ background: 'rgba(245, 158, 11, 0.1)', borderRadius: 8, padding: '10px 12px', border: '1px solid rgba(245, 158, 11, 0.3)' }}>
                    <div style={{ fontSize: 11, color: 'var(--vd-amber)', textTransform: 'uppercase', fontWeight: 700 }}>🛵 Frais de course</div>
                    <div style={{ fontSize: 16, fontWeight: 800, color: 'var(--vd-amber)', marginTop: 2 }}>
                      {(settlingOrder.deliveryFee ?? 1000).toLocaleString()} FCFA
                    </div>
                    <div style={{ fontSize: 11, color: 'var(--vd-amber)', marginTop: 2 }}>Dû au livreur (100%)</div>
                  </div>
                </div>
              </div>

              {/* Rappel modèle WAZAP */}
              <div style={{ background: 'var(--vd-amber-dim)', border: '1px solid rgba(245, 158, 11, 0.3)', borderRadius: 12, padding: '14px 16px', marginBottom: 20 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, color: 'var(--vd-amber)', fontWeight: 800, fontSize: 14 }}>
                  <span>🛵</span>
                  <span>Frais de livraison à verser au coursier : {(settlingOrder.deliveryFee ?? 1000).toLocaleString()} FCFA</span>
                </div>
                <p style={{ margin: '6px 0 0', fontSize: 12, color: 'var(--vd-text-muted)', lineHeight: 1.5 }}>
                  WAZAP ne facture que la mise en relation (1 crédit débité). Les frais de transport de <strong>{(settlingOrder.deliveryFee ?? 1000).toLocaleString()} FCFA</strong> reviennent à <strong>100% au coursier</strong> et doivent lui être transférés directement.
                </p>
              </div>

              {/* Coordonnées du livreur */}
              <div style={{ marginBottom: 20 }}>
                <div style={{ fontSize: 12, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.7px', color: 'var(--vd-text-muted)', marginBottom: 8 }}>
                  Livreur assigné
                </div>
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', background: 'var(--vd-surface)', border: '1px solid var(--vd-border)', borderRadius: 10, padding: '12px 16px' }}>
                  <div>
                    <div style={{ fontWeight: 800, fontSize: 15, color: '#fff' }}>
                      {settlingOrder.riderName || 'Livreur certifié WAZAP'}
                    </div>
                    <div style={{ fontSize: 13, color: 'var(--vd-emerald)', fontWeight: 600, marginTop: 2 }}>
                      {settlingOrder.riderPhone || 'Numéro indisponible'}
                    </div>
                  </div>
                  {settlingOrder.riderPhone && (
                    <button
                      type="button"
                      className="vd-btn vd-btn--secondary vd-btn--sm"
                      onClick={() => copyText(settlingOrder.riderPhone!, 'riderPhone')}
                    >
                      {copiedKey === 'riderPhone' ? '✅ Copié !' : '📋 Copier'}
                    </button>
                  )}
                </div>
              </div>

              {/* Raccourcis de paiement Mobile Money */}
              {settlingOrder.riderPhone && (
                <div style={{ marginBottom: 16 }}>
                  <div style={{ fontSize: 12, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.7px', color: 'var(--vd-text-muted)', marginBottom: 10 }}>
                    Transférer {(settlingOrder.deliveryFee ?? 1000).toLocaleString()} FCFA en 1 clic
                  </div>
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                    <a
                      href={`https://wa.me/${settlingOrder.riderPhone.replace(/\D/g, '')}?text=${encodeURIComponent(`Bonjour, je vous fais le transfert des frais de livraison de ${(settlingOrder.deliveryFee ?? 1000).toLocaleString()} FCFA pour la course WAZAP #${settlingOrder.code}. Merci !`)}`}
                      target="_blank"
                      rel="noreferrer"
                      className="vd-btn vd-btn--wa"
                      style={{ justifyContent: 'center', fontSize: 13 }}
                    >
                      💬 WhatsApp
                    </a>
                    <a
                      href={`tel:${settlingOrder.riderPhone}`}
                      className="vd-btn vd-btn--secondary"
                      style={{ justifyContent: 'center', fontSize: 13 }}
                    >
                      📞 Appeler
                    </a>
                  </div>

                  <div style={{ marginTop: 10, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                    <a
                      href="https://pay.wave.com"
                      target="_blank"
                      rel="noreferrer"
                      className="vd-btn"
                      style={{ background: '#1dc4ff', color: '#000', fontWeight: 800, justifyContent: 'center', fontSize: 13 }}
                    >
                      🌊 Ouvrir Wave
                    </a>
                    <a
                      href="tel:*144#"
                      className="vd-btn"
                      style={{ background: '#ff7900', color: '#fff', fontWeight: 800, justifyContent: 'center', fontSize: 13 }}
                    >
                      🟠 Orange Money
                    </a>
                  </div>
                </div>
              )}
            </div>

            <div className="vd-modal__footer">
              <button
                type="button"
                className="vd-btn vd-btn--primary"
                style={{ width: '100%', justifyContent: 'center' }}
                onClick={() => setSettlingOrder(null)}
              >
                ✓ Marquer comme réglé & fermer
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Toast */}
      {copiedKey && (
        <div className="vd-copied-flash">✅ Copié dans le presse-papier !</div>
      )}
    </div>
  )
}
