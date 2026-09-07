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
  referralCode: string | null
  status: LeadStatus
  createdAt: string
}

interface ConversionResult {
  vendorId: string
  username: string
  temporaryPassword: string | null
  credits: number
  referralCode: string
  zone: string | null
  alreadyExisted: boolean
}

const STATUS_OPTIONS: LeadStatus[] = ['New', 'Contacted', 'Converted', 'Discarded']
const STATUS_LABEL: Record<LeadStatus, string> = {
  New: 'Nouveau',
  Contacted: 'Contacté',
  Converted: 'Converti',
  Discarded: 'Écarté',
}

// Canaux connus (le sélecteur est complété par les sources réellement présentes).
const KNOWN_SOURCES = ['page-vente', 'whatsapp-prospect', 'whatsapp-livreur', 'whatsapp-parrainage']
const SOURCE_LABEL: Record<string, string> = {
  'page-vente': 'Page de vente',
  'whatsapp-prospect': 'WhatsApp prospect',
  'whatsapp-livreur': 'WhatsApp livreur',
  'whatsapp-parrainage': 'WhatsApp parrainage',
}

export default function LeadsPage() {
  const [leads, setLeads] = useState<LeadListItem[]>([])
  const [filterStatus, setFilterStatus] = useState('')
  const [filterSource, setFilterSource] = useState('')
  const [search, setSearch] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [convertingId, setConvertingId] = useState<string | null>(null)
  const [conversion, setConversion] = useState<ConversionResult | null>(null)

  const buildQuery = (): string => {
    const params = new URLSearchParams()
    if (filterStatus) params.set('status', filterStatus)
    if (filterSource) params.set('source', filterSource)
    const term = search.trim()
    if (term) params.set('search', term)
    const qs = params.toString()
    return qs ? `?${qs}` : ''
  }

  const load = async (): Promise<void> => {
    try {
      const data = await api.get<LeadListItem[]>(`/admin/leads${buildQuery()}`)
      setLeads(data)
      setError('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur de chargement')
    }
  }

  // Recherche avec léger débounce (300 ms) pour ne pas marteler l'API à chaque frappe.
  useEffect(() => {
    const t = setTimeout(() => void load(), 300)
    return () => clearTimeout(t)
  }, [filterStatus, filterSource, search])

  // Les sources connues + celles réellement présentes dans les résultats courants.
  const sources = Array.from(new Set([...KNOWN_SOURCES, ...leads.map((l) => l.source)])).sort()

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

  const convertLead = async (l: LeadListItem): Promise<void> => {
    if (!window.confirm(`Créer le compte vendeur pour « ${l.businessName} » (${l.whatsappNumber}) ?\nDes crédits d'offre découverte seront octroyés et un message de bienvenue WhatsApp sera envoyé.`)) return
    setConvertingId(l.id)
    setError('')
    try {
      const data = await api.post<ConversionResult>(`/admin/leads/${l.id}/convert`, { sendWelcome: true })
      setConversion(data)
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Conversion impossible')
    } finally {
      setConvertingId(null)
    }
  }

  const exportCsv = async (): Promise<void> => {
    const token = getToken()
    try {
      const res = await fetch(`/api/admin/leads/export${buildQuery()}`, {
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

      <div className="page-head" style={{ marginTop: -6, gap: 10, flexWrap: 'wrap' }}>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 14 }}>
          Statut :
          <select className="input" value={filterStatus} onChange={(e) => setFilterStatus(e.target.value)} style={{ width: 150 }}>
            <option value="">Tous</option>
            {STATUS_OPTIONS.map((s) => <option key={s} value={s}>{STATUS_LABEL[s]}</option>)}
          </select>
        </label>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 14 }}>
          Source :
          <select className="input" value={filterSource} onChange={(e) => setFilterSource(e.target.value)} style={{ width: 190 }}>
            <option value="">Toutes</option>
            {sources.map((s) => <option key={s} value={s}>{SOURCE_LABEL[s] ?? s}</option>)}
          </select>
        </label>
        <input
          className="input"
          placeholder="🔎 Commerce, contact ou numéro…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          style={{ width: 260 }}
        />
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
                  <th>Code parrainage</th>
                  <th>Statut</th>
                  <th>Reçu le</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {leads.map((l) => (
                  <tr key={l.id}>
                    <td><strong>{l.businessName}</strong></td>
                    <td>{l.contactName || '—'}</td>
                    <td>{l.whatsappNumber}</td>
                    <td>{l.zone}</td>
                    <td style={{ fontSize: 13 }} title={l.source}>{SOURCE_LABEL[l.source] ?? l.source}</td>
                    <td style={{ fontSize: 13 }}>{l.referralCode ? <code>{l.referralCode}</code> : '—'}</td>
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
                    <td>
                      {l.status === 'Converted' ? (
                        <span style={{ fontSize: 13 }}>✅ Compte créé</span>
                      ) : (l.status === 'New' || l.status === 'Contacted') && l.source !== 'whatsapp-livreur' ? (
                        <button
                          className="btn btn--primary"
                          style={{ padding: '6px 10px', fontSize: 13 }}
                          disabled={busy || convertingId !== null}
                          onClick={() => void convertLead(l)}
                        >
                          {convertingId === l.id ? '…' : '🛍️ Créer le compte'}
                        </button>
                      ) : (
                        <span style={{ fontSize: 13, color: 'var(--muted, #888)' }}>—</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {conversion && (
        <div className="modal-backdrop" onClick={() => setConversion(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <h3>{conversion.alreadyExisted ? 'ℹ️ Vendeur déjà existant' : '🎉 Compte vendeur créé'}</h3>
            <p style={{ marginBottom: 12 }}>
              {conversion.alreadyExisted
                ? 'Un compte vendeur existait déjà pour ce numéro — le lead a été marqué Converti.'
                : 'Le lead est marqué Converti. Un message de bienvenue WhatsApp a été envoyé au vendeur.'}
            </p>
            <table className="table" style={{ marginBottom: 16 }}>
              <tbody>
                <tr><th style={{ width: 160 }}>Identifiant</th><td><code>{conversion.username}</code></td></tr>
                {conversion.temporaryPassword && (
                  <tr><th>Mot de passe temporaire</th><td><code>{conversion.temporaryPassword}</code></td></tr>
                )}
                <tr><th>Crédits</th><td>{conversion.credits}</td></tr>
                <tr><th>Code parrainage</th><td><code>{conversion.referralCode}</code></td></tr>
                {conversion.zone && <tr><th>Zone</th><td>{conversion.zone}</td></tr>}
              </tbody>
            </table>
            <p style={{ fontSize: 13, marginBottom: 12 }}>
              💡 Communiquez le mot de passe temporaire au vendeur uniquement s'il demande un accès au tableau de bord.
            </p>
            <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
              {conversion.temporaryPassword && (
                <button
                  className="btn"
                  onClick={() => void navigator.clipboard.writeText(`Identifiant : ${conversion.username}\nMot de passe : ${conversion.temporaryPassword}`)}
                >
                  📋 Copier identifiants
                </button>
              )}
              <button className="btn btn--primary" onClick={() => setConversion(null)}>OK</button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
