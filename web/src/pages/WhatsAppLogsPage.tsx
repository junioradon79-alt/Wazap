import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'
import type { WhatsAppCostSummaryDto, WhatsAppMessageLogDto } from '../api/types'
import { ErrorAlert, StatusBadge, formatDateTime, shortId } from '../components/ui'

type PeriodFilter = 'today' | '7d' | '30d' | 'all'

const CATEGORY_CONFIG: Record<string, { label: string; badge: string; desc: string }> = {
  utility: {
    label: 'Utility',
    badge: 'badge--blue',
    desc: 'Notifications commande & suivi (~2,27 FCFA)',
  },
  marketing: {
    label: 'Marketing',
    badge: 'badge--orange',
    desc: 'Relances & acquisition (~12,79 FCFA)',
  },
  service: {
    label: 'Service',
    badge: 'badge--green',
    desc: 'Réponses conversation 24h (Gratuit / 0 FCFA)',
  },
  authentication: {
    label: 'Auth',
    badge: 'badge--gray',
    desc: 'Codes de sécurité OTP (~2,27 FCFA)',
  },
}

export default function WhatsAppLogsPage() {
  const [summary, setSummary] = useState<WhatsAppCostSummaryDto | null>(null)
  const [logs, setLogs] = useState<WhatsAppMessageLogDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  // Filtres
  const [period, setPeriod] = useState<PeriodFilter>('30d')
  const [phoneSearch, setPhoneSearch] = useState('')
  const [orderSearch, setOrderSearch] = useState('')
  const [limit, setLimit] = useState(100)
  const [directionFilter, setDirectionFilter] = useState('')
  const [categoryFilter, setCategoryFilter] = useState('')
  const [statusFilter, setStatusFilter] = useState('')

  const getPeriodRange = (p: PeriodFilter): { from?: string; to?: string } => {
    const now = new Date()
    if (p === 'today') {
      const startOfDay = new Date(now.getFullYear(), now.getMonth(), now.getDate())
      return { from: startOfDay.toISOString(), to: now.toISOString() }
    }
    if (p === '7d') {
      const from = new Date(Date.now() - 7 * 24 * 3600 * 1000)
      return { from: from.toISOString(), to: now.toISOString() }
    }
    if (p === '30d') {
      const from = new Date(Date.now() - 30 * 24 * 3600 * 1000)
      return { from: from.toISOString(), to: now.toISOString() }
    }
    return {}
  }

  const load = useCallback(async (): Promise<void> => {
    setLoading(true)
    setError('')
    try {
      const { from, to } = getPeriodRange(period)
      const costParams = new URLSearchParams()
      if (from) costParams.set('from', from)
      if (to) costParams.set('to', to)
      const costQuery = costParams.toString() ? `?${costParams.toString()}` : ''

      const logParams = new URLSearchParams()
      logParams.set('limit', String(limit))
      if (phoneSearch.trim()) logParams.set('phone', phoneSearch.trim())
      if (orderSearch.trim()) logParams.set('orderId', orderSearch.trim())
      const logQuery = `?${logParams.toString()}`

      const [summaryRes, logsRes] = await Promise.all([
        api.get<WhatsAppCostSummaryDto>(`/admin/whatsapp/costs${costQuery}`),
        api.get<WhatsAppMessageLogDto[]>(`/admin/whatsapp/logs${logQuery}`),
      ])

      setSummary(summaryRes)
      setLogs(logsRes)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur de chargement des données WhatsApp.')
    } finally {
      setLoading(false)
    }
  }, [period, limit, phoneSearch, orderSearch])

  useEffect(() => {
    void load()
  }, [load])

  // Filtrage local en mémoire pour affinage instantané du tableau
  const filteredLogs = useMemo(() => {
    return logs.filter((log) => {
      if (directionFilter && log.direction.toLowerCase() !== directionFilter.toLowerCase()) {
        return false
      }
      if (categoryFilter && (log.category || 'service').toLowerCase() !== categoryFilter.toLowerCase()) {
        return false
      }
      if (statusFilter && log.status.toLowerCase() !== statusFilter.toLowerCase()) {
        return false
      }
      return true
    })
  }, [logs, directionFilter, categoryFilter, statusFilter])

  const formatCost = (val: number | null | undefined): string => {
    if (val === null || val === undefined || !Number.isFinite(val)) return '0,00 FCFA'
    return `${val.toFixed(2).replace('.', ',')} FCFA`
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>💬 Journal WhatsApp &amp; Coûts Meta</h1>
          <p>Audit des messages WhatsApp et suivi financier des coûts de conversation Meta en Côte d&apos;Ivoire</p>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <div style={{ display: 'flex', background: '#eef2f7', borderRadius: 8, padding: 3, gap: 2 }}>
            {(
              [
                ['today', "Aujourd'hui"],
                ['7d', '7 jours'],
                ['30d', '30 jours'],
                ['all', 'Tout'],
              ] as const
            ).map(([p, label]) => (
              <button
                key={p}
                type="button"
                className={`btn ${period === p ? 'btn--primary' : 'btn--ghost'}`}
                style={{
                  border: 'none',
                  padding: '6px 12px',
                  fontSize: 13,
                  borderRadius: 6,
                  fontWeight: period === p ? 700 : 500,
                }}
                onClick={() => setPeriod(p)}
              >
                {label}
              </button>
            ))}
          </div>
          <button
            type="button"
            className="btn btn--ghost"
            onClick={() => void load()}
            disabled={loading}
            title="Actualiser les données"
          >
            🔄 {loading ? 'Chargement…' : 'Actualiser'}
          </button>
        </div>
      </div>

      {error && <ErrorAlert message={error} onRetry={() => void load()} />}

      {summary && (
        <>
          <section className="stats">
            <article className="stat-card stat-card--dark">
              <div className="stat-card__icon">💰</div>
              <div className="stat-card__body">
                <span className="stat-card__label">Coût Total Estimé</span>
                <span className="stat-card__value">{formatCost(summary.totalEstimatedCostFcfa)}</span>
                <span className="stat-card__hint">
                  {period === 'today'
                    ? "Aujourd'hui"
                    : period === '7d'
                      ? '7 derniers jours'
                      : period === '30d'
                        ? '30 derniers jours'
                        : 'Historique complet'}
                </span>
              </div>
            </article>

            <article className="stat-card stat-card--blue">
              <div className="stat-card__icon">📦</div>
              <div className="stat-card__body">
                <span className="stat-card__label">Coût Moyen / Course</span>
                <span className="stat-card__value">{formatCost(summary.averageCostPerOrder)}</span>
                <span className="stat-card__hint">Sur les courses avec messages</span>
              </div>
            </article>

            <article className="stat-card stat-card--green">
              <div className="stat-card__icon">💬</div>
              <div className="stat-card__body">
                <span className="stat-card__label">Total Messages</span>
                <span className="stat-card__value">{summary.totalMessages}</span>
                <span className="stat-card__hint">
                  ↗️ {summary.outboundCount} sortants · ↙️ {summary.inboundCount} entrants
                </span>
              </div>
            </article>

            <article className={`stat-card ${summary.failedCount > 0 ? 'stat-card--orange' : 'stat-card--blue'}`}>
              <div className="stat-card__icon">⚠️</div>
              <div className="stat-card__body">
                <span className="stat-card__label">Échecs d&apos;Envoi</span>
                <span className="stat-card__value">{summary.failedCount}</span>
                <span className="stat-card__hint">
                  {summary.totalMessages > 0
                    ? `${((summary.failedCount / summary.totalMessages) * 100).toFixed(1)} % d'échec`
                    : 'Aucun échec'}
                </span>
              </div>
            </article>
          </section>

          <section className="panel" style={{ marginBottom: 24 }}>
            <div className="panel__header">
              <div>
                <h2 className="panel__title">Répartition par Catégorie Meta (Côte d&apos;Ivoire)</h2>
                <p className="panel__subtitle">
                  Tarification Meta par modèle de conversation pour la zone Afrique subsaharienne (XOF)
                </p>
              </div>
            </div>
            <div className="panel__body">
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 16 }}>
                {(['utility', 'marketing', 'service', 'authentication'] as const).map((catKey) => {
                  const cfg = CATEGORY_CONFIG[catKey]
                  const count = summary.messagesByCategory[catKey] ?? 0
                  const cost = summary.costByCategory[catKey] ?? 0
                  return (
                    <div
                      key={catKey}
                      style={{
                        background: '#f8fafc',
                        border: '1px solid var(--wz-border)',
                        borderRadius: 8,
                        padding: 16,
                      }}
                    >
                      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
                        <span className={`badge ${cfg.badge}`} style={{ fontSize: 12, textTransform: 'uppercase' }}>
                          {cfg.label}
                        </span>
                        <span style={{ fontSize: 13, fontWeight: 700, color: 'var(--wz-muted)' }}>
                          {count} msg
                        </span>
                      </div>
                      <div style={{ fontSize: 20, fontWeight: 800, margin: '4px 0' }}>
                        {formatCost(cost)}
                      </div>
                      <div style={{ fontSize: 11, color: 'var(--wz-muted)' }}>
                        {cfg.desc}
                      </div>
                    </div>
                  )
                })}
              </div>
            </div>
          </section>
        </>
      )}

      <section className="panel">
        <div className="panel__header" style={{ flexWrap: 'wrap', gap: 16 }}>
          <div>
            <h2 className="panel__title">Journal d&apos;Audit des Messages WhatsApp</h2>
            <p className="panel__subtitle">
              {filteredLogs.length} message(s) affiché(s) sur {logs.length} chargé(s)
            </p>
          </div>
          <div className="panel__actions" style={{ gap: 8, flexWrap: 'wrap' }}>
            <input
              type="text"
              className="input"
              placeholder="🔍 Filtrer par tél (+225...)"
              value={phoneSearch}
              onChange={(e) => setPhoneSearch(e.target.value)}
              style={{ width: 170, height: 36, fontSize: 13 }}
            />
            <input
              type="text"
              className="input"
              placeholder="📦 ID Commande"
              value={orderSearch}
              onChange={(e) => setOrderSearch(e.target.value)}
              style={{ width: 140, height: 36, fontSize: 13 }}
            />
            <select
              className="input"
              value={directionFilter}
              onChange={(e) => setDirectionFilter(e.target.value)}
              style={{ width: 120, height: 36, fontSize: 13 }}
              aria-label="Filtrer par sens"
            >
              <option value="">Tous sens</option>
              <option value="Outbound">↗️ Sortant</option>
              <option value="Inbound">↙️ Entrant</option>
            </select>
            <select
              className="input"
              value={categoryFilter}
              onChange={(e) => setCategoryFilter(e.target.value)}
              style={{ width: 130, height: 36, fontSize: 13 }}
              aria-label="Filtrer par catégorie"
            >
              <option value="">Toutes catégories</option>
              <option value="utility">Utility</option>
              <option value="marketing">Marketing</option>
              <option value="service">Service</option>
              <option value="authentication">Auth</option>
            </select>
            <select
              className="input"
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
              style={{ width: 120, height: 36, fontSize: 13 }}
              aria-label="Filtrer par statut"
            >
              <option value="">Tous statuts</option>
              <option value="Sent">Sent</option>
              <option value="Delivered">Delivered</option>
              <option value="Read">Read</option>
              <option value="Received">Received</option>
              <option value="Failed">Failed</option>
            </select>
            <select
              className="input"
              value={limit}
              onChange={(e) => setLimit(Number(e.target.value))}
              style={{ width: 90, height: 36, fontSize: 13 }}
              aria-label="Limite de lignes"
            >
              <option value={50}>50</option>
              <option value={100}>100</option>
              <option value={200}>200</option>
              <option value={500}>500</option>
            </select>
          </div>
        </div>

        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Date &amp; Heure</th>
                <th>Sens</th>
                <th>Destinataire</th>
                <th>Type / Modèle</th>
                <th>Catégorie</th>
                <th>Statut</th>
                <th>Coût Estimé</th>
                <th>Détails &amp; Réf</th>
              </tr>
            </thead>
            <tbody>
              {filteredLogs.map((log) => {
                const cat = (log.category || 'service').toLowerCase()
                const catCfg = CATEGORY_CONFIG[cat] || {
                  label: log.category || 'Autre',
                  badge: 'badge--gray',
                }

                return (
                  <tr key={log.id}>
                    <td style={{ whiteSpace: 'nowrap', fontSize: 13 }}>
                      {formatDateTime(log.createdAt)}
                    </td>
                    <td>
                      {log.direction.toLowerCase() === 'inbound' ? (
                        <span className="badge badge--green" title="Message entrant reçu de l'utilisateur">
                          ↙️ Entrant
                        </span>
                      ) : (
                        <span className="badge badge--blue" title="Message sortant envoyé">
                          ↗️ Sortant
                        </span>
                      )}
                    </td>
                    <td>
                      <span style={{ fontWeight: 600 }}>{log.recipientPhone}</span>
                      {log.senderPhone && (
                        <div style={{ fontSize: 11, color: 'var(--wz-muted)' }}>
                          De: {log.senderPhone}
                        </div>
                      )}
                    </td>
                    <td>
                      {log.templateName ? (
                        <code
                          style={{
                            background: '#f1f5f9',
                            padding: '2px 6px',
                            borderRadius: 4,
                            fontSize: 12,
                            fontWeight: 600,
                          }}
                        >
                          {log.templateName}
                        </code>
                      ) : (
                        <span style={{ fontSize: 13, color: 'var(--wz-muted)' }}>
                          {log.messageType || 'Texte libre'}
                        </span>
                      )}
                    </td>
                    <td>
                      <span className={`badge ${catCfg.badge}`}>
                        {catCfg.label}
                      </span>
                    </td>
                    <td>
                      <StatusBadge status={log.status} />
                    </td>
                    <td style={{ fontWeight: 700, whiteSpace: 'nowrap' }}>
                      {formatCost(log.estimatedCostFcfa)}
                    </td>
                    <td>
                      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                        {log.orderId && (
                          <span className="order-id" title={`Commande ${log.orderId}`}>
                            #{shortId(log.orderId)}
                          </span>
                        )}
                        {log.errorMessage ? (
                          <span
                            className="badge badge--red"
                            style={{ fontSize: 11, maxWidth: 220, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
                            title={`Erreur ${log.errorCode ?? ''}: ${log.errorMessage}`}
                          >
                            ⚠️ {log.errorMessage}
                          </span>
                        ) : log.providerMessageId ? (
                          <span
                            style={{ fontSize: 11, color: 'var(--wz-muted)' }}
                            title={`Provider ID: ${log.providerMessageId} (${log.provider})`}
                          >
                            {log.provider}
                          </span>
                        ) : (
                          <span style={{ fontSize: 11, color: 'var(--wz-muted)' }}>{log.provider}</span>
                        )}
                      </div>
                    </td>
                  </tr>
                )
              })}
              {filteredLogs.length === 0 && (
                <tr>
                  <td colSpan={8} className="empty" style={{ textAlign: 'center', padding: '32px 16px' }}>
                    {loading ? 'Chargement des messages…' : 'Aucun message WhatsApp trouvé pour cette sélection.'}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </>
  )
}
