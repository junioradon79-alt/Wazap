import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { UserSummary } from '../api/types'
import { ErrorAlert, StatusBadge, formatDateTime } from '../components/ui'
import { Modal } from '../components/Modal'

export default function VendorsPage() {
  const [vendors, setVendors] = useState<UserSummary[]>([])
  const [error, setError] = useState('')
  const [topup, setTopup] = useState<UserSummary | null>(null)
  const [credits, setCredits] = useState('')
  const [zoneEdit, setZoneEdit] = useState<UserSummary | null>(null)
  const [zoneValue, setZoneValue] = useState('')
  const [busy, setBusy] = useState(false)

  const load = async (): Promise<void> => {
    try {
      setVendors(await api.get<UserSummary[]>('/vendors'))
      setError('') // une action réussie ne doit pas laisser une bannière d'erreur périmée
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur')
    }
  }

  useEffect(() => {
    void load()
  }, [])

  const doTopup = async (): Promise<void> => {
    if (!topup) return
    const n = Number(credits)
    if (!Number.isInteger(n) || n <= 0) return
    // Crédits OFFERTS sans paiement : confirmation explicite (opération financière).
    if (!window.confirm(`Offrir ${n} crédit(s) à ${topup.username} sans paiement ?`)) return
    setBusy(true)
    setError('')
    try {
      await api.post(`/vendors/${topup.id}/credits/topup`, { credits: n })
      setTopup(null)
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur')
    } finally {
      setBusy(false)
    }
  }

  const doZone = async (): Promise<void> => {
    if (!zoneEdit) return
    setBusy(true)
    setError('')
    try {
      await api.put(`/vendors/${zoneEdit.id}/zone`, { zone: zoneValue })
      setZoneEdit(null)
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Vendeurs</h1>
          <p>Comptes vendeurs, crédits et zones déclarées</p>
        </div>
      </div>

      {error && <ErrorAlert message={error} onRetry={() => void load()} />}

      <section className="panel">
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Vendeur</th>
                <th>Téléphone</th>
                <th>Crédits</th>
                <th>Zone</th>
                <th>Dispo</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {vendors.map((v) => (
                <tr key={v.id}>
                  <td><div className="vendor"><span className="vendor__name">{v.username}</span><span className="vendor__phone">{v.role}</span></div></td>
                  <td><span className="whatsapp">{v.phoneNumber ?? '—'}</span></td>
                  <td><b>{v.credits}</b></td>
                  <td>{v.zone ?? '—'}</td>
                  <td><StatusBadge status={v.isAvailable ? 'Oui' : 'Non'} /></td>
                  <td>
                    <div style={{ display: 'flex', gap: 8 }}>
                      <button className="btn btn--primary" onClick={() => { setTopup(v); setCredits('') }}>Créditer</button>
                      <button className="btn" onClick={() => { setZoneEdit(v); setZoneValue(v.zone ?? '') }}>Zone</button>
                    </div>
                  </td>
                </tr>
              ))}
              {vendors.length === 0 && <tr><td colSpan={6} className="empty">Aucun vendeur</td></tr>}
            </tbody>
          </table>
        </div>
      </section>

      {topup && (
        // Modales partagées (C-07) : rôle dialog, Échap, focus déplacé puis restitué.
        <Modal
          title={`Créditer ${topup.username}`}
          labelledBy="vendor-topup-title"
          onClose={() => setTopup(null)}
        >
            <p style={{ color: 'var(--wz-muted)' }}>
              Solde actuel : <b>{topup.credits}</b> crédits · Dernière position : {topup.locationUpdatedAt ? formatDateTime(topup.locationUpdatedAt) : '—'}
            </p>
            <div className="field">
              <label>Crédits à ajouter</label>
              <input type="number" min={1} value={credits} onChange={(e) => setCredits(e.target.value)} />
            </div>
            <div className="modal__actions">
              <button className="btn" onClick={() => setTopup(null)}>Annuler</button>
              <button className="btn btn--primary" onClick={() => void doTopup()} disabled={busy}>
                {busy ? '…' : 'Créditer'}
              </button>
            </div>
        </Modal>
      )}

      {zoneEdit && (
        <Modal
          title={`Zone de ${zoneEdit.username}`}
          labelledBy="vendor-zone-title"
          onClose={() => setZoneEdit(null)}
        >
            <div className="field">
              <label>Quartier / zone</label>
              <input value={zoneValue} onChange={(e) => setZoneValue(e.target.value)} placeholder="ex : Cocody" maxLength={50} />
            </div>
            <div className="modal__actions">
              <button className="btn" onClick={() => setZoneEdit(null)}>Annuler</button>
              <button className="btn btn--primary" onClick={() => void doZone()} disabled={busy}>
                {busy ? '…' : 'Enregistrer'}
              </button>
            </div>
        </Modal>
      )}
    </>
  )
}
