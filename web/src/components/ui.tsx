// Badge de statut coloré réutilisable.
export function StatusBadge({ status }: { status: string }) {
  const key = status.toLowerCase()

  let cls = 'badge--gray'
  if (['completed', 'delivered', 'livré', 'active', 'oui', 'ok'].includes(key)) cls = 'badge--green'
  else if (['pending', 'enlivraison', 'en livraison', 'recherchelivreur', 'en cours', 'assigned'].some((s) => key.includes(s))) cls = 'badge--blue'
  else if (['failed', 'cancelled', 'annulé', 'refused', 'non'].includes(key)) cls = 'badge--red'
  else if (['new', 'nouvelle', 'confirmed', 'confirmer'].includes(key)) cls = 'badge--orange'

  return <span className={`badge ${cls}`}>{status}</span>
}

export function formatMoney(amount: number): string {
  return new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 0 }).format(amount) + ' FCFA'
}

export function formatDateTime(iso: string): string {
  try {
    return new Date(iso).toLocaleString('fr-FR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    })
  } catch {
    return iso
  }
}

export function shortId(id: string): string {
  return id.replace(/-/g, '').slice(0, 6).toUpperCase()
}
