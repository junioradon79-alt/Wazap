import { useEffect, useState } from 'react'
import { api, getToken } from '../api/client'
import { formatDateTime } from '../components/ui'

type LeadStatus = 'New' | 'Contacted' | 'Converted' | 'Discarded'
interface LeadListItem {
  id: string
  businessName: string
  contactName: string | null
  whatsappNumber: string
  zone: string
  source: string
  status: LeadStatus
  createdAt: string
}

const STATUS_OPTIONS: LeadStatus[] = ['New', 'Contacted', 'Converted', 'Discarded']
const STATUS_LABEL: Record<LeadStatus, string> = {
  New: 'Nouveau',
  Contacted: 'Contacté',
  Converted: 'Converti',
  Discarded: 'Écarté',
}

export default function LeadsPage() {
  const [leads, setLeads] = useState<LeadListItem[]>([])
  const [filterStatus, setFilterStatus] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const load = async (): Promise<void> => {
    try {
      const q = filterStatus ? `?status=${filterStatus}` : ''
      const data = await api.get<LeadListItem[]>(`/admin/leads${q}`)
      setLeads(data)
      setError('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur de chargement')
    }
  }

  useEffect(() => {
    void load()
  }, [filterStatus])

  const setStatus = async (id: string, status: LeadStatus): Promise<void> => {
    setBusy(true)
    try {
      await api.post(`/admin/leads/${id}/status`, { status })
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Mise à jour impossible')
    } finally {
      setBusy(false)
    }
  }

  const exportCsv = async (): Promise<void> => {
    const token = getToken()
    try {
      const res = await fetch(`/api/admin/leads/export${filterStatus ? `?status=${filterStatus}` : ''}`, {
        headers: token ? { Authorization: `Bearer ${token}` } : undefined,
      })
      if (!res.ok) throw new Error('Export impossible')
      const blob = await res.blob()
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = 'wazap-leads.csv'
      a.click()
      URL.revokeObjectURL(url)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Export impossible')
    }
  }

  const count = (s: LeadStatus) => leads.filter((l) => l.status === s).length

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Leads d'acquisition</h1>
          <p>
            {leads.length} affichés · 🆕 {count('New')} · ☎️ {count('Contacted')} · ✅ {count('Converted')} · ⏭️ {count('Discarded')}
          </p>
        </div>
        <button className="btn btn--primary" onClick={() => void exportCsv()}>⬇️ Exporter CSV</button>
      </div>

      <div className="page-head" style={{ marginTop: -6 }}>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 14 }}>
          Statut :
          <select className="input" value={filterStatus} onChange={(e) => setFilterStatus(e.target.value)} style={{ width: 180 }}>
            <option value="">Tous</option>
            {STATUS_OPTIONS.map((s) => <option key={s} value={s}>{STATUS_LABEL[s]}</option>)}
          </select>
        </label>
        <button className="btn" onClick={() => void load()} disabled={busy}>⟳ Actualiser</button>
      </div>

      {error && <div className="alert alert--error">{error}</div>}

      <section className="panel">
        {leads.length === 0 ? (
          <p style={{ padding: 16 }}>Aucun lead pour le moment — partagez la page de vente /app/vente.</p>
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Commerce</th>
                  <th>Contact</th>
                  <th>WhatsApp</th>
                  <th>Zone</th>
                  <th>Source</th>
                  <th>Statut</th>
                  <th>Reçu le</th>
                </tr>
              </thead>
              <tbody>
                {leads.map((l) => (
                  <tr key={l.id}>
                    <td><strong>{l.businessName}</strong></td>
                    <td>{l.contactName || '—'}</td>
                    <td>{l.whatsappNumber}</td>
                    <td>{l.zone}</td>
                    <td style={{ fontSize: 13 }}>{l.source}</td>
                    <td>
                      <select
                        className="input"
                        style={{ width: 130, padding: '6px 8px' }}
                        value={l.status}
                        disabled={busy}
                        onChange={(e) => void setStatus(l.id, e.target.value as LeadStatus)}
                      >
                        {STATUS_OPTIONS.map((s) => <option key={s} value={s}>{STATUS_LABEL[s]}</option>)}
                      </select>
                    </td>
                    <td style={{ fontSize: 13 }}>{formatDateTime(l.createdAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  )
}
