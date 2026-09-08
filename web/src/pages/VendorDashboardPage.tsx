import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { VendorDashboard } from '../api/types'
import { StatusBadge, formatDateTime, shortId } from '../components/ui'

const WHATSAPP_BOT = '2250575803801'

const STATUS_FR: Record<string, string> = {
  PendingVendorConfirmation: 'Confirmation vendeur',
  VendorConfirmed: 'Confirmée',
  AwaitingRiderAcceptance: 'Recherche livreur',
  RiderAssigned: 'Livreur assigné',
  ReadyForPickup: 'Prête',
  PickedUp: 'Récupérée',
  InTransit: 'En livraison',
  Delivered: 'Livrée',
  Cancelled: 'Annulée',
}

const waLink = (message: string): string =>
  `https://wa.me/${WHATSAPP_BOT}?text=${encodeURIComponent(message)}`

export default function VendorDashboardPage() {
  const [dash, setDash] = useState<VendorDashboard | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    api
      .get<VendorDashboard>('/vendors/dashboard')
      .then(setDash)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Erreur'))
  }, [])

  if (error) return <div className="alert alert--error">{error}</div>
  if (!dash) return <div className="loading"><span className="loading__spinner" /> Chargement de votre espace…</div>

  const copyReferral = async (): Promise<void> => {
    try {
      await navigator.clipboard.writeText(dash.referralCode)
    } catch {
      /* clipboard indisponible */
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Mon espace vendeur</h1>
          <p>
            {dash.username} · {dash.zone || 'zone non définie'} · {dash.phoneNumber || '—'}
          </p>
        </div>
        <a
          className="btn btn--primary"
          href={waLink('Bonjour WAZAP ! Je veux passer une commande LIVRAISON.')}
          target="_blank"
          rel="noreferrer"
        >
          📲 Commander sur WhatsApp
        </a>
      </div>

      <section className="stats">
        <article className="stat-card stat-card--green">
          <div className="stat-card__icon">💳</div>
          <div className="stat-card__body">
            <span className="stat-card__label">Crédits restants</span>
            <span className="stat-card__value">{dash.credits}</span>
            <span className="stat-card__hint">1 course = 1 crédit</span>
          </div>
        </article>

        <article className="stat-card stat-card--blue">
          <div className="stat-card__icon">🧾</div>
          <div className="stat-card__body">
            <span className="stat-card__label">Courses en cours</span>
            <span className="stat-card__value">{dash.inProgressOrders}</span>
            <span className="stat-card__hint">En attente ou en livraison</span>
          </div>
        </article>

        <article className="stat-card stat-card--dark">
          <div className="stat-card__icon">🎉</div>
          <div className="stat-card__body">
            <span className="stat-card__label">Livraisons ce mois</span>
            <span className="stat-card__value">{dash.deliveredThisMonth}</span>
            <span className="stat-card__hint">Courses livrées avec succès</span>
          </div>
        </article>
      </section>

      <section className="stats">
        <article className="stat-card stat-card--dark">
          <div className="stat-card__icon">💰</div>
          <div className="stat-card__body">
            <span className="stat-card__label">CA mensuel</span>
            <span className="stat-card__value">{dash.monthlyRevenue.toLocaleString()} F</span>
            <span className="stat-card__hint">Chiffre d'affaires du mois</span>
          </div>
        </article>

        <article className="stat-card stat-card--blue">
          <div className="stat-card__icon">🛒</div>
          <div className="stat-card__body">
            <span className="stat-card__label">Panier moyen</span>
            <span className="stat-card__value">{dash.averageBasket.toLocaleString()} F</span>
            <span className="stat-card__hint">Montant moyen par livraison</span>
          </div>
        </article>

        <article className="stat-card stat-card--green">
          <div className="stat-card__icon">✅</div>
          <div className="stat-card__body">
            <span className="stat-card__label">Taux livraison</span>
            <span className="stat-card__value">{Math.round(dash.deliveryRate * 100)} %</span>
            <span className="stat-card__hint">Commandes livrées sur 30j</span>
          </div>
        </article>

        <article className="stat-card stat-card--blue">
          <div className="stat-card__icon">📊</div>
          <div className="stat-card__body">
            <span className="stat-card__label">Commandes 30j</span>
            <span className="stat-card__value">{dash.ordersLastMonth}</span>
            <span className="stat-card__hint">{dash.ordersThisWeek} cette semaine</span>
          </div>
        </article>
      </section>

      {dash.topClients.length > 0 && (
        <section className="panel" style={{ marginTop: 16 }}>
          <header className="panel__header">
            <div>
              <h2 className="panel__title">🎯 Meilleurs clients</h2>
              <p className="panel__subtitle">Ceux qui commandent le plus chez vous</p>
            </div>
          </header>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Client</th>
                  <th>Commandes</th>
                  <th>Total dépensé</th>
                </tr>
              </thead>
              <tbody>
                {dash.topClients.map((c, i) => (
                  <tr key={i}>
                    <td>{c.clientName}</td>
                    <td>{c.orderCount}</td>
                    <td>{c.totalSpent.toLocaleString()} F</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      <div className="grid" style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
        <section className="panel">
          <header className="panel__header">
            <div>
              <h2 className="panel__title">Comment commander ?</h2>
              <p className="panel__subtitle">100 % WhatsApp, aucune appli à installer</p>
            </div>
          </header>
          <ol style={{ paddingLeft: 20, lineHeight: 1.9, fontSize: 14 }}>
            <li>Envoyez <code>LIVRAISON</code> + votre produit + le quartier du client
              <br /><em style={{ color: 'var(--wz-muted, #888)' }}>Ex. « LIVRAISON 2 poulets braisés à Marcory, rue Princesse »</em>
            </li>
            <li>Un livreur proche est contacté automatiquement (1 crédit)</li>
            <li>Suivez la course jusqu'à la livraison de votre client</li>
          </ol>
        </section>

        <section className="panel">
          <header className="panel__header">
            <div>
              <h2 className="panel__title">Parrainage</h2>
              <p className="panel__subtitle">Chaque filleul inscrit vous rapporte +5 crédits</p>
            </div>
          </header>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap', padding: '8px 0 4px' }}>
            <code style={{ fontSize: 22, fontWeight: 700 }}>{dash.referralCode}</code>
            <button className="btn" onClick={() => void copyReferral()}>📋 Copier mon code</button>
          </div>
          <p style={{ fontSize: 13, marginTop: 6 }}>
            Votre lien parrainage :
          </p>
          <code style={{ fontSize: 13, wordBreak: 'break-all' }}>
            {waLink(`Bonjour WAZAP ! Je vous recommande WAZAP pour vos livraisons. Mon code parrainage : ${dash.referralCode}`)}
          </code>
          <div className="stats" style={{ marginTop: 14, gridTemplateColumns: '1fr 1fr' }}>
            <article className="stat-card stat-card--green">
              <div className="stat-card__body">
                <span className="stat-card__label">Filleuls inscrits</span>
                <span className="stat-card__value">{dash.totalReferrals}</span>
              </div>
            </article>
            <article className="stat-card stat-card--dark">
              <div className="stat-card__body">
                <span className="stat-card__label">Crédits gagnés</span>
                <span className="stat-card__value">+{dash.referralCreditsEarned}</span>
              </div>
            </article>
          </div>
        </section>
      </div>

      <section className="panel" style={{ marginTop: 16 }}>
        <header className="panel__header">
          <div>
            <h2 className="panel__title">Vos filleuls</h2>
            <p className="panel__subtitle">
              Vendeurs inscrits avec votre code parrainage ({dash.totalReferrals} au total)
            </p>
          </div>
        </header>
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Commerce</th>
                <th>WhatsApp</th>
                <th>Zone</th>
                <th>Inscrit le</th>
              </tr>
            </thead>
            <tbody>
              {dash.referrals.map((r) => (
                <tr key={r.id}>
                  <td>{r.username}</td>
                  <td>{r.phoneNumber || '—'}</td>
                  <td>{r.zone || '—'}</td>
                  <td style={{ fontSize: 13 }}>{formatDateTime(r.createdAt)}</td>
                </tr>
              ))}
              {dash.referrals.length === 0 && (
                <tr>
                  <td colSpan={4} className="empty">
                    Aucun filleul pour le moment — partagez votre code parrainage ci-dessus 🎁
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>

      <section className="panel" style={{ marginTop: 16 }}>
        <header className="panel__header">
          <div>
            <h2 className="panel__title">Vos dernières courses</h2>
            <p className="panel__subtitle">Historique récent de vos commandes</p>
          </div>
        </header>
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Course</th>
                <th>Client</th>
                <th>Détail</th>
                <th>Statut</th>
                <th>Créée le</th>
              </tr>
            </thead>
            <tbody>
              {dash.recentOrders.map((o) => (
                <tr key={o.id}>
                  <td><span className="order-id">#{shortId(o.id)}</span></td>
                  <td>{o.clientName || '—'}</td>
                  <td>{o.description}</td>
                  <td><StatusBadge status={STATUS_FR[o.status] ?? o.status} /></td>
                  <td style={{ fontSize: 13 }}>{formatDateTime(o.createdAt)}</td>
                </tr>
              ))}
              {dash.recentOrders.length === 0 && (
                <tr><td colSpan={5} className="empty">Aucune course pour le moment — lancez votre première commande via WhatsApp !</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </>
  )
}
