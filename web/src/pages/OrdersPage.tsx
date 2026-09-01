import { useEffect, useState, type FormEvent } from 'react'
import { api } from '../api/client'
import type { CreateOrderRequest, OrderDto, PagedResult } from '../api/types'
import { StatusBadge, formatDateTime, formatMoney, shortId } from '../components/ui'

export default function OrdersPage() {
  const [orders, setOrders] = useState<OrderDto[]>([])
  const [total, setTotal] = useState(0)
  const [error, setError] = useState('')
  const [showCreate, setShowCreate] = useState(false)
  const [busy, setBusy] = useState(false)
  const [broadcasting, setBroadcasting] = useState<string | null>(null)

  const [form, setForm] = useState<CreateOrderRequest>({
    clientName: '',
    clientWhatsAppNumber: '',
    vendorWhatsAppNumber: '',
    description: '',
    amount: 0,
  })

  const load = async (): Promise<void> => {
    try {
      const data = await api.get<PagedResult<OrderDto>>('/orders?page=1&pageSize=50')
      setOrders(data.items)
      setTotal(data.total)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur')
    }
  }

  useEffect(() => {
    void load()
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
    setBroadcasting(id)
    try {
      await api.post(`/orders/${id}/broadcast`)
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Diffusion impossible.')
    } finally {
      setBroadcasting(null)
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
          <p>{total} commandes au total</p>
        </div>
        <button className="btn btn--primary" onClick={() => setShowCreate(true)}>+ Nouvelle commande</button>
      </div>

      {error && <div className="alert alert--error">{error}</div>}

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
      </section>

      {showCreate && (
        <div className="modal-backdrop" onClick={() => setShowCreate(false)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <h3>Nouvelle commande</h3>
            <form onSubmit={(e) => void create(e)}>
              <div className="field">
                <label>Nom du client</label>
                <input value={form.clientName} onChange={(e) => set('clientName', e.target.value)} required />
              </div>
              <div className="field">
                <label>WhatsApp client (E.164)</label>
                <input value={form.clientWhatsAppNumber} onChange={(e) => set('clientWhatsAppNumber', e.target.value)} placeholder="+2250102030405" required />
              </div>
              <div className="field">
                <label>WhatsApp vendeur (E.164)</label>
                <input value={form.vendorWhatsAppNumber} onChange={(e) => set('vendorWhatsAppNumber', e.target.value)} placeholder="+2250708091011" required />
              </div>
              <div className="field">
                <label>Description</label>
                <input value={form.description} onChange={(e) => set('description', e.target.value)} required />
              </div>
              <div className="field">
                <label>Montant (FCFA)</label>
                <input type="number" min={0} value={form.amount} onChange={(e) => set('amount', Number(e.target.value))} required />
              </div>
              <div className="modal__actions">
                <button type="button" className="btn" onClick={() => setShowCreate(false)}>Annuler</button>
                <button type="submit" className="btn btn--primary" disabled={busy}>
                  {busy ? 'Création…' : 'Créer'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </>
  )
}
