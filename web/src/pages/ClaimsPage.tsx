import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { ClaimListItem } from '../api/types'
import { formatDateTime } from '../components/ui'

const CLAIM_STATUS: Record<ClaimListItem['status'], { label: string; badge: string }> = {
  Pending: { label: '🚨 À traiter', badge: 'badge--orange' },
  Approved: { label: '✅ Indemnisé', badge: 'badge--green' },
  Rejected: { label: 'Rejeté', badge: 'badge--red' },
}

export default function ClaimsPage() {
  const [claims, setClaims] = useState<ClaimListItem[]>([])
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [approveTarget, setApproveTarget] = useState<ClaimListItem | null>(null)
  const [compensation, setCompensation] = useState('0')
  const [note, setNote] = useState('')

  const load = async (): Promise<void> => {
    try {
      const data = await api.get<ClaimListItem[]>('/admin/claims')
      setClaims(data)
      setError('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur')
    }
  }

  useEffect(() => {
    void load()
  }, [])

  const openApprove = (c: ClaimListItem): void => {
    setCompensation('0')
    setNote('')
    setApproveTarget(c)
  }

  const doApprove = async (): Promise<void> => {
    if (!approveTarget) return
    setBusy(true)
    try {
      const credits = Number(compensation) || 0
      await api.post(`/admin/claims/${approveTarget.claimId}/approve`, {
        compensationCredits: credits,
        note: note || null,
      })
      setApproveTarget(null)
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Traitement impossible')
    } finally {
      setBusy(false)
    }
  }

  const reject = async (c: ClaimListItem): Promise<void> => {
    const reason = window.prompt(`Rejeter le dossier #${c.orderCode} ?\nMotif (optionnel) :`) || ''
    if (reason === null) return
    setBusy(true)
    try {
      await api.post(`/admin/claims/${c.claimId}/reject`, { note: reason || null })
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Rejet impossible')
    } finally {
      setBusy(false)
    }
  }

  const pending = claims.filter((c) => c.status === 'Pending').length

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Sinistres — Garantie Colis Sûr</h1>
          <p>
            {claims.length} dossiers · 🚨 {pending} à traiter · ✅{' '}
            {claims.filter((c) => c.status === 'Approved').length} indemnisés ·{' '}
            {claims.filter((c) => c.status === 'Rejected').length} rejetés
          </p>
        </div>
        <button className="btn" onClick={() => void load()} disabled={busy}>⟳ Actualiser</button>
      </div>

      {error && <div className="alert alert--error">{error}</div>}

      <section className="panel">
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Course</th>
                <th>Vendeur</th>
                <th>Livreur</th>
                <th>Description</th>
                <th>Statut</th>
                <th>Indemnisation</th>
                <th>Reçu le</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {claims.map((c) => {
                const meta = CLAIM_STATUS[c.status]
                return (
                  <tr key={c.claimId}>
                    <td><span className="order-id">#{c.orderCode}</span></td>
                    <td>
                      <div className="vendor">
                        <span className="vendor__name">{c.vendorName}</span>
                        <span className="vendor__phone">{c.vendorPhone}</span>
                      </div>
                    </td>
                    <td><span className="vendor__name">{c.riderName}</span></td>
                    <td style={{ fontSize: 13 }}>{c.description || '—'}</td>
                    <td><span className={`badge ${meta.badge}`}>{meta.label}</span></td>
                    <td style={{ fontSize: 13 }}>
                      {c.status === 'Approved'
                        ? <>1 (remb.){c.compensationCredits ? ` + ${c.compensationCredits}` : ''} crédits</>
                        : '—'}
                    </td>
                    <td style={{ fontSize: 13 }}>{formatDateTime(c.createdAt)}</td>
                    <td>
                      {c.status === 'Pending' ? (
                        <div style={{ display: 'flex', gap: 6 }}>
                          <button
                            className="btn btn--primary"
                            style={{ padding: '6px 8px', fontSize: 12 }}
                            disabled={busy}
                            onClick={() => openApprove(c)}
                          >
                            Indemniser
                          </button>
                          <button
                            className="btn btn--ghost"
                            style={{ padding: '6px 8px', fontSize: 12, borderColor: '#fca5a5', color: '#b91c1c' }}
                            disabled={busy}
                            onClick={() => void reject(c)}
                          >
                            Rejeter
                          </button>
                        </div>
                      ) : (
                        <span style={{ fontSize: 12, color: 'var(--muted, #888)' }}>
                          {c.reviewNote || 'Traité'}
                        </span>
                      )}
                    </td>
                  </tr>
                )
              })}
              {claims.length === 0 && <tr><td colSpan={8} className="empty">Aucun sinistre — les vendeurs déclarent via « SINISTRE +code » sur WhatsApp.</td></tr>}
            </tbody>
          </table>
        </div>
      </section>

      {approveTarget && (
        <div className="modal-backdrop" onClick={() => setApproveTarget(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <h3>Indemniser le sinistre #{approveTarget.orderCode}</h3>
            <p style={{ fontSize: 13, marginBottom: 12 }}>
              Le livreur <strong>{approveTarget.riderName}</strong> sera <strong>exclu définitivement</strong>.
              Le vendeur {approveTarget.vendorName} sera remboursé du crédit de la course + indemnisation.
            </p>
            <div className="field">
              <label>Crédits d'indemnisation (en plus du remboursement de la course)</label>
              <input
                type="number"
                min={0}
                value={compensation}
                onChange={(e) => setCompensation(e.target.value)}
              />
            </div>
            <div className="field">
              <label>Note de décision (optionnel)</label>
              <input value={note} onChange={(e) => setNote(e.target.value)} maxLength={300} />
            </div>
            <div className="modal__actions">
              <button className="btn" onClick={() => setApproveTarget(null)}>Annuler</button>
              <button className="btn btn--primary" onClick={() => void doApprove()} disabled={busy}>
                {busy ? '…' : '✅ Confirmer l\'indemnisation'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}

