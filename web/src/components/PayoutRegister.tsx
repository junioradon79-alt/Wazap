import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import { formatMoney } from './ui'

interface Payout {
  id: string
  paymentId: string
  beneficiaryRole: string
  beneficiaryPhone: string | null
  amount: number
  confirmedAtUtc: string | null
  providerReference: string | null
}
interface UncertainPayment { id: string; orderId: string; amount: number }

export default function PayoutRegister() {
  const [rows, setRows] = useState<Payout[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pendingOnly, setPendingOnly] = useState(true)
  const [selected, setSelected] = useState<Payout | null>(null)
  const [provider, setProvider] = useState('')
  const [reference, setReference] = useState('')
  const [evidence, setEvidence] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [uncertain, setUncertain] = useState<UncertainPayment[]>([])
  const [historical, setHistorical] = useState(0)
  const [providerReferences, setProviderReferences] = useState<Record<string, string>>({})
  const load = useCallback(async () => {
    try {
      const res = await api.get<{ total: number; items: Payout[] }>(`/admin/payouts?page=${page}&pendingOnly=${pendingOnly}`)
      setRows(res.items); setTotal(res.total); setError('')
      const queue = await api.get<{ items: UncertainPayment[]; historicalCompletedWithoutPayout: number }>('/admin/payouts/reconciliation')
      setUncertain(queue.items); setHistorical(queue.historicalCompletedWithoutPayout)
    } catch (e) { setError(e instanceof Error ? e.message : 'Chargement impossible') }
  }, [page, pendingOnly])
  useEffect(() => { void load() }, [load])
  const confirm = async () => {
    if (!selected) return
    setBusy(true)
    try {
      await api.post(`/admin/payouts/${selected.id}/confirm`, { provider, reference, evidence, amount: selected.amount })
      setSelected(null); setReference(''); setEvidence(''); await load()
    } catch (e) { setError(e instanceof Error ? e.message : 'Rapprochement impossible') }
    finally { setBusy(false) }
  }
  return <section className="panel" style={{ padding: 20, marginTop: 20 }}>
    <h2>Versements commerçants et livreurs</h2>
    <p>Sommes dues après encaissement. Rapprochez uniquement les transferts réellement effectués avec une preuve du prestataire.</p>
    <label><input type="checkbox" checked={pendingOnly} onChange={e => { setPendingOnly(e.target.checked); setPage(1) }} /> À verser uniquement</label>
    <button type="button" onClick={() => void load()}>Actualiser</button>
    {error && <p role="alert">{error}</p>}
    {historical > 0 && <p role="status">{historical} paiement(s) historique(s) nécessitent une reprise du registre des versements avec leurs justificatifs.</p>}
    {uncertain.length > 0 && <div><h3>Initiations à vérifier chez le prestataire</h3><p>Aucune nouvelle session n’est créée tant que le résultat reste inconnu.</p>
      {uncertain.map(payment => <div key={payment.id}>
        <span className="mono">{payment.id}</span> — {formatMoney(payment.amount)}
        <input aria-label={`Référence prestataire ${payment.id}`} maxLength={200} value={providerReferences[payment.id] || ''} onChange={e => setProviderReferences(prev => ({ ...prev, [payment.id]: e.target.value }))} />
        <button disabled={busy || !providerReferences[payment.id]} onClick={() => {
          setBusy(true)
          void api.post(`/admin/payouts/payments/${payment.id}/reconcile`, { reference: providerReferences[payment.id] })
            .then(() => load()).catch(e => setError(e instanceof Error ? e.message : 'Vérification impossible')).finally(() => setBusy(false))
        }}>Vérifier auprès du prestataire</button>
      </div>)}
    </div>}
    <div className="table-wrap"><table className="table"><thead><tr><th>Bénéficiaire</th><th>Compte</th><th>Montant</th><th>État</th><th>Preuve</th></tr></thead>
      <tbody>{rows.map(row => <tr key={row.id}>
        <td>{row.beneficiaryRole === 'Vendor' ? 'Commerçant' : 'Livreur'}</td><td>{row.beneficiaryPhone || 'Bénéficiaire à définir'}</td><td>{formatMoney(row.amount)}</td>
        <td>{row.confirmedAtUtc ? 'Versement rapproché' : 'À verser'}</td>
        <td>{row.confirmedAtUtc ? row.providerReference : <button disabled={!row.beneficiaryPhone} onClick={() => setSelected(row)}>Rapprocher un transfert</button>}</td>
      </tr>)}</tbody></table></div>
    <p>{total} versement(s) — page {page}</p>
    <button disabled={page === 1} onClick={() => setPage(p => p - 1)}>Précédent</button>
    <button disabled={page * 50 >= total} onClick={() => setPage(p => p + 1)}>Suivant</button>
    {selected && <form onSubmit={e => { e.preventDefault(); void confirm() }}>
      <h3>Preuve de transfert : {formatMoney(selected.amount)} vers {selected.beneficiaryPhone}</h3>
      <label>Prestataire <input required maxLength={80} value={provider} onChange={e => setProvider(e.target.value)} /></label>
      <label>Référence du transfert <input required maxLength={200} value={reference} onChange={e => setReference(e.target.value)} /></label>
      <label>Preuve ou emplacement du justificatif <textarea required maxLength={1000} value={evidence} onChange={e => setEvidence(e.target.value)} /></label>
      <button disabled={busy} type="submit">Enregistrer le rapprochement</button>
      <button type="button" onClick={() => setSelected(null)}>Annuler</button>
    </form>}
  </section>
}
