// Badge de statut coloré réutilisable.
export function StatusBadge({ status }: { status: string }) {
  // Les espaces et accents sont retirés : le serveur envoie des libellés lisibles
  // (« Recherche Livreur », « En livraison ») qu'un simple toLowerCase() ne faisait
  // pas correspondre — le badge restait gris.
  const key = status
    .toLowerCase()
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^a-z]/g, '')

  let cls = 'badge--gray'
  if (['completed', 'delivered', 'livre', 'active', 'oui', 'ok'].includes(key)) cls = 'badge--green'
  else if (['pending', 'enlivraison', 'recherchelivreur', 'encours', 'assigned'].some((s) => key.includes(s))) cls = 'badge--blue'
  else if (['failed', 'cancelled', 'annule', 'refused', 'non'].includes(key)) cls = 'badge--red'
  else if (['new', 'nouvelle', 'confirmed', 'confirmer'].includes(key)) cls = 'badge--orange'

  return <span className={`badge ${cls}`}>{status}</span>
}

export function formatMoney(amount: number | null | undefined): string {
  // `Intl.NumberFormat.format(undefined)` renvoie « NaN » : on affiche un tiret plutôt
  // qu'un montant faux (le champ peut être absent d'une réponse d'API).
  if (amount === null || amount === undefined || !Number.isFinite(amount)) return '—'
  return new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 0 }).format(amount) + ' FCFA'
}

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  // `new Date('n/a').toLocaleString()` ne lève PAS : il renvoie « Invalid Date ».
  // Un try/catch était donc inopérant, et l'écran affichait « Invalid Date ».
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return iso

  return date.toLocaleString('fr-FR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

export function shortId(id: string): string {
  return id.replace(/-/g, '').slice(0, 6).toUpperCase()
}
