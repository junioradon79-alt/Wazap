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
              <p className="panel__subtitle">Vos filleuls reçoivent 15 commandes offertes — vous aussi</p>
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
        </section>
      </div>

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
