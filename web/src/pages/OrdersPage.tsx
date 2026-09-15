import { useEffect, useState, type FormEvent } from 'react'
import { api, getToken, getUser } from '../api/client'
import type { CreateOrderRequest, OrderDto, PagedResult } from '../api/types'
import { Modal } from '../components/Modal'
import { StatusBadge, formatDateTime, formatMoney, shortId } from '../components/ui'

export default function OrdersPage() {
  const [orders, setOrders] = useState<OrderDto[]>([])
  const [total, setTotal] = useState(0)
  const [error, setError] = useState('')
  const [showCreate, setShowCreate] = useState(false)
  const [busy, setBusy] = useState(false)
  const [broadcasting, setBroadcasting] = useState<string | null>(null)
  const [paying, setPaying] = useState<string | null>(null)
  const [payMessage, setPayMessage] = useState('')

  // Pagination : la liste était figée sur les 50 premières commandes alors que le TOTAL était
  // affiché — au-delà, des lignes existaient sans aucun moyen de les atteindre.
  const [page, setPage] = useState(1)
  const [totalPages, setTotalPages] = useState(1)
  const pageSize = 50

  const [form, setForm] = useState<CreateOrderRequest>({
    clientName: '',
    clientWhatsAppNumber: '',
    vendorWhatsAppNumber: '',
    description: '',
    amount: 0,
  })

  const load = async (targetPage = page): Promise<void> => {
    try {
      const data = await api.get<PagedResult<OrderDto>>(`/orders?page=${targetPage}&pageSize=${pageSize}`)
      setOrders(data.items)
      setTotal(data.total)
      setTotalPages(Math.max(1, data.totalPages))
      setPage(targetPage)
      setError('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur')
    }
  }

  useEffect(() => {
    void load(1)
    // Chargement initial uniquement : les changements de page passent par le bouton.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const create = async (e: FormEvent): Promise<void> => {
    e.preventDefault()
    setBusy(true)
    setError('')
    try {
      await api.post('/orders', form)
      setShowCreate(false)
      setForm({ clientName: '', clientWhatsAppNumber: '', vendorWhatsAppNumber: '', description: '', amount: 0 })
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Création impossible.')
    } finally {
      setBusy(false)
    }
  }

  const broadcast = async (id: string): Promise<void> => {
    // Effet EXTERNE immédiat : des notifications partent vers les livreurs du secteur.
    if (!window.confirm('Relancer la diffusion de cette commande aux livreurs proches ?'))
      return
    setBroadcasting(id)
    setError('')
    try {
      await api.post(`/orders/${id}/broadcast`)
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Diffusion impossible.')
    } finally {
      setBroadcasting(null)
    }
  }

  interface PayResult {
    success: boolean
    paymentLink: string | null
    errorMessage: string | null
  }

  const requestPayment = async (id: string): Promise<void> => {
    // Le client reçoit un message WhatsApp : on confirme avant d'engager cet effet externe.
    if (!window.confirm('Envoyer au client le lien de paiement Mobile Money sur WhatsApp ?'))
      return
    setPaying(id)
    setError('')
    setPayMessage('')
    try {
      const res = await api.post<PayResult>(`/vendors/orders/${id}/pay`)
      setPayMessage(
        res.success
          ? '✅ Lien de paiement envoyé au client sur WhatsApp.'
          : `ℹ️ ${res.errorMessage ?? 'Paiement en ligne indisponible.'}`,
      )
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Demande du lien impossible.')
    } finally {
      setPaying(null)
    }
  }

  // Photo de preuve de livraison (réservée admin, litiges « Garantie Colis Sûr ») :
  // fetch direct avec le JWT (l'endpoint renvoie l'image, pas du JSON).
  const viewProof = async (id: string): Promise<void> => {
    try {
      const token = getToken()
      const res = await fetch(`/api/orders/${id}/proof-photo`, {
        headers: token ? { Authorization: `Bearer ${token}` } : {},
      })
      if (!res.ok) throw new Error('Photo de preuve indisponible.')
      const blob = await res.blob()
      const url = URL.createObjectURL(blob)
      window.open(url, '_blank', 'noopener')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Photo indisponible.')
    }
  }

  const set = (field: keyof CreateOrderRequest, value: string | number): void => {
    setForm((f) => ({ ...f, [field]: value }))
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Commandes</h1>
          <p>
            {total} commande{total > 1 ? 's' : ''} au total
            {totalPages > 1 && ` · page ${page} sur ${totalPages}`}
          </p>
        </div>
        <button className="btn btn--primary" onClick={() => setShowCreate(true)}>+ Nouvelle commande</button>
      </div>

      {error && (
        <div className="alert alert--error">
          {error}{' '}
          <button className="btn btn--ghost" onClick={() => void load(page)}>Réessayer</button>
        </div>
      )}
      {payMessage && <div className="alert alert--success">{payMessage}</div>}

      <section className="panel">
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>ID</th>
                <th>Client</th>
                <th>Description</th>
                <th>Montant</th>
                <th>Statut</th>
                <th>Date</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {orders.map((o) => (
                <tr key={o.id}>
                  <td><span className="order-id">#{shortId(o.id)}</span></td>
                  <td>{o.clientName}</td>
                  <td>{o.description}</td>
                  <td>{formatMoney(o.amount)}</td>
                  <td><StatusBadge status={o.status} /></td>
                  <td>{formatDateTime(o.createdAt)}</td>
                  <td>
                    {getUser()?.role === 'Admin' && o.hasProofPhoto && (
                      <button className="btn btn--ghost" onClick={() => void viewProof(o.id)} title="Photo de preuve">
                        📸
                      </button>
                    )}
                    {o.status !== 'Delivered' && o.status !== 'Cancelled' && (
                      <button
                        className="btn btn--primary"
                        onClick={() => void requestPayment(o.id)}
                        disabled={paying !== null}
                      >
                        {paying === o.id ? '…' : '💳 Demander le lien'}
                      </button>
                    )}
                    <button
                      className="btn btn--blue"
                      onClick={() => void broadcast(o.id)}
                      disabled={broadcasting !== null}
                    >
                      {broadcasting === o.id ? '…' : 'Broadcast'}
                    </button>
                  </td>
                </tr>
              ))}
              {orders.length === 0 && <tr><td colSpan={7} className="empty">Aucune commande</td></tr>}
            </tbody>
          </table>
        </div>

        {totalPages > 1 && (
          <div className="pager">
            <button className="btn" disabled={page <= 1} onClick={() => void load(page - 1)}>
              ← Précédent
            </button>
            <span>Page {page} / {totalPages}</span>
            <button className="btn" disabled={page >= totalPages} onClick={() => void load(page + 1)}>
              Suivant →
            </button>
          </div>
        )}
      </section>

      {showCreate && (
        <Modal title="Nouvelle commande" onClose={() => setShowCreate(false)}>
          <form onSubmit={(e) => void create(e)}>
            <div className="field">
              <label htmlFor="order-client-name">Nom du client</label>
              <input
                id="order-client-name"
                value={form.clientName}
                onChange={(e) => set('clientName', e.target.value)}
                required
              />
            </div>
            <div className="field">
              <label htmlFor="order-client-phone">WhatsApp client (E.164)</label>
              <input
                id="order-client-phone"
                value={form.clientWhatsAppNumber}
                onChange={(e) => set('clientWhatsAppNumber', e.target.value)}
                placeholder="+2250102030405"
                maxLength={20}
                required
              />
            </div>
            <div className="field">
              <label htmlFor="order-vendor-phone">WhatsApp vendeur (E.164)</label>
              <input
                id="order-vendor-phone"
                value={form.vendorWhatsAppNumber}
                onChange={(e) => set('vendorWhatsAppNumber', e.target.value)}
                placeholder="+2250708091011"
                maxLength={20}
                required
              />
            </div>
            <div className="field">
              <label htmlFor="order-description">Description</label>
              <input
                id="order-description"
                value={form.description}
                onChange={(e) => set('description', e.target.value)}
                maxLength={500}
                required
              />
            </div>
            <div className="field">
              <label htmlFor="order-amount">Montant (FCFA)</label>
              <input
                id="order-amount"
                type="number"
                min={0}
                max={9999999}
                value={form.amount}
                onChange={(e) => set('amount', Number(e.target.value))}
                required
              />
            </div>
            <div className="modal__actions">
              <button type="button" className="btn" onClick={() => setShowCreate(false)}>Annuler</button>
              <button type="submit" className="btn btn--primary" disabled={busy}>
                {busy ? 'Création…' : 'Créer'}
              </button>
            </div>
          </form>
        </Modal>
      )}
    </>
  )
}
