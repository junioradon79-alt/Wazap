import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { UserSummary } from '../api/types'
import {
  EmptyState,
  ErrorAlert,
  Modal,
  PageHeader,
  StatusBadge,
  formatDateTime,
} from '../components'

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
      const data = await api.get<UserSummary[]>('/vendors')
      setVendors(data)
      setError('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur de chargement')
    }
  }

  useEffect(() => {
    void load()
  }, [])

  const doTopup = async (): Promise<void> => {
    if (!topup) return
    const n = Number(credits)
    if (!Number.isInteger(n) || n <= 0) return
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
      <PageHeader
        title="Vendeurs"
        subtitle={`${vendors.length} compte${vendors.length > 1 ? 's' : ''} vendeur${vendors.length > 1 ? 's' : ''} partenaires · crédits et zones déclarées`}
        actions={
          <button type="button" className="btn btn--ghost" onClick={() => void load()}>
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
                  <td>
                    <div className="vendor">
                      <span className="vendor__name">{v.username}</span>
                      <span className="vendor__phone">{v.role}</span>
                    </div>
                  </td>
                  <td>
                    <span className="whatsapp">{v.phoneNumber ?? '—'}</span>
                  </td>
                  <td>
                    <strong>{v.credits}</strong>
                  </td>
                  <td>{v.zone ?? '—'}</td>
                  <td>
                    <StatusBadge status={v.isAvailable ? 'Oui' : 'Non'} />
                  </td>
                  <td>
                    <div style={{ display: 'flex', gap: 6 }}>
                      <button
                        type="button"
                        className="btn btn--primary btn--sm"
                        onClick={() => {
                          setTopup(v)
                          setCredits('')
                        }}
                      >
                        Créditer
                      </button>
                      <button
                        type="button"
                        className="btn btn--ghost btn--sm"
                        onClick={() => {
                          setZoneEdit(v)
                          setZoneValue(v.zone ?? '')
                        }}
                      >
                        Zone
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
              {vendors.length === 0 && (
                <tr>
                  <td colSpan={6}>
                    <EmptyState
                      icon="🏪"
                      title="Aucun vendeur enregistré"
                      description="Les commerçants inscrits via WhatsApp ou la page d'acquisition apparaîtront ici."
                    />
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>

      {topup && (
        <Modal
          title={`Créditer ${topup.username}`}
          labelledBy="vendor-topup-title"
          onClose={() => setTopup(null)}
        >
          <p style={{ color: 'var(--wz-muted)', marginBottom: 16 }}>
            Solde actuel : <b>{topup.credits}</b> crédits · Dernière position :{' '}
            {topup.locationUpdatedAt ? formatDateTime(topup.locationUpdatedAt) : '—'}
          </p>
          <div className="field">
            <label htmlFor="topup-credits">Crédits à ajouter</label>
            <input
              id="topup-credits"
              type="number"
              min={1}
              value={credits}
              onChange={(e) => setCredits(e.target.value)}
              placeholder="ex: 15"
            />
          </div>
          <div className="modal__actions">
            <button type="button" className="btn" onClick={() => setTopup(null)}>
              Annuler
            </button>
            <button
              type="button"
              className="btn btn--primary"
              onClick={() => void doTopup()}
              disabled={busy}
            >
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
            <label htmlFor="vendor-zone-input">Quartier / commune d'Abidjan</label>
            <input
              id="vendor-zone-input"
              value={zoneValue}
              onChange={(e) => setZoneValue(e.target.value)}
              placeholder="ex : Cocody"
              maxLength={50}
            />
          </div>
          <div className="modal__actions">
            <button type="button" className="btn" onClick={() => setZoneEdit(null)}>
              Annuler
            </button>
            <button
              type="button"
              className="btn btn--primary"
              onClick={() => void doZone()}
              disabled={busy}
            >
              {busy ? '…' : 'Enregistrer'}
            </button>
          </div>
        </Modal>
      )}
    </>
  )
}
