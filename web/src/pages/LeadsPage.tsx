import { useCallback, useEffect, useState } from 'react'
import { api, getToken } from '../api/client'
import { ErrorAlert, formatDateTime } from '../components/ui'
import { Modal } from '../components/Modal'

type LeadStatus = 'New' | 'Contacted' | 'Converted' | 'Discarded'

// Taille de page de la liste des leads (P2 / C-09). Le serveur applique ce plafond à chaque
// réponse : le nombre de lignes rendues par le navigateur est donc BORNÉ (200), avec un sélecteur
// de statut par ligne.
//
// Choix : PAGINER plutôt que virtualiser. Virtualiser un <table> (positions absolues, hauteurs
// fixes, `aria-rowcount` à tenir à jour) se paie en accessibilité et en fragilité pour un gain
// non mesuré à 200 lignes. La perte réelle n'était pas la fluidité mais l'ACCÈS : les leads
// au-delà des 200 plus récents étaient inatteignables depuis l'écran, qui se contentait de
// prévenir qu'il en manquait.
const LEAD_PAGE_SIZE = 200

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
  // Page courante (0 = les plus récents) et total renvoyé par l'API (`X-Total-Count`).
  const [page, setPage] = useState(0)
  const [total, setTotal] = useState<number | null>(null)
  const [filterStatus, setFilterStatus] = useState('')
  const [filterSource, setFilterSource] = useState('')
  const [search, setSearch] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [convertingId, setConvertingId] = useState<string | null>(null)
  const [conversion, setConversion] = useState<ConversionResult | null>(null)

  // Filtres seuls : servent aussi à l'export CSV (qui ne pagine pas — il exporte tout).
  const buildFilterQuery = (): string => {
    const params = new URLSearchParams()
    if (filterStatus) params.set('status', filterStatus)
    if (filterSource) params.set('source', filterSource)
    const term = search.trim()
    if (term) params.set('search', term)
    return params.toString()
  }

  const buildListQuery = (): string => {
    const filters = buildFilterQuery()
    return `?${filters ? `${filters}&` : ''}limit=${LEAD_PAGE_SIZE}&offset=${page * LEAD_PAGE_SIZE}`
  }

  const load = useCallback(async (signal?: AbortSignal): Promise<void> => {
    try {
      const { items, total: count } = await api.getPage<LeadListItem[]>(`/admin/leads${buildListQuery()}`)
      if (signal?.aborted) return
      setLeads(items)
      setTotal(count)
      setError('')
    } catch (err) {
      // Une requête annulée (frappe suivante) n'est pas une erreur à afficher.
      if (signal?.aborted) return
      setError(err instanceof Error ? err.message : 'Erreur de chargement')
    }
    // `buildListQuery` est dérivé des filtres et de la page : les recréer ici à chaque rendu est
    // sans effet de bord.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filterStatus, filterSource, search, page])

  // Recherche avec léger débounce (300 ms) pour ne pas marteler l'API à chaque frappe.
  // Chaque frappe ANNULE la requête précédente : sans cela, une réponse lente pouvait écraser
  // un résultat plus récent (liste incohérente avec les filtres affichés).
  useEffect(() => {
    const controller = new AbortController()
    const t = setTimeout(() => void load(controller.signal), 300)
    return () => {
      clearTimeout(t)
      controller.abort()
    }
  }, [load])

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
      const res = await fetch(`/api/admin/leads/export${buildFilterQuery() ? `?${buildFilterQuery()}` : ''}`, {
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

  // Position affichée dans l'ensemble filtré (« 1–200 sur 1 234 »).
  const from = leads.length === 0 ? 0 : page * LEAD_PAGE_SIZE + 1
  const to = page * LEAD_PAGE_SIZE + leads.length
  // Sans l'en-tête du serveur, on ne peut pas connaître le total : on reste alors prudent et on
  // n'autorise la suite que si la page est pleine (jamais de bouton « Suivant » qui ne mène nulle part).
  const hasNext = total !== null ? to < total : leads.length === LEAD_PAGE_SIZE

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Leads d'acquisition</h1>
          <p>
            {/* Les compteurs portent sur la PAGE affichée, pas sur la totalité filtrée : sans cette
                précision, « 12 nouveaux » se lirait comme un total alors que 1 000 leads existent. */}
            {leads.length} affichés sur cette page · 🆕 {count('New')} · ☎️ {count('Contacted')} · ✅ {count('Converted')} · ⏭️ {count('Discarded')}
          </p>
        </div>
        <button className="btn btn--primary" onClick={() => void exportCsv()}>⬇️ Exporter CSV</button>
      </div>

      <div className="page-head" style={{ marginTop: -6, gap: 10, flexWrap: 'wrap' }}>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 14 }}>
          Statut :
          <select
            className="input"
            value={filterStatus}
            // Tout changement de filtre ramène à la PREMIÈRE page : rester en page 4 d'un
            // résultat plus court afficherait une page vide, que l'on prendrait pour « aucun lead ».
            onChange={(e) => { setFilterStatus(e.target.value); setPage(0) }}
            style={{ width: 150 }}
          >
            <option value="">Tous</option>
            {STATUS_OPTIONS.map((s) => <option key={s} value={s}>{STATUS_LABEL[s]}</option>)}
          </select>
        </label>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 14 }}>
          Source :
          <select
            className="input"
            value={filterSource}
            onChange={(e) => { setFilterSource(e.target.value); setPage(0) }}
            style={{ width: 190 }}
          >
            <option value="">Toutes</option>
            {sources.map((s) => <option key={s} value={s}>{SOURCE_LABEL[s] ?? s}</option>)}
          </select>
        </label>
        {/* Champ de recherche : un placeholder n'est pas un libellé — il disparaît à la
            saisie et les lecteurs d'écran ne l'annoncent pas de façon fiable (C-07). */}
        <input
          className="input"
          aria-label="Rechercher un lead (commerce, contact ou numéro)"
          placeholder="🔎 Commerce, contact ou numéro…"
          value={search}
          onChange={(e) => { setSearch(e.target.value); setPage(0) }}
          style={{ width: 260 }}
        />
        <button className="btn" onClick={() => void load()} disabled={busy}>⟳ Actualiser</button>
      </div>

      {error && <ErrorAlert message={error} onRetry={() => void load()} />}

      <section className="panel">
        {/* P2 / C-09 : l'écran n'affiche qu'une page (200 lignes au maximum, ce qui borne le
            travail du navigateur) et dit désormais où il en est — au lieu d'un simple
            avertissement de troncature qui laissait les leads plus anciens inatteignables. */}
        {(leads.length > 0 || page > 0) && (
          <div
            style={{
              display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap',
              padding: '10px 16px', fontSize: 13, color: 'var(--wz-muted)',
            }}
          >
            {/* Seule la POSITION est annoncée (role="status") : les boutons restent hors de la
                zone live, un lecteur d'écran n'ayant pas à réannoncer des commandes. */}
            <span role="status">
              Leads {from}–{to}
              {total !== null ? ` sur ${total}` : ''}
            </span>
            <button
              className="btn"
              style={{ padding: '6px 10px', fontSize: 13 }}
              disabled={page === 0 || busy}
              onClick={() => setPage((p) => Math.max(0, p - 1))}
            >
              ← Précédents
            </button>
            <button
              className="btn"
              style={{ padding: '6px 10px', fontSize: 13 }}
              disabled={!hasNext || busy}
              onClick={() => setPage((p) => p + 1)}
            >
              Suivants →
            </button>
            {!hasNext && total !== null && <span>· dernière page atteinte</span>}
          </div>
        )}
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
                      {/* Un tableau de leads aligne des sélecteurs identiques : sans le nom du
                          commerce, un lecteur d'écran annonce « liste déroulante » sans dire
                          quel lead est modifié (C-07). */}
                      <select
                        className="input"
                        style={{ width: 130, padding: '6px 8px' }}
                        aria-label={`Statut du lead ${l.businessName}`}
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
                        <span style={{ fontSize: 13, color: 'var(--wz-muted, #888)' }}>—</span>
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
        // Modale partagée (C-07) : le mot de passe temporaire ne doit pas rester affiché
        // derrière un simple <div> sans Échap ni restitution du focus.
        <Modal
          title={conversion.alreadyExisted ? 'ℹ️ Vendeur déjà existant' : '🎉 Compte vendeur créé'}
          labelledBy="lead-conversion-title"
          onClose={() => setConversion(null)}
        >
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
          <div className="modal__actions">
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
        </Modal>
      )}
    </>
  )
}
