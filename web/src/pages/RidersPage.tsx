import { useEffect, useState } from 'react'
import { api, getToken } from '../api/client'
import type { RiderCertification, UserSummary } from '../api/types'
import { ErrorAlert, StatusBadge } from '../components/ui'
import { Modal } from '../components/Modal'

const CERT_STATUS: Record<RiderCertification['status'], { label: string; badge: string }> = {
  Pending: { label: 'À vérifier', badge: 'badge--orange' },
  Verified: { label: '✔ Certifié', badge: 'badge--green' },
  Rejected: { label: 'Refusé', badge: 'badge--red' },
  Blacklisted: { label: '⛔ Exclu', badge: 'badge--red' },
}

function formatPhoneForWaMe(phone?: string | null): string {
  if (!phone) return ''
  let clean = phone.replace(/\D/g, '')
  if (clean.length === 10 && (clean.startsWith('01') || clean.startsWith('05') || clean.startsWith('07'))) {
    clean = '225' + clean
  }
  return clean
}

function buildWhatsAppAccessLink(rider: UserSummary, cert?: RiderCertification): string {
  const cleanPhone = formatPhoneForWaMe(rider.phoneNumber)
  const name = cert?.fullName || rider.username || 'champion'
  const dashboardWebUrl = `https://junioradon79gm-001-site1.jtempurl.com/app/login?u=${encodeURIComponent(rider.username)}`
  const msg = `Salut ${name} ! Bienvenue sur WAZAP 🛵💨\n\n` +
    `📊 Touche ce lien pour ouvrir ton COCKPIT LIVREUR (courses, gains nets, challenge smartphone) :\n` +
    `${dashboardWebUrl}\n\n` +
    `👉 Touche ce lien pour te mettre EN LIGNE (DISPO) :\n` +
    `https://wa.me/2250544051972?text=DISPO\n\n` +
    `⚪ Touche ce lien pour te mettre EN PAUSE (INDISPO) :\n` +
    `https://wa.me/2250544051972?text=INDISPO`
  return `https://wa.me/${cleanPhone}?text=${encodeURIComponent(msg)}`
}

export default function RidersPage() {
  const [riders, setRiders] = useState<UserSummary[]>([])
  const [certById, setCertById] = useState<Record<string, RiderCertification>>({})
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [verifyTarget, setVerifyTarget] = useState<{ rider: UserSummary | null; cert: RiderCertification | null } | null>(null)
  const [verifyForm, setVerifyForm] = useState({ fullName: '', idNumber: '', motorcycle: '' })
  const [newRiderPhone, setNewRiderPhone] = useState('')
  const [newRiderZone, setNewRiderZone] = useState('Cocody')
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [scanSaved, setScanSaved] = useState(false)
  const [ocrBusy, setOcrBusy] = useState(false)
  const [ocrSuccess, setOcrSuccess] = useState('')
  const [syncNotice, setSyncNotice] = useState('')
  const [selectedFilePreview, setSelectedFilePreview] = useState<string | null>(null)

  // Enrôlement manuel rapide
  const [showEnrollModal, setShowEnrollModal] = useState(false)
  const [enrollForm, setEnrollForm] = useState({ fullName: '', phoneNumber: '', zone: 'Cocody' })
  const [enrollBusy, setEnrollBusy] = useState(false)

  const load = async (): Promise<void> => {
    try {
      const [list, certs] = await Promise.all([
        api.get<UserSummary[]>('/riders'),
        api.get<RiderCertification[]>('/riders/certifications'),
      ])
      setRiders(list)
      setCertById(Object.fromEntries(certs.map((c) => [c.riderId, c])))
      setError('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur')
    }
  }

  useEffect(() => {
    void load()
  }, [])

  const openVerify = (r: UserSummary | null): void => {
    if (r) {
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
      setNewRiderPhone(r.phoneNumber ?? '')
      setNewRiderZone(r.zone ?? 'Cocody')
      setScanSaved(Boolean(cert.scanFileName))
      setVerifyTarget({ rider: r, cert })
    } else {
      setVerifyForm({ fullName: '', idNumber: '', motorcycle: '' })
      setNewRiderPhone('')
      setNewRiderZone('Cocody')
      setScanSaved(false)
      setVerifyTarget({ rider: null, cert: null })
    }
    setOcrSuccess('')
    setSelectedFile(null)
    setSelectedFilePreview(null)
  }

  const uploadScan = async (file: File, riderId: string): Promise<void> => {
    setBusy(true)
    setError('')
    const token = getToken()
    const body = new FormData()
    body.append('file', file)
    try {
      const res = await fetch(`/api/riders/${riderId}/scan`, {
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

  const analyzeOcr = async (file: File): Promise<void> => {
    setSelectedFile(file)
    setSelectedFilePreview(URL.createObjectURL(file))
    setOcrBusy(true)
    setError('')
    setOcrSuccess('')
    const token = getToken()
    const body = new FormData()
    body.append('file', file)

    try {
      // 1. Analyse OCR via Google Vision
      const res = await fetch('/api/riders/ocr-scan', {
        method: 'POST',
        headers: token ? { Authorization: `Bearer ${token}` } : undefined,
        body,
      })

      if (res.ok) {
        const data = await res.json() as { success: boolean; fullName?: string; idNumber?: string; documentType?: string; rawText?: string; error?: string }
        if (data.success && (data.fullName || data.idNumber)) {
          setVerifyForm((f) => ({
            ...f,
            fullName: data.fullName || f.fullName,
            idNumber: data.idNumber || f.idNumber,
          }))
          const docLabel = data.documentType || 'Pièce d’identité'
          setOcrSuccess(`✨ ${docLabel} reconnue avec succès ! Nom : ${data.fullName ?? 'Reconnu'} · N° : ${data.idNumber ?? 'Reconnu'}`)
        } else if (data.error) {
          setError(`Notice OCR : ${data.error}`)
        }
      }

      // 2. Si un livreur existant est ciblé, sauvegarde immédiate du scan
      if (verifyTarget?.rider) {
        await uploadScan(file, verifyTarget.rider.id)
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur pendant l’analyse OCR')
    } finally {
      setOcrBusy(false)
    }
  }

  const syncLeads = async (): Promise<void> => {
    setBusy(true)
    setError('')
    setSyncNotice('')
    try {
      const res = await api.post<{ message: string; total: number }>('/riders/sync-leads', {})
      setSyncNotice(`✅ ${res.message}`)
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur lors de la synchronisation des leads')
    } finally {
      setBusy(false)
    }
  }

  const doEnroll = async (): Promise<void> => {
    if (!enrollForm.phoneNumber.trim()) {
      setError('Le numéro de téléphone est obligatoire.')
      return
    }
    setEnrollBusy(true)
    setError('')
    try {
      await api.post('/riders/enroll', enrollForm)
      setShowEnrollModal(false)
      setEnrollForm({ fullName: '', phoneNumber: '', zone: 'Cocody' })
      setSyncNotice('✅ Livreur enrôlé avec succès !')
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur lors de l’enrôlement')
    } finally {
      setEnrollBusy(false)
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
    setError('')
    try {
      if (verifyTarget.rider) {
        await api.post(`/riders/${verifyTarget.rider.id}/verify`, verifyForm)
        setSyncNotice(`✅ Livreur « ${verifyForm.fullName || verifyTarget.rider.username} » certifié avec succès !`)
      } else {
        if (!newRiderPhone.trim()) {
          setError('Le numéro WhatsApp du livreur est obligatoire pour finaliser l’enrôlement.')
          setBusy(false)
          return
        }
        const token = getToken()
        const fd = new FormData()
        if (selectedFile) fd.append('file', selectedFile)
        fd.append('phoneNumber', newRiderPhone)
        fd.append('fullName', verifyForm.fullName)
        fd.append('idNumber', verifyForm.idNumber)
        fd.append('zone', newRiderZone)
        fd.append('motorcycle', verifyForm.motorcycle)

        const res = await fetch('/api/riders/enroll-and-verify', {
          method: 'POST',
          headers: token ? { Authorization: `Bearer ${token}` } : undefined,
          body: fd,
        })
        if (!res.ok) {
          const err = await res.json().catch(() => null)
          throw new Error((err as { error?: string } | null)?.error ?? 'Échec de l’enrôlement certifié')
        }
        const data = await res.json() as { message: string }
        setSyncNotice(`✅ ${data.message}`)
      }
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

  const deleteRider = async (r: UserSummary): Promise<void> => {
    if (!window.confirm(`Supprimer définitivement le compte livreur « ${r.username} » (${r.phoneNumber ?? 'sans numéro'}) ?`)) return
    setBusy(true)
    setError('')
    setSyncNotice('')
    try {
      await api.del(`/riders/${r.id}`)
      setSyncNotice(`Livreur « ${r.username} » supprimé avec succès.`)
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Suppression impossible')
    } finally {
      setBusy(false)
    }
  }

  const purgeDemo = async (): Promise<void> => {
    if (!window.confirm('Supprimer tous les comptes de test (Karim Diallo, Lucas Martin, Sofiane Benali, Yann Le Goff) ?')) return
    setBusy(true)
    setError('')
    setSyncNotice('')
    try {
      const res = await api.post<{ message: string; purgedCount: number }>('/riders/purge-demo', {})
      setSyncNotice(`🧹 ${res.message}`)
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur lors de la suppression des comptes tests')
    } finally {
      setBusy(false)
    }
  }

  const certOf = (r: UserSummary): RiderCertification | undefined => certById[r.id]
  const certifiedCount = Object.values(certById).filter((c) => c.status === 'Verified').length
  const pendingCount = Object.values(certById).filter((c) => c.status === 'Pending').length

  return (
    <>
      <div className="page-head" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1>Livreurs &amp; certification</h1>
          <p>
            {riders.length} livreurs · 🛡️ {certifiedCount} certifiés · ⏳ {pendingCount} à vérifier —
            Garantie Colis Sûr
          </p>
        </div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
          <button
            type="button"
            className="btn btn--primary"
            style={{ background: '#059669', borderColor: '#059669', fontWeight: 600, display: 'inline-flex', alignItems: 'center', gap: 6 }}
            onClick={() => {
              const pendingRider = riders.find((r) => certOf(r)?.status !== 'Verified')
              openVerify(pendingRider ?? (riders.length > 0 ? riders[0] : null))
            }}
            disabled={busy}
            title="Scanner une pièce d'identité (CNI, Permis de Conduire ou Passeport) par OCR pour certifier un livreur"
          >
            ⚡ Analyser CNI / Permis par OCR
          </button>
          <button
            type="button"
            className="btn btn--ghost"
            style={{ borderColor: '#fed7aa', color: '#c2410c' }}
            onClick={() => void purgeDemo()}
            disabled={busy}
            title="Supprime tous les profils de démonstration (Karim Diallo, Lucas Martin...)"
          >
            🧹 Purger comptes tests
          </button>
          <button
            type="button"
            className="btn btn--ghost"
            onClick={() => void syncLeads()}
            disabled={busy}
            title="Convertit automatiquement tous les leads livreurs WhatsApp en comptes actifs"
          >
            🔄 Synchroniser Leads WhatsApp
          </button>
          <button
            type="button"
            className="btn btn--primary"
            onClick={() => setShowEnrollModal(true)}
            disabled={busy}
          >
            + Enrôler un Livreur
          </button>
        </div>
      </div>

      {syncNotice && (
        <div style={{ background: '#ecfdf5', color: '#065f46', border: '1px solid #a7f3d0', padding: '10px 14px', borderRadius: 6, marginBottom: 16 }}>
          {syncNotice}
        </div>
      )}

      {error && <ErrorAlert message={error} onRetry={() => void load()} />}

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
                        : <span style={{ fontSize: 13, color: 'var(--wz-muted, #888)' }}>—</span>}
                    </td>
                    <td><StatusBadge status={r.isAvailable ? 'Oui' : 'Non'} /></td>
                    <td>
                      {r.latitude != null && r.longitude != null
                        ? <span className="mono">{r.latitude.toFixed(4)}, {r.longitude.toFixed(4)}</span>
                        : '—'}
                    </td>
                    <td>
                      <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
                        {r.phoneNumber && (
                          <a
                            className="btn btn--ghost"
                            style={{ padding: '6px 8px', fontSize: 12, borderColor: '#25D366', color: '#128C7E', display: 'inline-flex', alignItems: 'center', gap: 4, textDecoration: 'none', fontWeight: 600 }}
                            href={buildWhatsAppAccessLink(r, cert)}
                            target="_blank"
                            rel="noopener noreferrer"
                            title="Ouvrir WhatsApp et envoyer les liens 1-clic (Dashboard, DISPO, INDISPO)"
                          >
                            💬 Accès WA
                          </a>
                        )}
                        {status !== 'Verified' && status !== 'Blacklisted' && (
                          <button
                            className="btn btn--primary"
                            style={{ padding: '6px 8px', fontSize: 12, background: '#059669', borderColor: '#059669', fontWeight: 600 }}
                            disabled={busy}
                            onClick={() => openVerify(r)}
                            title="Ouvrir l'analyse OCR et certifier ce livreur"
                          >
                            ⚡ Vérifier / OCR
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
                        <button
                          className="btn btn--ghost"
                          style={{ padding: '6px 8px', fontSize: 12, borderColor: '#e2e8f0', color: '#64748b' }}
                          disabled={busy}
                          onClick={() => void deleteRider(r)}
                          title="Supprimer définitivement ce livreur"
                        >
                          🗑️
                        </button>
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
        <Modal
          title={verifyTarget.rider ? `⚡ Analyse OCR & Certification « ${verifyTarget.rider.username} »` : '⚡ Analyseur Magique OCR — Certifier un Livreur'}
          labelledBy="rider-verify-title"
          onClose={() => setVerifyTarget(null)}
        >
            {verifyTarget.rider ? (
              <div style={{ marginBottom: 12, background: '#f1f5f9', padding: '10px 12px', borderRadius: 6, display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 10 }}>
                <div>
                  <span style={{ fontSize: 12, color: '#64748b' }}>Livreur sélectionné :</span>
                  <div style={{ fontWeight: 600, fontSize: 14 }}>{verifyTarget.rider.username} <span className="whatsapp" style={{ fontSize: 13 }}>({verifyTarget.rider.phoneNumber ?? '—'})</span></div>
                </div>
                {riders.length > 1 && (
                  <select
                    value={verifyTarget.rider.id}
                    onChange={(e) => {
                      const sel = riders.find((r) => r.id === e.target.value)
                      if (sel) openVerify(sel)
                    }}
                    style={{ padding: '4px 8px', fontSize: 12, borderRadius: 6, border: '1px solid #cbd5e1' }}
                    title="Changer de livreur à vérifier"
                  >
                    {riders.map((r) => (
                      <option key={r.id} value={r.id}>
                        {r.username} ({r.zone ?? 'Zone'}) {certOf(r)?.status === 'Verified' ? '✔' : '⏳'}
                      </option>
                    ))}
                  </select>
                )}
              </div>
            ) : (
              <div style={{ background: '#f8fafc', padding: 12, borderRadius: 8, marginBottom: 14, border: '1px solid #e2e8f0' }}>
                <div style={{ fontWeight: 600, fontSize: 13, marginBottom: 8, color: '#0f172a' }}>
                  📱 Coordonnées du livreur reçu sur WhatsApp :
                </div>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10 }}>
                  <div className="field" style={{ margin: 0 }}>
                    <label style={{ fontSize: 12 }}>Numéro WhatsApp</label>
                    <input
                      value={newRiderPhone}
                      onChange={(e) => setNewRiderPhone(e.target.value)}
                      placeholder="ex : +225 05 44 05 19 72"
                      required
                    />
                  </div>
                  <div className="field" style={{ margin: 0 }}>
                    <label style={{ fontSize: 12 }}>Commune principale</label>
                    <select
                      value={newRiderZone}
                      onChange={(e) => setNewRiderZone(e.target.value)}
                      style={{ width: '100%', padding: '8px 10px', borderRadius: 6, border: '1px solid #cbd5e1' }}
                    >
                      <option value="Cocody">Cocody</option>
                      <option value="Yopougon">Yopougon</option>
                      <option value="Marcory">Marcory</option>
                      <option value="Koumassi">Koumassi / Treichville</option>
                      <option value="Abobo">Abobo</option>
                      <option value="Plateau">Plateau / Adjamé</option>
                      <option value="Port-Bouët">Port-Bouët</option>
                    </select>
                  </div>
                </div>
              </div>
            )}

            <p style={{ fontSize: 13, marginBottom: 12 }}>
              Téléversez ou glissez-déposez la photo de la pièce (CNI, Permis de Conduire ou Passeport) reçue sur WhatsApp. L'IA extrait automatiquement le nom et le numéro !
            </p>

            <div className="field" style={{ background: '#f8fafc', border: '1px solid #e2e8f0', padding: 14, borderRadius: 8, marginBottom: 14 }}>
              <label style={{ fontWeight: 600, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <span>🪪 Pièce d'identité (CNI, Permis de Conduire ou Passeport)</span>
                {ocrBusy && <span style={{ color: '#059669', fontSize: 12 }}>⚡ Analyse OCR en cours...</span>}
              </label>

              {verifyTarget.cert?.scanFileName || scanSaved ? (
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', margin: '6px 0 10px 0', fontSize: 13, color: '#059669' }}>
                  <span>✅ Scan actif enregistré</span>
                  {verifyTarget.rider && (
                    <button type="button" className="btn" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => void viewScan(verifyTarget.rider!.id)}>
                      👁️ Voir le scan
                    </button>
                  )}
                </div>
              ) : (
                <p style={{ fontSize: 12, color: '#64748b', margin: '4px 0 8px 0' }}>
                  Téléversez la photo de la CNI, du Permis de Conduire ou du Passeport pour l'analyser et la chiffrer.
                </p>
              )}

              {selectedFilePreview && (
                <div style={{ marginBottom: 10, textAlign: 'center' }}>
                  <img
                    src={selectedFilePreview}
                    alt="Aperçu Pièce"
                    style={{ maxHeight: 140, maxWidth: '100%', borderRadius: 6, border: '1px solid #cbd5e1', objectFit: 'contain' }}
                  />
                </div>
              )}

              {ocrSuccess && (
                <div style={{ background: '#ecfdf5', color: '#065f46', padding: '8px 12px', borderRadius: 6, fontSize: 12, marginBottom: 10, border: '1px solid #a7f3d0' }}>
                  {ocrSuccess}
                </div>
              )}

              <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
                <label className="btn btn--primary" style={{ cursor: 'pointer', margin: 0, padding: '8px 14px', fontSize: 13, background: 'linear-gradient(135deg, #059669 0%, #047857 100%)' }}>
                  ⚡ {ocrBusy ? 'Analyse OCR...' : 'Sélectionner CNI / Permis par OCR'}
                  <input
                    type="file"
                    accept="image/jpeg,image/png,image/webp,application/pdf"
                    disabled={busy || ocrBusy}
                    style={{ display: 'none' }}
                    onChange={(e) => {
                      const file = e.target.files?.[0]
                      if (file) void analyzeOcr(file)
                      e.currentTarget.value = ''
                    }}
                  />
                </label>
                <span style={{ fontSize: 12, color: '#64748b' }}>
                  Glissez-déposez la photo reçue sur WhatsApp (Google Vision lit le nom et N°)
                </span>
              </div>
            </div>

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
              <label>N° de la pièce (CNI, Permis de conduire, Passeport…)</label>
              <input
                value={verifyForm.idNumber}
                onChange={(e) => setVerifyForm((f) => ({ ...f, idNumber: e.target.value }))}
                placeholder="ex : CI-XXXXXXXXX, C0123456789 ou N° Permis"
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

            <div className="modal__actions" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', width: '100%' }}>
              <div>
                {verifyTarget.rider?.phoneNumber && (
                  <a
                    className="btn btn--ghost"
                    style={{ borderColor: '#25D366', color: '#128C7E', textDecoration: 'none', display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 13, fontWeight: 600 }}
                    href={buildWhatsAppAccessLink(verifyTarget.rider, verifyTarget.cert ?? undefined)}
                    target="_blank"
                    rel="noopener noreferrer"
                    title="Envoyer les accès et le lien Dashboard directement sur WhatsApp"
                  >
                    💬 Ouvrir WhatsApp & Envoyer Liens 1-Clic
                  </a>
                )}
              </div>
              <div style={{ display: 'flex', gap: 8 }}>
                <button className="btn" onClick={() => setVerifyTarget(null)}>Annuler</button>
                <button
                  className="btn btn--primary"
                  onClick={() => void doVerify()}
                  disabled={busy || ocrBusy || verifyForm.fullName.trim().length === 0 || (!selectedFile && !scanSaved && !verifyTarget.cert?.scanFileName)}
                  title={(!selectedFile && !scanSaved && !verifyTarget.cert?.scanFileName) ? 'Téléversez d’abord la photo de la pièce' : undefined}
                >
                  {busy ? '…' : verifyTarget.rider ? '✅ Certifier' : '🚀 Créer & Certifier ce Livreur'}
                </button>
              </div>
            </div>
        </Modal>
      )}

      {showEnrollModal && (
        <Modal
          title="Enrôler un nouveau livreur"
          labelledBy="rider-enroll-title"
          onClose={() => setShowEnrollModal(false)}
        >
          <p style={{ fontSize: 13, marginBottom: 14 }}>
            Crée instantanément un compte Livreur actif sur le réseau WAZAP à partir de son numéro WhatsApp.
          </p>
          <div className="field">
            <label>Nom complet ou Prénom</label>
            <input
              value={enrollForm.fullName}
              onChange={(e) => setEnrollForm((f) => ({ ...f, fullName: e.target.value }))}
              placeholder="ex : Moussa Traoré"
              maxLength={60}
            />
          </div>
          <div className="field">
            <label>Numéro WhatsApp (avec indicatif +225)</label>
            <input
              value={enrollForm.phoneNumber}
              onChange={(e) => setEnrollForm((f) => ({ ...f, phoneNumber: e.target.value }))}
              placeholder="ex : +2250544051972"
              required
            />
          </div>
          <div className="field">
            <label>Commune principale</label>
            <select
              value={enrollForm.zone}
              onChange={(e) => setEnrollForm((f) => ({ ...f, zone: e.target.value }))}
              style={{ width: '100%', padding: '8px 10px', borderRadius: 6, border: '1px solid #cbd5e1' }}
            >
              <option value="Cocody">Cocody (Angré, 2 Plateaux, Riviera)</option>
              <option value="Yopougon">Yopougon (Maroc, Siporex, Bel Air)</option>
              <option value="Marcory">Marcory (Zone 4, Biétry)</option>
              <option value="Koumassi">Koumassi / Treichville</option>
              <option value="Plateau">Plateau / Adjamé</option>
              <option value="Abobo">Abobo</option>
              <option value="Port-Bouët">Port-Bouët / Vridi</option>
              <option value="Bingerville">Bingerville</option>
            </select>
          </div>
          <div className="modal__actions">
            <button className="btn" onClick={() => setShowEnrollModal(false)}>Annuler</button>
            <button
              className="btn btn--primary"
              onClick={() => void doEnroll()}
              disabled={enrollBusy || !enrollForm.phoneNumber.trim()}
            >
              {enrollBusy ? 'Enrôlement...' : '✅ Activer le Livreur'}
            </button>
          </div>
        </Modal>
      )}
    </>
  )
}

