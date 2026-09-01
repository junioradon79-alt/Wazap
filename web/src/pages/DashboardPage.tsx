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
    </>
  )
}
