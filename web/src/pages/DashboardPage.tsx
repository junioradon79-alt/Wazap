import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { DashboardSummary } from '../api/types'
import { StatusBadge, formatMoney, shortId } from '../components/ui'

export default function DashboardPage() {
  const [summary, setSummary] = useState<DashboardSummary | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    api
      .get<DashboardSummary>('/dashboard/summary')
      .then(setSummary)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Erreur'))
  }, [])

  if (error) return <div className="alert alert--error">{error}</div>
  if (!summary) return <div className="loading"><span className="loading__spinner" /> Chargement du tableau de bord…</div>

  return (
    <>
      <section className="stats">
        <article className="stat-card stat-card--green">
          <div className="stat-card__icon">🧾</div>
          <div className="stat-card__body">
            <span className="stat-card__label">Commandes en cours</span>
            <span className="stat-card__value">{summary.inProgressOrdersCount}</span>
            <span className="stat-card__hint">En cours de traitement</span>
          </div>
        </article>

        <article className="stat-card stat-card--blue">
          <div className="stat-card__icon">🛵</div>
          <div className="stat-card__body">
            <span className="stat-card__label">Livreurs actifs</span>
            <span className="stat-card__value">{summary.activeRiders}</span>
            <span className="stat-card__hint">Assignés à une course</span>
          </div>
        </article>

        <article className="stat-card stat-card--dark">
          <div className="stat-card__icon">💰</div>
          <div className="stat-card__body">
            <span className="stat-card__label">Chiffre d'affaires du mois</span>
            <span className="stat-card__value">{formatMoney(summary.monthlyRevenue)}</span>
            <span className="stat-card__hint">Livré ce mois-ci</span>
          </div>
        </article>
      </section>

      <section className="stats">
        <article className="stat-card stat-card--dark">
          <div className="stat-card__icon">📊</div>
          <div className="stat-card__body">
            <span className="stat-card__label">CA 30j</span>
            <span className="stat-card__value">{formatMoney(summary.revenue30d)}</span>
            <span className="stat-card__hint">
              {summary.revenueChangePercent > 0 ? '+' : ''}{summary.revenueChangePercent} % vs période précédente
            </span>
          </div>
        </article>

        <article className="stat-card stat-card--blue">
          <div className="stat-card__icon">🛒</div>
          <div className="stat-card__body">
            <span className="stat-card__label">Panier moyen</span>
            <span className="stat-card__value">{formatMoney(summary.averageBasket30d)}</span>
            <span className="stat-card__hint">Sur les 30 derniers jours</span>
          </div>
        </article>

        <article className="stat-card stat-card--green">
          <div className="stat-card__icon">✅</div>
          <div className="stat-card__body">
            <span className="stat-card__label">Taux livraison</span>
            <span className="stat-card__value">{Math.round(summary.deliveryRate30d * 100)} %</span>
            <span className="stat-card__hint">Commandes livrées 30j</span>
          </div>
        </article>

        <article className="stat-card stat-card--blue">
          <div className="stat-card__icon">🔄</div>
          <div className="stat-card__body">
            <span className="stat-card__label">Conversion leads</span>
            <span className="stat-card__value">{Math.round(summary.leadConversionRate30d * 100)} %</span>
            <span className="stat-card__hint">Leads → vendeurs 30j</span>
          </div>
        </article>
      </section>

      <section className="panel">
        <header className="panel__header">
          <div>
            <h2 className="panel__title">Commandes en cours</h2>
            <p className="panel__subtitle">Suivi temps réel des livraisons</p>
          </div>
          <div className="panel__actions">
            <span className="panel__count">{summary.recentOrders.length} commandes</span>
          </div>
        </header>

        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>ID</th>
                <th>Vendeur</th>
                <th>WhatsApp client</th>
                <th>Statut</th>
              </tr>
            </thead>
            <tbody>
              {summary.recentOrders.map((order) => (
                <tr key={order.id}>
                  <td><span className="order-id">#{shortId(order.id)}</span></td>
                  <td>
                    <div className="vendor">
                      <span className="vendor__name">{order.vendorName}</span>
                      <span className="vendor__phone">{order.vendorWhatsApp}</span>
                    </div>
                  </td>
                  <td><span className="whatsapp">{order.maskedClientPhone}</span></td>
                  <td><StatusBadge status={order.statusLabel} /></td>
                </tr>
              ))}
              {summary.recentOrders.length === 0 && (
                <tr><td colSpan={4} className="empty">Aucune commande en cours</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>

      <div className="grid" style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16, marginTop: 16 }}>
        {summary.topVendors30d.length > 0 && (
          <section className="panel">
            <header className="panel__header">
              <div>
                <h2 className="panel__title">🏆 Top vendeurs 30j</h2>
                <p className="panel__subtitle">Les plus actifs du mois</p>
              </div>
            </header>
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>Vendeur</th>
                    <th>Livrées</th>
                    <th>CA</th>
                  </tr>
                </thead>
                <tbody>
                  {summary.topVendors30d.map((v, i) => (
                    <tr key={i}>
                      <td>{v.username}</td>
                      <td>{v.deliveredOrders}</td>
                      <td>{formatMoney(v.revenue)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        )}

        {summary.revenueByZone30d.length > 0 && (
          <section className="panel">
            <header className="panel__header">
              <div>
                <h2 className="panel__title">🗺️ CA par zone 30j</h2>
                <p className="panel__subtitle">Répartition géographique</p>
              </div>
            </header>
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>Zone</th>
                    <th>CA</th>
                  </tr>
                </thead>
                <tbody>
                  {summary.revenueByZone30d.map((z, i) => (
                    <tr key={i}>
                      <td>{z.zone}</td>
                      <td>{formatMoney(z.revenue)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        )}
      </div>
    </>
  )
}
