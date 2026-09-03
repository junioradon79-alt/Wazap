import { useCallback, useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api } from '../api/client'
import type { ClientOrderStatus } from '../api/types'

const STATUS_FR: Record<string, string> = {
  PendingVendorConfirmation: 'En attente du vendeur',
  VendorConfirmed: 'Vendeur accepté',
  AwaitingRiderAcceptance: 'Recherche d’un livreur…',
  RiderAssigned: 'Livreur en route',
  ReadyForPickup: 'Prêt',
  PickedUp: 'Colis récupéré',
  InTransit: 'En cours de livraison',
  Delivered: 'Livré ✓',
  Cancelled: 'Annulé',
}

const s = {
  wrap: { maxWidth: 460, margin: '0 auto', padding: '20px 16px 40px', fontFamily: 'system-ui, sans-serif', color: '#1c2733' },
  logo: { fontWeight: 800, fontSize: 20, margin: '6px 0 16px' },
  card: { background: '#fff', borderRadius: 14, padding: 18, boxShadow: '0 2px 8px rgba(0,0,0,.05)', marginBottom: 14 },
  muted: { color: '#6b7684', fontSize: 13 },
  pill: { display: 'inline-block', background: '#e7f6ee', color: '#0e7a3e', fontWeight: 700, fontSize: 12, borderRadius: 999, padding: '4px 10px' },
  label: { display: 'block', fontSize: 13, fontWeight: 600, margin: '12px 0 4px' },
  input: { width: '100%', padding: 12, border: '1px solid #d5dbe2', borderRadius: 10, fontSize: 15, boxSizing: 'border-box' as const },
  coords: { fontSize: 12, color: '#6b7684', marginTop: 6, wordBreak: 'break-all' as const },
  btn: { width: '100%', padding: 14, border: 'none', borderRadius: 12, fontSize: 16, fontWeight: 700, cursor: 'pointer', background: '#1db954', color: '#fff', marginTop: 12 },
  btn2: { width: '100%', padding: 14, border: 'none', borderRadius: 12, fontSize: 15, fontWeight: 600, cursor: 'pointer', background: '#eef1f4', color: '#1c2733', marginTop: 8 },
  row: { display: 'flex', justifyContent: 'space-between' as const, alignItems: 'center' as const, gap: 8 },
  err: { color: '#c0392b', background: '#fdecea', padding: 10, borderRadius: 8, fontSize: 14, marginTop: 10, whiteSpace: 'pre-wrap' as const },
}
export default function SuiviPage() {
  const { id } = useParams<{ id: string }>()
  const [order, setOrder] = useState<ClientOrderStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [lat, setLat] = useState<number | null>(null)
  const [lng, setLng] = useState<number | null>(null)
  const [address, setAddress] = useState('')
  const [geoErr, setGeoErr] = useState<string | null>(null)
  const [sending, setSending] = useState(false)
  const [sent, setSent] = useState(false)
  const [rider, setRider] = useState<{ riderName?: string; location?: { latitude: number; longitude: number } | null } | null>(null)
  const timer = useRef<ReturnType<typeof setInterval> | null>(null)

  const fetchOrder = useCallback(async () => {
    try {
      const o = await api.get<ClientOrderStatus>(`/client/orders/${id}`)
      setOrder(o)
      setSent(o.hasCoordinates)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Erreur de chargement')
    }
  }, [id])

  useEffect(() => {
    fetchOrder()
    return () => { if (timer.current) clearInterval(timer.current) }
  }, [fetchOrder])

  const startPolling = useCallback(() => {
    if (timer.current) clearInterval(timer.current)
    timer.current = setInterval(async () => {
      try {
        const o = await api.get<ClientOrderStatus>(`/client/orders/${id}`)
        setOrder(o)
        if (o.delivered && timer.current) clearInterval(timer.current)
        const active = ['RiderAssigned', 'ReadyForPickup', 'PickedUp', 'InTransit'].includes(o.status)
        if (active) {
          try {
            const r = await api.get<{ riderName?: string; location?: { latitude: number; longitude: number } | null }>(`/client/orders/${id}/rider-location`)
            setRider(r)
          } catch { /* silencieux */ }
        }
      } catch { /* silencieux */ }
    }, 5000)
  }, [id])

  const useGeo = () => {
    setGeoErr(null)
    if (!navigator.geolocation) { setGeoErr('Géolocalisation non supportée — saisissez votre adresse.'); return }
    navigator.geolocation.getCurrentPosition(
      (p) => { setLat(p.coords.latitude); setLng(p.coords.longitude) },
      () => setGeoErr('Position impossible — saisissez votre adresse (le livreur vous appellera).'),
    )
  }

  const submit = async () => {
    if (lat == null || lng == null) { setGeoErr('Activez votre position (📍) avant de valider.'); return }
    setSending(true)
    try {
      await api.post<{ status: string }>(`/client/orders/${id}/coordinates`, {
        latitude: lat,
        longitude: lng,
        address: address || null,
      })
      setSent(true)
      await fetchOrder()
      startPolling()
    } catch (e) {
      setGeoErr(e instanceof Error ? e.message : 'Erreur d’envoi')
    } finally {
      setSending(false)
    }
  }

  if (error) return <div style={s.wrap}><div style={{ ...s.card, ...s.err }}>{error}</div></div>
  if (!order) return <div style={s.wrap}><div style={{ ...s.card, ...s.muted }}>Chargement de votre commande…</div></div>

  const statusLabel = order.delivered ? 'Livré ✓' : STATUS_FR[order.status] || order.status

  return (
    <div style={s.wrap}>
      <div style={s.logo}>⚡ WAZAP <span style={s.muted}>— Suivi</span></div>
      {order.needsCoordinates && !order.hasCoordinates && !sent ? (
        <div style={s.card}>
          <div style={s.row}><h2 style={{ margin: 0 }}>Commande #{order.code}</h2></div>
          <p style={s.muted}>Vendeur : {order.vendorName || '—'}</p>
          {order.description && <p>{order.description}</p>}
          <label style={s.label}>Votre adresse / repère (quartier, rue…)</label>
          <input style={s.input} value={address} onChange={(e) => setAddress(e.target.value)}
            placeholder="Ex : Marcory, rue Princesse, près de la pharmacie" />
          <button style={s.btn2} type="button" onClick={useGeo}>📍 Utiliser ma position GPS</button>
          {(lat != null && lng != null) && <div style={s.coords}>Position : {lat.toFixed(5)}, {lng.toFixed(5)}</div>}
          {geoErr && <div style={s.err}>{geoErr}</div>}
          <button style={{ ...s.btn, opacity: sending ? 0.6 : 1 }} disabled={sending} onClick={submit}>
            {sending ? 'Envoi…' : 'Valider et lancer la livraison'}
          </button>
        </div>
      ) : (
        <div style={s.card}>
          <div style={s.row}>
            <h2 style={{ margin: 0 }}>Commande #{order.code}</h2>
            <span style={s.pill}>{statusLabel}</span>
          </div>
          <p style={s.muted}>Vendeur : {order.vendorName || '—'}</p>
          {order.description && <p>{order.description}</p>}
          {order.address && <p style={s.muted}>📍 {order.address}</p>}
        </div>
      )}
      {rider && (
        <div style={s.card}>
          <div style={s.row}><span style={{ fontWeight: 700 }}>🛵 {rider.riderName || 'Livreur'}</span><span style={s.pill}>En route</span></div>
          {rider.location
            ? <a
                href={`https://www.google.com/maps/search/?api=1&query=${rider.location.latitude},${rider.location.longitude}`}
                target="_blank" rel="noreferrer"
                style={{ ...s.btn, textDecoration: 'none', textAlign: 'center', display: 'block' }}
              >📍 Voir le livreur sur la carte</a>
            : <p style={s.muted}>Le livreur se rapproche de chez vous…</p>}
        </div>
      )}
    </div>
  )
}

