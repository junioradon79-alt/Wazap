import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import type { UserSummary, VendorProduct, VendorProductRequest } from '../api/types'
import { formatMoney } from '../components/ui'
import { useAuth } from '../auth/AuthContext'

const EMPTY_FORM: VendorProductRequest = { name: '', price: 0, emoji: '', description: '' }

/**
 * Catalogue produits du vendeur (admin) / de son propre commerce (vendeur).
 * Ce catalogue alimente le menu numéroté du bot WhatsApp de commande client.
 */
export default function CataloguePage() {
  const { user } = useAuth()
  const isAdmin = user?.role === 'Admin'

  const [vendors, setVendors] = useState<UserSummary[]>([])
  const [vendorId, setVendorId] = useState(isAdmin ? '' : user?.userId ?? '')
  const [products, setProducts] = useState<VendorProduct[]>([])
  const [form, setForm] = useState<VendorProductRequest>(EMPTY_FORM)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)

  // Admin : choisir le vendeur dont on gère le catalogue.
  useEffect(() => {
    if (!isAdmin) return
    void (async () => {
      try {
        const list = await api.get<UserSummary[]>('/vendors')
        setVendors(list)
        setVendorId((current) => current || list[0]?.id || '')
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Erreur')
      }
    })()
  }, [isAdmin])

  const load = useCallback(async (id: string): Promise<void> => {
    if (!id) {
      setProducts([])
      return
    }
    try {
      setProducts(await api.get<VendorProduct[]>(`/vendors/${id}/products`))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur')
    }
  }, [])

  useEffect(() => {
    void load(vendorId)
  }, [load, vendorId])

  const resetForm = (): void => {
    setForm(EMPTY_FORM)
    setEditingId(null)
  }

  const submit = async (): Promise<void> => {
    if (!vendorId) return

    const name = form.name.trim()
    if (name.length < 2) {
      setError('Le nom du produit est obligatoire (2 caractères minimum).')
      return
    }

    const price = Number(form.price)
    const payload: VendorProductRequest = {
      name,
      description: form.description?.trim() ? form.description.trim() : null,
      price: Number.isFinite(price) && price > 0 ? price : 0,
      emoji: form.emoji?.trim() ? form.emoji.trim() : null,
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      if (editingId) {
        await api.put(`/vendors/${vendorId}/products/${editingId}`, payload)
        setNotice('Produit mis à jour.')
      } else {
        await api.post(`/vendors/${vendorId}/products`, payload)
        setNotice('Produit ajouté au catalogue.')
      }
      resetForm()
      await load(vendorId)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur')
    } finally {
      setBusy(false)
    }
  }

  const remove = async (product: VendorProduct): Promise<void> => {
    if (!vendorId) return
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await api.del(`/vendors/${vendorId}/products/${product.id}`)
      setNotice(`Produit « ${product.name} » retiré du catalogue.`)
      if (editingId === product.id) resetForm()
      await load(vendorId)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur')
    } finally {
      setBusy(false)
    }
  }

  const startEdit = (product: VendorProduct): void => {
    setEditingId(product.id)
    setForm({
      name: product.name,
      description: product.description,
      price: product.price,
      emoji: product.emoji ?? '',
    })
    setError('')
    setNotice('')
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Catalogue produits</h1>
          <p>
            Ces produits composent le menu que vos clients voient sur WhatsApp. Un client qui écrit
            « COMMANDE » choisit son commerce puis ses articles par numéro — la commande arrive ici
            en attente de confirmation.
          </p>
        </div>
      </div>

      {error && <div className="alert alert--error">{error}</div>}
      {notice && <div className="alert alert--success">{notice}</div>}

      {isAdmin && (
        <section className="panel">
          <div className="panel__body">
            <div className="field">
              <label>Vendeur dont vous gérez le catalogue</label>
              <select
                value={vendorId}
                onChange={(e) => {
                  setVendorId(e.target.value)
                  resetForm()
                }}
              >
                <option value="">— Choisir un vendeur —</option>
                {vendors.map((v) => (
                  <option key={v.id} value={v.id}>
                    {v.username}
                    {v.zone ? ` (${v.zone})` : ''}
                  </option>
                ))}
              </select>
            </div>
          </div>
        </section>
      )}

      {!vendorId && <div className="alert alert--info">Sélectionnez un vendeur pour gérer son catalogue.</div>}

      {vendorId && (
        <section className="panel">
          <div className="panel__header">
            <h2 className="panel__title">{editingId ? 'Modifier le produit' : 'Ajouter un produit'}</h2>
            {editingId && <span className="panel__subtitle">Modification en cours</span>}
          </div>
          <div className="panel__body">
            <div className="field">
              <label>Nom du produit *</label>
              <input
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                placeholder="ex : Poulet braisé"
                maxLength={100}
              />
            </div>
            <div className="field">
              <label>Prix (FCFA)</label>
              <input
                type="number"
                min={0}
                value={String(form.price ?? 0)}
                onChange={(e) => setForm({ ...form, price: Number(e.target.value) })}
              />
            </div>
            <div className="field">
              <label>Emoji (optionnel)</label>
              <input
                value={form.emoji ?? ''}
                onChange={(e) => setForm({ ...form, emoji: e.target.value })}
                placeholder="ex : 🍗"
                maxLength={10}
              />
            </div>
            <div className="field">
              <label>Description (optionnel)</label>
              <input
                value={form.description ?? ''}
                onChange={(e) => setForm({ ...form, description: e.target.value })}
                placeholder="ex : portion 1"
                maxLength={300}
              />
            </div>
            <div className="panel__actions">
              {editingId && (
                <button className="btn btn--ghost" onClick={resetForm} disabled={busy}>
                  Annuler
                </button>
              )}
              <button className="btn btn--primary" onClick={() => void submit()} disabled={busy}>
                {busy ? '…' : editingId ? 'Mettre à jour' : 'Ajouter au catalogue'}
              </button>
            </div>
          </div>
        </section>
      )}

      {vendorId && (
        <section className="panel">
          <div className="panel__header">
            <h2 className="panel__title">Produits du menu</h2>
            <span className="panel__count">{products.length}</span>
          </div>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>#</th>
                  <th>Produit</th>
                  <th>Description</th>
                  <th>Prix</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {products.map((p, index) => (
                  <tr key={p.id}>
                    <td>{index + 1}</td>
                    <td>
                      <b>
                        {p.emoji ? `${p.emoji} ` : ''}
                        {p.name}
                      </b>
                    </td>
                    <td>{p.description}</td>
                    <td>{formatMoney(p.price)}</td>
                    <td>
                      <div style={{ display: 'flex', gap: 8 }}>
                        <button className="btn" onClick={() => startEdit(p)} disabled={busy}>
                          Modifier
                        </button>
                        <button className="btn btn--danger" onClick={() => void remove(p)} disabled={busy}>
                          Retirer
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
                {products.length === 0 && (
                  <tr>
                    <td colSpan={5} className="empty">
                      Aucun produit : le bot de commande basculera en mode texte libre pour ce commerce.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </section>
      )}
    </>
  )
}
