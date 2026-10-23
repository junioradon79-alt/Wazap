import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { DashboardSummary } from '../api/types'
import {
  ErrorAlert,
  PageHeader,
  SkeletonStatCard,
  StatCard,
  StatsGrid,
  StatusBadge,
  formatMoney,
  shortId,
} from '../components'

export default function DashboardPage() {
  const [summary, setSummary] = useState<DashboardSummary | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  const load = async (): Promise<void> => {
    try {
      setLoading(true)
      const data = await api.get<DashboardSummary>('/dashboard/summary')
      setSummary(data)
      setError('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur de chargement')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void load()
  }, [])

  if (error) return <ErrorAlert message={error} onRetry={() => void load()} />

  if (loading || !summary) {
    return (
      <>
        <PageHeader
          title="Tableau de bord"
          subtitle="Chargement des indicateurs en direct…"
        />
        <StatsGrid>
          <SkeletonStatCard />
          <SkeletonStatCard />
          <SkeletonStatCard />
        </StatsGrid>
        <StatsGrid>
          <SkeletonStatCard />
          <SkeletonStatCard />
          <SkeletonStatCard />
          <SkeletonStatCard />
        </StatsGrid>
      </>
    )
  }

  return (
    <>
      <PageHeader
        title="Tableau de bord"
        subtitle="Pilotage global de la plateforme, flux des livraisons et volumes"
        actions={
          <button
            type="button"
            className="btn btn--ghost"
            onClick={() => void load()}
            title="Rafraîchir les données"
          >
            ⟳ Actualiser
          </button>
        }
      />

      {/* Cartes Clés Principales */}
      <StatsGrid>
        <StatCard
          icon="🧾"
          variant="green"
          label="Commandes en cours"
          value={summary.inProgressOrdersCount}
          hint="En cours de traitement"
        />
        <StatCard
          icon="🛵"
          variant="blue"
          label="Livreurs actifs"
          value={summary.activeRiders}
          hint="Assignés à une course"
        />
        <StatCard
          icon="💰"
          variant="dark"
          label="Chiffre d'affaires du mois"
          value={formatMoney(summary.monthlyRevenue)}
          hint="Livré ce mois-ci"
        />
      </StatsGrid>

      {/* Métriques 30 jours */}
      <StatsGrid>
        <StatCard
          icon="📊"
          variant="dark"
          label="CA 30j"
          value={formatMoney(summary.revenue30d)}
          trend={{
            value: summary.revenueChangePercent,
            label: 'vs période précédente',
          }}
          hint={`${summary.revenueChangePercent > 0 ? '+' : ''}${summary.revenueChangePercent} % vs période précédente`}
        />
        <StatCard
          icon="🛒"
          variant="blue"
          label="Panier moyen"
          value={formatMoney(summary.averageBasket30d)}
          hint="Sur les 30 derniers jours"
        />
        <StatCard
          icon="✅"
          variant="green"
          label="Taux livraison"
          value={`${Math.round(summary.deliveryRate30d * 100)} %`}
          hint="Commandes livrées 30j"
        />
        <StatCard
          icon="🔄"
          variant="blue"
          label="Conversion leads"
          value={`${Math.round(summary.leadConversionRate30d * 100)} %`}
          hint="Leads → vendeurs 30j"
        />
      </StatsGrid>

      {/* Tableau des commandes récentes */}
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
                  <td>
                    <span className="order-id">#{shortId(order.id)}</span>
                  </td>
                  <td>
                    <div className="vendor">
                      <span className="vendor__name">{order.vendorName}</span>
                      <span className="vendor__phone">{order.vendorWhatsApp}</span>
                    </div>
                  </td>
                  <td>
                    <span className="whatsapp">{order.maskedClientPhone}</span>
                  </td>
                  <td>
                    <StatusBadge status={order.statusLabel} />
                  </td>
                </tr>
              ))}
              {summary.recentOrders.length === 0 && (
                <tr>
                  <td colSpan={4} className="empty">
                    Aucune commande en cours
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>

      {/* Top vendeurs & Répartition par zone */}
      <div
        className="grid"
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))',
          gap: 16,
          marginTop: 16,
        }}
      >
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
                      <td><strong>{v.username}</strong></td>
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
                <p className="panel__subtitle">Répartition géographique Grand Abidjan</p>
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
                      <td><strong>{z.zone}</strong></td>
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
