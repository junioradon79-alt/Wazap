import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { CreditTransaction, UserSummary } from '../api/types'
import { StatusBadge, formatDateTime, formatMoney } from '../components/ui'

export default function TransactionsPage() {
  const [vendors, setVendors] = useState<UserSummary[]>([])
  const [vendorId, setVendorId] = useState('')
  const [txs, setTxs] = useState<CreditTransaction[]>([])
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    api
      .get<UserSummary[]>('/vendors')
      .then((v) => setVendors(v.filter((x) => x.role === 'Vendor')))
      .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Erreur'))
  }, [])

  const load = async (id: string): Promise<void> => {
    setLoading(true)
    setError('')
    try {
      const data = await api.get<CreditTransaction[]>(`/vendors/${id}/transactions`)
      setTxs(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Erreur')
    } finally {
      setLoading(false)
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Transactions</h1>
          <p>Historique des achats de packs (crédits) par vendeur</p>
        </div>
      </div>

      <section className="panel" style={{ padding: 20 }}>
        <div className="field" style={{ maxWidth: 380 }}>
          <label htmlFor="vendor">Vendeur</label>
          <select
            id="vendor"
            value={vendorId}
            onChange={(e) => {
              setVendorId(e.target.value)
              if (e.target.value) void load(e.target.value)
            }}
          >
            <option value="">— Sélectionner un vendeur —</option>
            {vendors.map((v) => (
              <option key={v.id} value={v.id}>
                {v.username}
              </option>
            ))}
          </select>
        </div>
      </section>

      {error && <div className="alert alert--error">{error}</div>}

      <section className="panel">
        <header className="panel__header">
          <div>
            <h2 className="panel__title">Historique</h2>
            <p className="panel__subtitle">Transactions GeniusPay &amp; crédits ajoutés</p>
          </div>
          {vendorId && <span className="panel__count">{txs.length} transactions</span>}
        </header>

        {loading ? (
          <div className="loading"><span className="loading__spinner" /> Chargement…</div>
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Référence</th>
                  <th>Montant</th>
                  <th>Crédits</th>
                  <th>Statut</th>
                </tr>
              </thead>
              <tbody>
                {txs.map((tx) => (
                  <tr key={tx.id}>
                    <td>{formatDateTime(tx.createdAt)}</td>
                    <td><span className="mono">{tx.transactionReference}</span></td>
                    <td>{formatMoney(tx.amount)}</td>
                    <td><b>+{tx.creditsPurchased}</b></td>
                    <td><StatusBadge status={tx.status} /></td>
                  </tr>
                ))}
                {!vendorId && <tr><td colSpan={5} className="empty">Sélectionnez un vendeur pour voir son historique</td></tr>}
                {vendorId && txs.length === 0 && <tr><td colSpan={5} className="empty">Aucune transaction</td></tr>}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  )
}
