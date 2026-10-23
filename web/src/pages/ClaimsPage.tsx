import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { ClaimListItem } from '../api/types'
import { EmptyState, ErrorAlert, Modal, PageHeader, formatDateTime } from '../components'

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
  const [amountFcfa, setAmountFcfa] = useState('0')
  const [note, setNote] = useState('')

  const load = async (): Promise<void> => {
    try {
      const data = await api.get<ClaimListItem[]>('/admin/claims')
      setClaims(data)
      setError('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur de chargement')
    }
  }

  useEffect(() => {
    void load()
  }, [])

  const openApprove = (c: ClaimListItem): void => {
    setCompensation('0')
    setNote('')
    setAmountFcfa(String(c.suggestedCompensationFcfa ?? 0))
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
        compensationAmountFcfa: Number(amountFcfa) || 0,
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
    const reason = window.prompt(`Rejeter le dossier #${c.orderCode} ?\nMotif (optionnel) :`)
    if (reason === null) return
    if (!window.confirm(`Confirmer le REJET définitif du dossier #${c.orderCode} ?`)) return

    setBusy(true)
    try {
      await api.post(`/admin/claims/${c.claimId}/reject`, { note: reason.trim() || null })
      setError('')
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Rejet impossible')
    } finally {
      setBusy(false)
    }
  }

  const markPaid = async (c: ClaimListItem): Promise<void> => {
    const reference = window.prompt(
      `Versement de ${c.compensationAmountFcfa} FCFA à ${c.vendorName} (${c.vendorPhone ?? 'numéro inconnu'}).\n` +
        'Référence de la transaction Mobile Money :',
    )
    if (!reference || !reference.trim()) return
    setBusy(true)
    try {
      await api.post(`/admin/claims/${c.claimId}/payout`, { reference: reference.trim() })
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Confirmation impossible')
    } finally {
      setBusy(false)
    }
  }

  const pending = claims.filter((c) => c.status === 'Pending').length
  const toPay = claims.filter((c) => c.payoutStatus === 'Pending' || c.payoutStatus === 'Failed').length

  return (
    <>
      <PageHeader
        title="Sinistres — Garantie Colis Sûr"
        subtitle={`${claims.length} dossier${claims.length > 1 ? 's' : ''} · 🚨 ${pending} à traiter · ✅ ${claims.filter((c) => c.status === 'Approved').length} indemnisés · ${claims.filter((c) => c.status === 'Rejected').length} rejetés${toPay > 0 ? ` · 💰 ${toPay} versement(s) à effectuer` : ''}`}
        actions={
          <button type="button" className="btn btn--ghost" onClick={() => void load()} disabled={busy}>
            ⟳ Actualiser
          </button>
        }
      />

      {error && <ErrorAlert message={error} onRetry={() => void load()} />}

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
                    <td><strong>{c.riderName}</strong></td>
                    <td style={{ fontSize: 13 }}>{c.description || '—'}</td>
                    <td><span className={`badge ${meta.badge}`}>{meta.label}</span></td>
                    <td style={{ fontSize: 13 }}>
                      {c.status === 'Approved' ? (
                        <>
                          1 (remb.){c.compensationCredits ? ` + ${c.compensationCredits}` : ''} crédits
                          {c.compensationAmountFcfa ? (
                            <div style={{ marginTop: 2 }}>
                              💰 {c.compensationAmountFcfa.toLocaleString('fr-FR')} FCFA
                              {c.payoutStatus === 'Paid' ? (
                                <span className="badge badge--green" style={{ marginLeft: 6 }}>versé</span>
                              ) : (
                                <span className="badge badge--orange" style={{ marginLeft: 6 }}>
                                  {c.payoutStatus === 'Failed' ? 'échec' : 'à verser'}
                                </span>
                              )}
                              {c.riderDepositDebitedFcfa ? (
                                <div style={{ fontSize: 11, color: 'var(--wz-muted)' }}>
                                  dont {c.riderDepositDebitedFcfa.toLocaleString('fr-FR')} F sur la caution livreur
                                </div>
                              ) : null}
                              {c.payoutReference ? (
                                <div style={{ fontSize: 11, color: 'var(--wz-muted)' }}>réf. {c.payoutReference}</div>
                              ) : null}
                            </div>
                          ) : null}
                        </>
                      ) : (
                        '—'
                      )}
                    </td>
                    <td style={{ fontSize: 13 }}>{formatDateTime(c.createdAt)}</td>
                    <td>
                      {c.status === 'Pending' ? (
                        <div style={{ display: 'flex', gap: 6 }}>
                          <button
                            type="button"
                            className="btn btn--primary btn--sm"
                            disabled={busy}
                            onClick={() => openApprove(c)}
                          >
                            Indemniser
                          </button>
                          <button
                            type="button"
                            className="btn btn--danger btn--sm"
                            disabled={busy}
                            onClick={() => void reject(c)}
                          >
                            Rejeter
                          </button>
                        </div>
                      ) : c.payoutStatus === 'Pending' || c.payoutStatus === 'Failed' ? (
                        <button
                          type="button"
                          className="btn btn--primary btn--sm"
                          disabled={busy}
                          onClick={() => void markPaid(c)}
                        >
                          💰 Verser
                        </button>
                      ) : (
                        <span style={{ fontSize: 12, color: 'var(--wz-muted)' }}>
                          {c.reviewNote || 'Traité'}
                        </span>
                      )}
                    </td>
                  </tr>
                )
              })}
              {claims.length === 0 && (
                <tr>
                  <td colSpan={8}>
                    <EmptyState
                      icon="🛡️"
                      title="Aucun sinistre en cours"
                      description="La Garantie Colis Sûr protège les commerçants. Les déclarations reçues via WhatsApp apparaîtront ici."
                    />
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>

      {approveTarget && (
        <Modal
          title={`Indemniser le sinistre #${approveTarget.orderCode}`}
          labelledBy="claim-approve-title"
          onClose={() => setApproveTarget(null)}
        >
          <p style={{ fontSize: 13, marginBottom: 14, color: 'var(--wz-muted)' }}>
            Le livreur <strong>{approveTarget.riderName}</strong> sera exclu si négligence prouvée.
            Le vendeur <strong>{approveTarget.vendorName}</strong> sera remboursé du crédit de la course et indemnisé.
          </p>
          <div className="field">
            <label htmlFor="claim-compensation-credits">Crédits d'indemnisation (en plus du remboursement)</label>
            <input
              id="claim-compensation-credits"
              type="number"
              min={0}
              value={compensation}
              onChange={(e) => setCompensation(e.target.value)}
            />
          </div>
          <div className="field">
            <label htmlFor="claim-amount-fcfa">
              Indemnisation en FCFA (barème recommandé :{' '}
              {approveTarget.suggestedCompensationFcfa.toLocaleString('fr-FR')} F)
            </label>
            <input
              id="claim-amount-fcfa"
              type="number"
              min={0}
              value={amountFcfa}
              onChange={(e) => setAmountFcfa(e.target.value)}
            />
            <small style={{ color: 'var(--wz-muted)', marginTop: 4 }}>
              0 = aucun virement d'espèces. Sinon le dossier passe en attente de virement Mobile Money.
            </small>
          </div>
          <div className="field">
            <label htmlFor="claim-note">Note de décision / motif (optionnel)</label>
            <input
              id="claim-note"
              value={note}
              onChange={(e) => setNote(e.target.value)}
              maxLength={300}
              placeholder="ex: Colis égaré constaté"
            />
          </div>
          <div className="modal__actions">
            <button type="button" className="btn" onClick={() => setApproveTarget(null)}>
              Annuler
            </button>
            <button
              type="button"
              className="btn btn--primary"
              onClick={() => void doApprove()}
              disabled={busy}
            >
              {busy ? '…' : '✅ Confirmer l\'indemnisation'}
            </button>
          </div>
        </Modal>
      )}
    </>
  )
}
