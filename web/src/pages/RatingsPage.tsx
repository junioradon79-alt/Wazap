import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { RiderRatingAdmin, RiderRatingAdminBoard } from '../api/types'
import { EmptyState, ErrorAlert, PageHeader, formatDateTime } from '../components'

function stars(score: number): string {
  return '⭐'.repeat(score) + '☆'.repeat(5 - score)
}

export default function RatingsPage() {
  const [board, setBoard] = useState<RiderRatingAdminBoard | null>(null)
  const [error, setError] = useState('')
  const [riderFilter, setRiderFilter] = useState('')
  const [scoreFilter, setScoreFilter] = useState('')
  const [busy, setBusy] = useState(false)

  const load = async (): Promise<void> => {
    setBusy(true)
    try {
      const data = await api.get<RiderRatingAdminBoard>('/admin/ratings')
      setBoard(data)
      setError('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur de chargement')
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    void load()
  }, [])

  const summaries = board?.riderSummaries ?? []
  const ratings = (board?.ratings ?? []).filter(
    (r) =>
      (riderFilter === '' || r.riderName === riderFilter) &&
      (scoreFilter === '' || String(r.score) === scoreFilter),
  )

  const overall =
    summaries.filter((s) => s.ratingCount > 0).length === 0
      ? null
      : summaries.filter((s) => s.ratingCount > 0).reduce((acc, s) => acc + s.averageScore, 0) /
        summaries.filter((s) => s.ratingCount > 0).length

  const bestRider = summaries
    .filter((s) => s.ratingCount > 0)
    .sort((a, b) => b.averageScore - a.averageScore)[0]

  return (
    <>
      <PageHeader
        title="Avis des clients — Réputation livreur"
        subtitle={
          <>
            {summaries.length} livreurs notés · {board ? `${board.ratings.length} avis` : '…'}
            {overall ? ` · ⭐ moyenne générale ${overall.toFixed(1)}/5` : ''}
            {bestRider ? ` · 🥇 ${bestRider.riderName} (${bestRider.averageScore.toFixed(1)})` : ''}
          </>
        }
        actions={
          <button type="button" className="btn btn--ghost" onClick={() => void load()} disabled={busy}>
            ⟳ Actualiser
          </button>
        }
      />

      {error && <ErrorAlert message={error} onRetry={() => void load()} />}

      <div className="filter-bar" style={{ display: 'flex', gap: 12, marginBottom: 18, flexWrap: 'wrap' }}>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, fontWeight: 600 }}>
          Livreur :
          <select
            className="field input"
            value={riderFilter}
            onChange={(e) => setRiderFilter(e.target.value)}
            style={{ width: 220, padding: '8px 12px', borderRadius: 8, border: '1px solid var(--wz-border)' }}
          >
            <option value="">Tous les livreurs</option>
            {summaries.map((s) => (
              <option key={s.riderId} value={s.riderName}>
                {s.riderName} — ⭐ {s.averageScore.toFixed(1)} ({s.ratingCount})
              </option>
            ))}
          </select>
        </label>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, fontWeight: 600 }}>
          Note :
          <select
            className="field input"
            value={scoreFilter}
            onChange={(e) => setScoreFilter(e.target.value)}
            style={{ width: 120, padding: '8px 12px', borderRadius: 8, border: '1px solid var(--wz-border)' }}
          >
            <option value="">Toutes</option>
            {[5, 4, 3, 2, 1].map((n) => (
              <option key={n} value={String(n)}>
                {stars(n)}
              </option>
            ))}
          </select>
        </label>
      </div>

      <section className="panel">
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Livreur</th>
                <th>Course</th>
                <th>Client</th>
                <th>Note</th>
                <th>Commentaire</th>
                <th>Réponse du livreur</th>
                <th>Date</th>
              </tr>
            </thead>
            <tbody>
              {ratings.map((r: RiderRatingAdmin) => (
                <tr key={r.ratingId}>
                  <td>
                    <strong>{r.riderName}</strong>
                  </td>
                  <td>
                    <span className="order-id">#{r.orderCode}</span>
                  </td>
                  <td>
                    <span className="whatsapp">{r.maskedClientPhone}</span>
                  </td>
                  <td>
                    <span title={`${r.score}/5`} style={{ letterSpacing: 1 }}>{stars(r.score)}</span>
                  </td>
                  <td style={{ fontSize: 13 }}>{r.comment ?? '—'}</td>
                  <td style={{ fontSize: 13 }}>
                    {r.reply ? (
                      <>
                        <span style={{ color: 'var(--wz-green-ink)', fontWeight: 600 }}>🗣️ {r.reply}</span>
                        {r.repliedAt ? (
                          <span style={{ display: 'block', fontSize: 11, color: 'var(--wz-muted)' }}>
                            {formatDateTime(r.repliedAt)}
                          </span>
                        ) : null}
                      </>
                    ) : (
                      <span style={{ color: 'var(--wz-muted)' }}>—</span>
                    )}
                  </td>
                  <td style={{ fontSize: 13 }}>{formatDateTime(r.createdAt)}</td>
                </tr>
              ))}
              {ratings.length === 0 && (
                <tr>
                  <td colSpan={7}>
                    <EmptyState
                      icon="⭐"
                      title="Aucun avis trouvé"
                      description="Les notes et avis déposés par les acheteurs à la réception du colis s'afficheront ici."
                    />
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
