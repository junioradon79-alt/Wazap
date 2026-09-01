import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { UserSummary } from '../api/types'
import { StatusBadge, formatDateTime } from '../components/ui'

export default function RidersPage() {
  const [riders, setRiders] = useState<UserSummary[]>([])
  const [error, setError] = useState('')
  const [zoneEdit, setZoneEdit] = useState<UserSummary | null>(null)
  const [zoneValue, setZoneValue] = useState('')
  const [busy, setBusy] = useState(false)

  const load = async (): Promise<void> => {
    try {
      setRiders(await api.get<UserSummary[]>('/riders'))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur')
    }
  }

  useEffect(() => {
    void load()
  }, [])

  const doZone = async (): Promise<void> => {
    if (!zoneEdit) return
    setBusy(true)
    try {
      await api.put(`/riders/${zoneEdit.id}/zone`, { zone: zoneValue })
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
          <h1>Livreurs</h1>
          <p>Livreurs inscrits, disponibilité, zone et dernière position GPS</p>
        </div>
      </div>

      {error && <div className="alert alert--error">{error}</div>}

      <section className="panel">
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Livreur</th>
                <th>Téléphone</th>
                <th>Zone</th>
                <th>Dispo</th>
                <th>GPS</th>
                <th>Dernière position</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {riders.map((r) => (
                <tr key={r.id}>
                  <td><span className="vendor__name">{r.username}</span></td>
                  <td><span className="whatsapp">{r.phoneNumber ?? '—'}</span></td>
                  <td>{r.zone ?? '—'}</td>
                  <td><StatusBadge status={r.isAvailable ? 'Oui' : 'Non'} /></td>
                  <td>
                    {r.latitude != null && r.longitude != null
                      ? <span className="mono">{r.latitude.toFixed(4)}, {r.longitude.toFixed(4)}</span>
                      : '—'}
                  </td>
                  <td>{r.locationUpdatedAt ? formatDateTime(r.locationUpdatedAt) : '—'}</td>
                  <td>
                    <button className="btn" onClick={() => { setZoneEdit(r); setZoneValue(r.zone ?? '') }}>Zone</button>
                  </td>
                </tr>
              ))}
              {riders.length === 0 && <tr><td colSpan={7} className="empty">Aucun livreur</td></tr>}
            </tbody>
          </table>
        </div>
      </section>

      {zoneEdit && (
        <div className="modal-backdrop" onClick={() => setZoneEdit(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <h3>Zone de {zoneEdit.username}</h3>
            <div className="field">
              <label>Quartier / zone</label>
              <input value={zoneValue} onChange={(e) => setZoneValue(e.target.value)} placeholder="ex : Yopougon" maxLength={50} />
            </div>
            <div className="modal__actions">
              <button className="btn" onClick={() => setZoneEdit(null)}>Annuler</button>
              <button className="btn btn--primary" onClick={() => void doZone()} disabled={busy}>
                {busy ? '…' : 'Enregistrer'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
