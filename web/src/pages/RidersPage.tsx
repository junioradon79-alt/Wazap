import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { RiderCertification, UserSummary } from '../api/types'
import { StatusBadge } from '../components/ui'

const CERT_STATUS: Record<RiderCertification['status'], { label: string; badge: string }> = {
  Pending: { label: 'À vérifier', badge: 'badge--orange' },
  Verified: { label: '✔ Certifié', badge: 'badge--green' },
  Rejected: { label: 'Refusé', badge: 'badge--red' },
  Blacklisted: { label: '⛔ Exclu', badge: 'badge--red' },
}

export default function RidersPage() {
  const [riders, setRiders] = useState<UserSummary[]>([])
  const [certById, setCertById] = useState<Record<string, RiderCertification>>({})
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [verifyTarget, setVerifyTarget] = useState<{ rider: UserSummary; cert: RiderCertification } | null>(null)
  const [verifyForm, setVerifyForm] = useState({ fullName: '', cniNumber: '', motorcyclePlate: '' })

  const load = async (): Promise<void> => {
    try {
      const [list, certs] = await Promise.all([
        api.get<UserSummary[]>('/riders'),
        api.get<RiderCertification[]>('/riders/certifications'),
      ])
      setRiders(list)
      setCertById(Object.fromEntries(certs.map((c) => [c.riderId, c])))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur')
    }
  }

  useEffect(() => {
    void load()
  }, [])

  const openVerify = (r: UserSummary): void => {
    const cert = certById[r.id] ?? {
      riderId: r.id, username: r.username, phoneNumber: r.phoneNumber, zone: r.zone,
      isAvailable: r.isAvailable, status: 'Pending' as const, fullName: null,
      cniNumber: null, motorcyclePlate: null, blacklistReason: null, createdAt: null, reviewedAt: null,
    }
    setVerifyForm({
      fullName: cert.fullName ?? r.username,
      cniNumber: cert.cniNumber ?? '',
      motorcyclePlate: cert.motorcyclePlate ?? '',
    })
    setVerifyTarget({ rider: r, cert })
  }

  const doVerify = async (): Promise<void> => {
    if (!verifyTarget) return
    setBusy(true)
    try {
      await api.post(`/riders/${verifyTarget.rider.id}/verify`, verifyForm)
      setVerifyTarget(null)
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Certification impossible')
    } finally {
      setBusy(false)
    }
  }

  const reject = async (r: UserSummary): Promise<void> => {
    if (!window.confirm(`Révoquer la certification de « ${r.username} » ?`)) return
    const reason = window.prompt('Motif (optionnel) :') || null
    setBusy(true)
    try {
      await api.post(`/riders/${r.id}/reject`, { reason })
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Refus impossible')
    } finally {
      setBusy(false)
    }
  }

  const blacklist = async (r: UserSummary): Promise<void> => {
    const reason = window.prompt(`Exclure DÉFINITIVEMENT « ${r.username} » (vol/fraude) ? Motif obligatoire :`)
    if (!reason) return
    if (!window.confirm(`⛔ Confirmer l'exclusion de « ${r.username} » ?\nIl ne recevra plus aucune course.`)) return
    setBusy(true)
    try {
      await api.post(`/riders/${r.id}/blacklist`, { reason })
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Exclusion impossible')
    } finally {
      setBusy(false)
    }
  }

  const certOf = (r: UserSummary): RiderCertification | undefined => certById[r.id]
  const certifiedCount = Object.values(certById).filter((c) => c.status === 'Verified').length
  const pendingCount = Object.values(certById).filter((c) => c.status === 'Pending').length

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Livreurs &amp; certification</h1>
          <p>
            {riders.length} livreurs · 🛡️ {certifiedCount} certifiés · ⏳ {pendingCount} à vérifier —
            Garantie Colis Sûr
          </p>
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
                <th>Certification</th>
                <th>Identité vérifiée</th>
                <th>Dispo</th>
                <th>Dernière position</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {riders.map((r) => {
                const cert = certOf(r)
                const status = cert?.status ?? 'Pending'
                const meta = CERT_STATUS[status]
                return (
                  <tr key={r.id}>
                    <td><span className="vendor__name">{r.username}</span></td>
                    <td><span className="whatsapp">{r.phoneNumber ?? '—'}</span></td>
                    <td>{r.zone ?? '—'}</td>
                    <td><span className={`badge ${meta.badge}`}>{meta.label}</span></td>
                    <td>
                      {cert?.fullName
                        ? <span style={{ fontSize: 13 }}>
                            {cert.fullName}
                            {cert.motorcyclePlate && <><br /><span className="mono">{cert.motorcyclePlate}</span></>}
                          </span>
                        : <span style={{ fontSize: 13, color: 'var(--muted, #888)' }}>—</span>}
                    </td>
                    <td><StatusBadge status={r.isAvailable ? 'Oui' : 'Non'} /></td>
                    <td>
                      {r.latitude != null && r.longitude != null
                        ? <span className="mono">{r.latitude.toFixed(4)}, {r.longitude.toFixed(4)}</span>
                        : '—'}
                    </td>
                    <td>
                      <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                        {status !== 'Verified' && status !== 'Blacklisted' && (
                          <button
                            className="btn btn--primary"
                            style={{ padding: '6px 8px', fontSize: 12 }}
                            disabled={busy}
                            onClick={() => openVerify(r)}
                          >
                            Vérifier
                          </button>
                        )}
                        {status !== 'Blacklisted' && (
                          <button
                            className="btn btn--ghost"
                            style={{ padding: '6px 8px', fontSize: 12, borderColor: '#fca5a5', color: '#b91c1c' }}
                            disabled={busy}
                            onClick={() => void blacklist(r)}
                          >
                            Exclure
                          </button>
                        )}
                        {status === 'Verified' && (
                          <button
                            className="btn"
                            style={{ padding: '6px 8px', fontSize: 12 }}
                            disabled={busy}
                            onClick={() => void reject(r)}
                          >
                            Révoquer
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                )
              })}
              {riders.length === 0 && <tr><td colSpan={8} className="empty">Aucun livreur</td></tr>}
            </tbody>
          </table>
        </div>
      </section>


      {verifyTarget && (
        <div className="modal-backdrop" onClick={() => setVerifyTarget(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <h3>Certifier « {verifyTarget.rider.username} »</h3>
            <p style={{ fontSize: 13, marginBottom: 12 }}>
              Vérifiez l'identité (CNI) et la moto avant d'activer le badge « Livreur certifié ».
              Numéro : <span className="whatsapp">{verifyTarget.rider.phoneNumber ?? '—'}</span>
            </p>
            <div className="field">
              <label>Nom complet (CNI)</label>
              <input
                value={verifyForm.fullName}
                onChange={(e) => setVerifyForm((f) => ({ ...f, fullName: e.target.value }))}
                maxLength={80}
                required
              />
            </div>
            <div className="field">
              <label>N° CNI</label>
              <input
                value={verifyForm.cniNumber}
                onChange={(e) => setVerifyForm((f) => ({ ...f, cniNumber: e.target.value }))}
                placeholder="ex : CI-XXXXXXXXX"
                maxLength={30}
              />
            </div>
            <div className="field">
              <label>Plaque de la moto</label>
              <input
                value={verifyForm.motorcyclePlate}
                onChange={(e) => setVerifyForm((f) => ({ ...f, motorcyclePlate: e.target.value }))}
                placeholder="ex : 1234 AB 01"
                maxLength={30}
              />
            </div>
            <div className="modal__actions">
              <button className="btn" onClick={() => setVerifyTarget(null)}>Annuler</button>
              <button
                className="btn btn--primary"
                onClick={() => void doVerify()}
                disabled={busy || verifyForm.fullName.trim().length === 0}
              >
                {busy ? '…' : '✅ Certifier'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}

