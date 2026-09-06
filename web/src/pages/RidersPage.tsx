import { useEffect, useState } from 'react'
import { api, getToken } from '../api/client'
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
  const [verifyForm, setVerifyForm] = useState({ fullName: '', idNumber: '', motorcycle: '' })
  const [scanSaved, setScanSaved] = useState(false)

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
      idNumber: null, motorcycle: null, idScanUrl: null, scanFileName: null,
      scanReceivedAt: null, blacklistReason: null, createdAt: null, reviewedAt: null,
    }
    setVerifyForm({
      fullName: cert.fullName ?? r.username,
      idNumber: cert.idNumber ?? '',
      motorcycle: cert.motorcycle ?? '',
    })
    setScanSaved(Boolean(cert.scanFileName))
    setVerifyTarget({ rider: r, cert })
  }

  const uploadScan = async (file: File): Promise<void> => {
    if (!verifyTarget) return
    setBusy(true)
    setError('')
    const token = getToken()
    const body = new FormData()
    body.append('file', file)
    try {
      const res = await fetch(`/api/riders/${verifyTarget.rider.id}/scan`, {
        method: 'POST',
        headers: token ? { Authorization: `Bearer ${token}` } : undefined,
        body,
      })
      if (!res.ok) {
        const err = await res.json().catch(() => null)
        throw new Error((err as { error?: string } | null)?.error ?? 'Téléversement impossible')
      }
      setScanSaved(true)
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Téléversement impossible')
    } finally {
      setBusy(false)
    }
  }

  const viewScan = async (riderId: string): Promise<void> => {
    const token = getToken()
    try {
      const res = await fetch(`/api/riders/${riderId}/scan`, {
        headers: token ? { Authorization: `Bearer ${token}` } : undefined,
      })
      if (!res.ok) throw new Error('Scan introuvable')
      const blob = await res.blob()
      const url = URL.createObjectURL(blob)
      window.open(url, '_blank')
      setTimeout(() => URL.revokeObjectURL(url), 30_000)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Scan introuvable')
    }
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
                            {cert.idNumber && <><br /><span className="mono">{cert.idNumber}</span></>}
                            {(cert.scanFileName)
                              ? <><br /><button className="btn" style={{ padding: '2px 8px', fontSize: 11 }} onClick={() => void viewScan(r.id)}>🖼 Voir le scan</button></>
                              : null}
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
              Le scan de la pièce d'identité et le nom complet sont obligatoires avant de certifier.
              La moto des particuliers n'est souvent pas immatriculée : la plaque est optionnelle.
              Numéro : <span className="whatsapp">{verifyTarget.rider.phoneNumber ?? '—'}</span>
            </p>
            <div className="field">
              <label>Nom complet (sur la pièce d'identité)</label>
              <input
                value={verifyForm.fullName}
                onChange={(e) => setVerifyForm((f) => ({ ...f, fullName: e.target.value }))}
                maxLength={80}
                required
              />
            </div>
            <div className="field">
              <label>N° de la pièce (CNI, passeport…)</label>
              <input
                value={verifyForm.idNumber}
                onChange={(e) => setVerifyForm((f) => ({ ...f, idNumber: e.target.value }))}
                placeholder="ex : CI-XXXXXXXXX"
                maxLength={40}
              />
            </div>
            <div className="field">
              <label>Moto (type / couleur — plaque seulement si disponible)</label>
              <input
                value={verifyForm.motorcycle}
                onChange={(e) => setVerifyForm((f) => ({ ...f, motorcycle: e.target.value }))}
                placeholder="ex : moto rouge"
                maxLength={80}
              />
            </div>
            <div className="field">
              <label>🪪 Scan de la pièce d'identité (obligatoire)</label>
              {verifyTarget.cert.scanFileName || scanSaved ? (
                <p style={{ fontSize: 13, color: 'var(--wz-green-strong, #059669)' }}>
                  ✅ Scan reçu —{' '}
                  <button className="btn" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => void viewScan(verifyTarget.rider.id)}>
                    Voir le scan
                  </button>
                </p>
              ) : (
                <p style={{ fontSize: 13, color: 'var(--muted, #888)' }}>Aucun scan — téléversez la photo de la pièce reçue sur WhatsApp (JPG/PNG/WEBP/PDF).</p>
              )}
              <input
                type="file"
                accept="image/jpeg,image/png,image/webp,application/pdf"
                disabled={busy}
                onChange={(e) => {
                  const file = e.target.files?.[0]
                  if (file) void uploadScan(file)
                  e.currentTarget.value = ''
                }}
              />
            </div>
            <div className="modal__actions">
              <button className="btn" onClick={() => setVerifyTarget(null)}>Annuler</button>
              <button
                className="btn btn--primary"
                onClick={() => void doVerify()}
                disabled={busy || verifyForm.fullName.trim().length === 0 || !(verifyTarget.cert.scanFileName || scanSaved)}
                title={!(verifyTarget.cert.scanFileName || scanSaved) ? 'Téléversez d’abord le scan de la pièce d’identité' : undefined}
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

