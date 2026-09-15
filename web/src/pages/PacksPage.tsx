import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { PackDto, PaymentResponse, UserSummary } from '../api/types'
import { ErrorAlert, formatMoney } from '../components/ui'

export default function PacksPage() {
  const [packs, setPacks] = useState<PackDto[]>([])
  const [vendors, setVendors] = useState<UserSummary[]>([])
  const [vendorId, setVendorId] = useState('')
  const [buying, setBuying] = useState<string | null>(null)
  const [payment, setPayment] = useState<PaymentResponse | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  // Extrait de l'effet : un échec de chargement laissait la grille de packs vide, sans
  // aucun moyen de relancer (P2 / C-05).
  const load = async (): Promise<void> => {
    setLoading(true)
    try {
      const [p, v] = await Promise.all([api.get<PackDto[]>('/packs'), api.get<UserSummary[]>('/vendors')])
      setPacks(p)
      setVendors(v.filter((x) => x.role === 'Vendor'))
      setError('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void load()
    // Chargement initial uniquement.
  }, [])

  const buy = async (pack: PackDto): Promise<void> => {
    if (!vendorId) {
      setError('Sélectionnez un vendeur.')
      return
    }
    setBuying(pack.name)
    setError('')
    setPayment(null)
    try {
      const res = await api.post<PaymentResponse>('/packs/buy', { vendorId, packName: pack.name })
      setPayment(res)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Achat impossible.')
    } finally {
      setBuying(null)
    }
  }

  if (loading) return <div className="loading"><span className="loading__spinner" /> Chargement…</div>

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Packs prépayés</h1>
          <p>Catalogue des crédits de commandes — paiement via GeniusPay</p>
        </div>
      </div>

      <section className="panel" style={{ padding: 20 }}>
        <div className="field" style={{ maxWidth: 380 }}>
          <label htmlFor="vendor">Vendeur concerné</label>
          <select id="vendor" value={vendorId} onChange={(e) => setVendorId(e.target.value)}>
            <option value="">— Sélectionner un vendeur —</option>
            {vendors.map((v) => (
              <option key={v.id} value={v.id}>
                {v.username} · {v.credits} crédits
              </option>
            ))}
          </select>
        </div>
      </section>

      {error && <ErrorAlert message={error} onRetry={() => void load()} />}

      {payment && (
        <div className="alert alert--success">
          {payment.success && payment.paymentLink ? (
            <>
              Paiement à finaliser sur GeniusPay.
              <div className="checkout-box">
                <a href={payment.paymentLink} target="_blank" rel="noreferrer">
                  Ouvrir la page de paiement → {payment.paymentLink}
                </a>
              </div>
            </>
          ) : payment.success ? (
            `Crédits ajoutés ! ${payment.message}`
          ) : (
            payment.message
          )}
        </div>
      )}

      <section className="pack-grid">
        {packs.map((pack) => (
          <article key={pack.name} className="pack-card">
            <div className="pack-card__name">{pack.name}</div>
            <div className="pack-card__price">{formatMoney(pack.price)}</div>
            <div className="pack-card__credits">≈ {pack.credits} commandes</div>
            <button className="btn btn--primary" onClick={() => buy(pack)} disabled={buying !== null || !vendorId}>
              {buying === pack.name ? 'Création…' : 'Acheter'}
            </button>
          </article>
        ))}
      </section>
    </>
  )
}
