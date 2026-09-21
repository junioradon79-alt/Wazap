import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import type { UserSummary, VendorProduct, VendorProductRequest } from '../api/types'
import { ErrorAlert, formatMoney } from '../components/ui'
import { useAuth } from '../auth/AuthContext'

const EMPTY_FORM: VendorProductRequest = {
  name: '',
  price: 0,
  emoji: '',
  description: '',
  isAvailable: true,
  imageUrl: ''
}

const WHATSAPP_BOT = '2250787119520'

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
  const [copiedId, setCopiedId] = useState<string | null>(null)

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
      setError('')
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
      isAvailable: form.isAvailable ?? true,
      imageUrl: form.imageUrl?.trim() ? form.imageUrl.trim() : null,
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

  const toggleAvailability = async (product: VendorProduct): Promise<void> => {
    if (!vendorId) return
    const nextStatus = !product.isAvailable
    // Optimistic UI update
    setProducts((prev) =>
      prev.map((p) => (p.id === product.id ? { ...p, isAvailable: nextStatus } : p))
    )
    try {
      await api.patch(`/vendors/${vendorId}/products/${product.id}/availability`, {
        isAvailable: nextStatus,
      })
      setNotice(`Statut de « ${product.name} » : ${nextStatus ? 'En stock ✅' : 'Épuisé ❌'}`)
    } catch (err) {
      // Rollback on error
      setProducts((prev) =>
        prev.map((p) => (p.id === product.id ? { ...p, isAvailable: !nextStatus } : p))
      )
      setError(err instanceof Error ? err.message : 'Impossible de changer le stock.')
    }
  }

  const copyWhatsAppLink = (product: VendorProduct): void => {
    const activeVendor = vendors.find((v) => v.id === vendorId)
    const vendorName = activeVendor?.username ?? user?.username ?? 'le commerce'
    const emojiPrefix = product.emoji ? `${product.emoji} ` : ''
    const msg = `Bonjour WAZAP ! Je souhaite commander : ${emojiPrefix}${product.name} (${formatMoney(product.price)}) chez ${vendorName}.`
    const link = `https://wa.me/${WHATSAPP_BOT}?text=${encodeURIComponent(msg)}`

    if (navigator.clipboard) {
      void navigator.clipboard.writeText(link)
    }
    setCopiedId(product.id)
    setNotice(`Lien WhatsApp pour « ${product.name} » copié dans le presse-papier !`)
    setTimeout(() => setCopiedId(null), 2500)
  }

  const remove = async (product: VendorProduct): Promise<void> => {
    if (!vendorId) return
    if (!window.confirm(`Retirer « ${product.name} » du catalogue ?\n\nLe produit disparaîtra du menu des clients.`))
      return
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
      isAvailable: product.isAvailable,
      imageUrl: product.imageUrl ?? '',
    })
    setError('')
    setNotice('')
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Catalogue & Menu WhatsApp</h1>
          <p>
            Ces produits composent le menu présenté à vos clients sur WhatsApp (+225 {WHATSAPP_BOT}).
            Générez des liens de commande directs en 1 clic pour vos réseaux sociaux et vos statuts WhatsApp.
          </p>
        </div>
      </div>

      {error && <ErrorAlert message={error} onRetry={() => void load(vendorId)} />}
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
            <h2 className="panel__title">{editingId ? 'Modifier le produit' : 'Ajouter un produit au catalogue'}</h2>
            {editingId && <span className="panel__subtitle">Modification en cours</span>}
          </div>
          <div className="panel__body">
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 16 }}>
              <div className="field">
                <label>Nom du produit *</label>
                <input
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  placeholder="ex : Poulet braisé complet"
                  maxLength={100}
                />
              </div>
              <div className="field">
                <label>Prix (FCFA) *</label>
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
                <label>URL de la photo (optionnel)</label>
                <input
                  value={form.imageUrl ?? ''}
                  onChange={(e) => setForm({ ...form, imageUrl: e.target.value })}
                  placeholder="ex : https://mon-site.ci/photos/poulet.jpg"
                  maxLength={500}
                />
              </div>
            </div>

            <div className="field" style={{ marginTop: 12 }}>
              <label>Description courte (portion, accompagnements, etc.)</label>
              <input
                value={form.description ?? ''}
                onChange={(e) => setForm({ ...form, description: e.target.value })}
                placeholder="ex : Accompagné d'attiéké ou d'allocos avec piment frais"
                maxLength={300}
              />
            </div>

            <div className="field" style={{ marginTop: 12, display: 'flex', alignItems: 'center', gap: 10 }}>
              <label style={{ margin: 0, display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer', fontWeight: 600 }}>
                <input
                  type="checkbox"
                  checked={form.isAvailable ?? true}
                  onChange={(e) => setForm({ ...form, isAvailable: e.target.checked })}
                  style={{ width: 18, height: 18, cursor: 'pointer' }}
                />
                Produit immédiatement disponible en stock
              </label>
            </div>

            {form.imageUrl && (
              <div style={{ marginTop: 12, display: 'flex', alignItems: 'center', gap: 12 }}>
                <span style={{ fontSize: 12, color: '#7d8590' }}>Aperçu photo :</span>
                <img
                  src={form.imageUrl}
                  alt="Aperçu"
                  style={{ width: 56, height: 56, objectFit: 'cover', borderRadius: 8, border: '1px solid #30363d' }}
                  onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none' }}
                />
              </div>
            )}

            <div className="panel__actions" style={{ marginTop: 16 }}>
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
            <div>
              <h2 className="panel__title">Fiches produits & Liens WhatsApp</h2>
              <span className="panel__subtitle">Activez ou désactivez la disponibilité en 1 clic</span>
            </div>
            <span className="panel__count">{products.length} produit{products.length > 1 ? 's' : ''}</span>
          </div>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>#</th>
                  <th>Visuel</th>
                  <th>Produit</th>
                  <th>Description</th>
                  <th>Prix</th>
                  <th>Disponibilité</th>
                  <th>Lien direct</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {products.map((p, index) => (
                  <tr key={p.id} style={{ opacity: p.isAvailable ? 1 : 0.65 }}>
                    <td>{index + 1}</td>
                    <td>
                      {p.imageUrl ? (
                        <img
                          src={p.imageUrl}
                          alt={p.name}
                          style={{ width: 38, height: 38, objectFit: 'cover', borderRadius: 6, border: '1px solid #30363d' }}
                          onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none' }}
                        />
                      ) : (
                        <span style={{ fontSize: 24 }}>{p.emoji ?? '📦'}</span>
                      )}
                    </td>
                    <td>
                      <b>
                        {p.emoji ? `${p.emoji} ` : ''}
                        {p.name}
                      </b>
                    </td>
                    <td style={{ maxWidth: 220, fontSize: 13, color: '#8b949e' }}>{p.description}</td>
                    <td>
                      <b style={{ color: '#00d66c' }}>{formatMoney(p.price)}</b>
                    </td>
                    <td>
                      <button
                        type="button"
                        className={`btn ${p.isAvailable ? 'btn--success' : 'btn--ghost'}`}
                        style={{
                          padding: '3px 10px',
                          fontSize: 12,
                          fontWeight: 700,
                          borderRadius: 20,
                          cursor: 'pointer',
                          background: p.isAvailable ? 'rgba(0, 214, 108, 0.15)' : 'rgba(248, 81, 73, 0.15)',
                          color: p.isAvailable ? '#00d66c' : '#f85149',
                          border: `1px solid ${p.isAvailable ? '#00d66c' : '#f85149'}`
                        }}
                        onClick={() => void toggleAvailability(p)}
                        title="Cliquer pour basculer la disponibilité"
                      >
                        {p.isAvailable ? '✓ En stock' : '✕ Épuisé'}
                      </button>
                    </td>
                    <td>
                      <button
                        type="button"
                        className="btn btn--secondary"
                        style={{ padding: '4px 8px', fontSize: 12, display: 'inline-flex', alignItems: 'center', gap: 4 }}
                        onClick={() => copyWhatsAppLink(p)}
                        title="Copier le lien pré-rempli pour WhatsApp"
                      >
                        {copiedId === p.id ? '✅ Copié !' : '📱 Lien WhatsApp'}
                      </button>
                    </td>
                    <td>
                      <div style={{ display: 'flex', gap: 6 }}>
                        <button className="btn" onClick={() => startEdit(p)} disabled={busy} title="Modifier la fiche">
                          ✏️
                        </button>
                        <button className="btn btn--danger" onClick={() => void remove(p)} disabled={busy} title="Retirer du catalogue">
                          🗑️
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
                {products.length === 0 && (
                  <tr>
                    <td colSpan={8} className="empty">
                      Aucun produit au catalogue : le bot WhatsApp basculera automatiquement en saisie de texte libre pour vos clients.
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
