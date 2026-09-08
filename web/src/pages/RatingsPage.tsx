import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { RiderRatingAdmin, RiderRatingAdminBoard } from '../api/types'
import { formatDateTime } from '../components/ui'

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
      setError(err instanceof Error ? err.message : 'Erreur')
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
      <div className="page-head">
        <div>
          <h1>Avis des clients — Réputation livreur</h1>
          <p>
            {summaries.length} livreurs notés ·{' '}
            {board ? `${board.ratings.length} avis` : '…'}
            {overall ? ` · ⭐ moyenne générale ${overall.toFixed(1)}/5` : ''}
            {bestRider ? ` · 🥇 ${bestRider.riderName} (${bestRider.averageScore.toFixed(1)})` : ''}
          </p>
        </div>
        <button className="btn" onClick={() => void load()} disabled={busy}>⟳ Actualiser</button>
      </div>

      {error && <div className="alert alert--error">{error}</div>}

      <div className="page-head" style={{ marginTop: -6, gap: 10, flexWrap: 'wrap' }}>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 14 }}>
          Livreur :
          <select className="input" value={riderFilter} onChange={(e) => setRiderFilter(e.target.value)} style={{ width: 200 }}>
            <option value="">Tous</option>
            {summaries.map((s) => (
              <option key={s.riderId} value={s.riderName}>
                {s.riderName} — ⭐ {s.averageScore.toFixed(1)} ({s.ratingCount})
              </option>
            ))}
          </select>
        </label>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 14 }}>
          Note :
          <select className="input" value={scoreFilter} onChange={(e) => setScoreFilter(e.target.value)} style={{ width: 90 }}>
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
              {ratings.length === 0 && (
                <tr>
                  <td colSpan={7} style={{ textAlign: 'center', color: 'var(--muted)' }}>
                    Aucun avis pour le moment.
                  </td>
                </tr>
              )}
              {ratings.map((r: RiderRatingAdmin) => (
                <tr key={r.ratingId}>
                  <td>
                    <span className="vendor__name">{r.riderName}</span>
                  </td>
                  <td>
                    <span className="mono">{r.orderCode}</span>
                  </td>
                  <td>
                    <span className="whatsapp">{r.maskedClientPhone}</span>
                  </td>
                  <td>
                    <span title={`${r.score}/5`}>{stars(r.score)}</span>
                  </td>
                  <td style={{ fontSize: 13 }}>{r.comment ?? '—'}</td>
                  <td style={{ fontSize: 13 }}>
                    {r.reply ? (
                      <>
                        <span style={{ color: 'var(--green, #1a7f37)' }}>🗣️ {r.reply}</span>
                        {r.repliedAt ? (
                          <span style={{ display: 'block', fontSize: 11, color: 'var(--muted)' }}>
                            {formatDateTime(r.repliedAt)}
                          </span>
                        ) : null}
                      </>
                    ) : (
                      <span style={{ color: 'var(--muted)' }}>—</span>
                    )}
                  </td>
                  <td style={{ fontSize: 12 }}>{formatDateTime(r.createdAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </>
  )
}