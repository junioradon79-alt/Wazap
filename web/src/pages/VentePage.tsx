import { useEffect, useState } from 'react'
import { api } from '../api/client'

// ===== Style (mobile-first, identité WAZAP) =================================
const t = {
  wrap: { maxWidth: 720, margin: '0 auto', padding: '0 18px 60px', fontFamily: 'system-ui, -apple-system, Segoe UI, Roboto, sans-serif', color: '#15221b', lineHeight: 1.5 } as const,
  header: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '14px 0' } as const,
  logo: { fontWeight: 800, fontSize: 20, letterSpacing: -0.5 } as const,
  hero: { textAlign: 'center' as const, padding: '28px 4px 10px' },
  badge: { display: 'inline-block', background: '#e7f6ee', color: '#0e7a3e', fontWeight: 700, fontSize: 13, borderRadius: 999, padding: '6px 14px', marginBottom: 14 },
  h1: { fontSize: 32, lineHeight: 1.15, margin: '0 0 10px' } as const,
  sub: { fontSize: 17, color: '#48604f', margin: '0 auto 22px', maxWidth: 560 },
  chips: { display: 'flex', flexWrap: 'wrap', gap: 8, justifyContent: 'center', marginTop: 18 } as const,
  chip: { background: '#eef1ee', borderRadius: 999, padding: '6px 12px', fontSize: 13, fontWeight: 600 },
  card: { background: '#fff', border: '1px solid #e3e9e4', borderRadius: 16, padding: 20, boxShadow: '0 4px 16px rgba(21,34,27,.06)', marginTop: 18 } as const,
  offer: { background: 'linear-gradient(135deg,#0e7a3e,#07a04b)', color: '#fff', border: 'none', textAlign: 'center' as const },
  offerTitle: { fontSize: 21, fontWeight: 800, margin: '0 0 6px' } as const,
  muted: { color: '#5d7164', fontSize: 14 },
  steps: { display: 'grid', gap: 14, marginTop: 6 } as const,
  step: { display: 'flex', gap: 12, alignItems: 'flex-start' } as const,
  num: { minWidth: 30, height: 30, borderRadius: '50%', background: '#e7f6ee', color: '#0e7a3e', fontWeight: 800, display: 'flex', alignItems: 'center', justifyContent: 'center' } as const,
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(200px,1fr))', gap: 10 } as const,
  item: { background: '#f6f9f7', borderRadius: 12, padding: 12, fontSize: 14 } as const,
  label: { display: 'block', fontSize: 13, fontWeight: 700, margin: '14px 0 4px' } as const,
  input: { width: '100%', padding: 12, border: '1px solid #cfd9d2', borderRadius: 10, fontSize: 15, boxSizing: 'border-box' as const, background: '#fff' },
  btn: { display: 'block', width: '100%', padding: 14, border: 'none', borderRadius: 12, fontSize: 16, fontWeight: 800, cursor: 'pointer', background: '#07a04b', color: '#fff', marginTop: 16 } as const,
  btnGrey: { background: '#eef1ee', color: '#15221b', marginTop: 8 } as const,
  btnGhost: { display: 'inline-block', background: '#07a04b', color: '#fff', textDecoration: 'none', fontWeight: 800, padding: '14px 22px', borderRadius: 12, fontSize: 16 },
  err: { color: '#b02a2a', background: '#fdecea', padding: 10, borderRadius: 8, fontSize: 14, marginTop: 12, whiteSpace: 'pre-wrap' as const },
  ok: { background: '#e7f6ee', color: '#0e7a3e', padding: 12, borderRadius: 10, fontSize: 15, marginTop: 14 },
  footer: { textAlign: 'center' as const, color: '#8a988f', fontSize: 13, marginTop: 34 },
  small: { fontSize: 12, color: '#8a988f', textAlign: 'center' as const, marginTop: 10 },
}

const ZONES = ['Cocody', 'Marcory', 'Yopougon', 'Adjamé', 'Treichville', 'Koumassi', 'Plateau', 'Abobo', 'Port-Bouët']
const AVANTAGES: [string, string, string][] = [
  ['📈', 'Vendez plus', 'Livrez à ceux qui ne peuvent pas se déplacer — plus jamais de commande perdue.'],
  ['📱', '100 % WhatsApp', 'Zéro application à installer : vos clients commandent par message, vous gérez tout ici.'],
  ['🛵', 'Livreur automatique', 'Le livreur le plus proche est trouvé et assigné automatiquement (GPS + zone).'],
  ['📍', 'Suivi en direct', 'Vos clients suivent leur livreur en temps réel et vous savez où en est chaque course.'],
  ['💰', 'Sans abonnement', 'Des packs de crédits payés à l’usage (Orange Money / Mobile Money).'],
  ['🎁', 'Parrainage', '+5 crédits offerts pour chaque commerçant que vous recommandez.'],
]

export default function VentePage() {
  const [whatsapp, setWhatsapp] = useState('')
  const [business, setBusiness] = useState('')
  const [contact, setContact] = useState('')
  const [phone, setPhone] = useState('')
  const [zone, setZone] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  useEffect(() => {
    api.get<{ whatsappNumber: string }>('/public/sales/config')
      .then((c) => setWhatsapp(c.whatsappNumber))
      .catch(() => setWhatsapp(''))
  }, [])

  const waLink = (text: string) => {
    if (!whatsapp) return null
    const n = whatsapp.replace(/[^\d]/g, '').replace(/^0+/, '')
    return `https://wa.me/${n}?text=${encodeURIComponent(text)}`
  }

  const submit = async () => {
    setError(null)
    if (!business.trim()) { setError('Indiquez le nom de votre commerce.'); return }
    if (!zone.trim()) { setError('Choisissez votre quartier / commune.'); return }
    if (!phone.trim() || phone.replace(/\D/g, '').length < 8) { setError('Numéro WhatsApp invalide.'); return }
    setBusy(true)
    try {
      await api.post<{ leadId: string }>('/public/leads', {
        businessName: business.trim(),
        contactName: contact.trim() || null,
        whatsappNumber: phone.trim(),
        zone: zone.trim(),
        source: 'page-vente',
      })
      setDone(true)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Erreur d’envoi, réessayez.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div style={t.wrap}>
      <div style={t.header}>
        <div style={t.logo}>⚡ WAZAP</div>
        <a style={{ ...t.btnGhost, padding: '9px 16px', fontSize: 14 }} href="#inscription">Commencer</a>
      </div>

      <section style={t.hero}>
        <span style={t.badge}>🚚 La livraison de votre quartier, gérée depuis WhatsApp</span>
        <h1 style={t.h1}>Vendez plus.<br />Livrez sans effort.</h1>
        <p style={t.sub}>
          Vos clients commandent par WhatsApp. Un livreur proche est assigné automatiquement,
          vos clients suivent la course en direct. Sans abonnement, crédits à l’usage.
        </p>
        {waLink('Bonjour WAZAP 👋 je veux activer la livraison pour mon commerce.') && (
          <a style={{ ...t.btnGhost, marginRight: 8 }} href={waLink('Bonjour WAZAP 👋 je veux activer la livraison pour mon commerce.')!} target="_blank" rel="noreferrer">Discuter sur WhatsApp</a>
        )}
        <a style={{ ...t.btnGhost, background: '#15221b' }} href="#inscription">Je suis intéressé(e)</a>
        <div style={t.chips}>
          {['⚡ Activation en 30 s', '✅ 15 premières commandes offertes', '💳 Paiement Mobile Money'].map((c) => <span key={c} style={t.chip}>{c}</span>)}
        </div>
      </section>

      <section style={{ ...t.card, ...t.offer }}>
        <p style={{ margin: 0, fontWeight: 700, letterSpacing: 1, fontSize: 13 }}>OFFRE DE LANCEMENT</p>
        <p style={t.offerTitle}>Vos 15 premières commandes de livraison offertes</p>
        <p style={{ margin: 0, fontSize: 14, opacity: 0.92 }}>Découvrez le service sans risque, sans abonnement, sans engagement.</p>
      </section>

      <section style={{ padding: '6px 0' }}>
        <h2 style={{ margin: '0 0 6px' }}>Comment ça marche ?</h2>
        <div style={t.steps}>
          {[
            ['Vos clients commandent', 'Ils envoient un message WhatsApp : « 2 poulets braisés à Marcory » — la commande arrive chez vous instantanément.'],
            ['Vous validez d’un clic', 'Confirmez la commande, et le livreur le plus proche reçoit la course automatiquement.'],
            ['Livré et suivi en direct', 'Vos clients voient arriver leur livreur sur une carte et reçoivent la confirmation de livraison.'],
          ].map(([title, desc], i) => (
            <div key={title} style={t.step}>
              <span style={t.num}>{i + 1}</span>
              <div>
                <strong>{title}</strong>
                <div style={t.muted}>{desc}</div>
              </div>
            </div>
          ))}
        </div>
      </section>

      <section style={t.card}>
        <h2 style={{ margin: '0 0 10px' }}>Pourquoi WAZAP ?</h2>
        <div style={t.grid}>
          {AVANTAGES.map(([ico, title, desc]) => (
            <div key={title} style={t.item}>
              <div style={{ fontSize: 20 }}>{ico}</div>
              <strong>{title}</strong>
              <div style={{ color: '#5d7164', fontSize: 13, marginTop: 4 }}>{desc}</div>
            </div>
          ))}
        </div>
      </section>

      <section style={t.card}>
        <h2 style={{ margin: '0 0 10px' }}>Zones desservies</h2>
        <div style={t.chips}>{ZONES.map((z) => <span key={z} style={t.chip}>{z}</span>)}</div>
        <p style={t.muted}>D’autres communes arrivent — laissez-nous vos coordonnées pour être prévenu.</p>
      </section>


      <section id="inscription" style={t.card}>
        {done ? (
          <div>
            <h2 style={{ marginTop: 0 }}>✅ C’est noté !</h2>
            <p style={t.ok}>Merci {contact || business} ! Un membre de l’équipe WAZAP vous contacte sur WhatsApp très vite pour activer vos <b>15 premières commandes offertes</b>.</p>
            {waLink(`Bonjour WAZAP 👋 Je viens de m’inscrire (${business}). Je veux activer la livraison.`) && (
              <a style={t.btnGhost} href={waLink(`Bonjour WAZAP 👋 Je viens de m’inscrire (${business}). Je veux activer la livraison.`)!} target="_blank" rel="noreferrer">💬 Nous écrire maintenant sur WhatsApp</a>
            )}
            <button style={{ ...t.btn, ...t.btnGrey }} onClick={() => { setDone(false); setBusiness(''); setContact(''); setPhone(''); setZone('') }}>Ajouter un autre commerce</button>
          </div>
        ) : (
          <div>
            <h2 style={{ marginTop: 0 }}>Activer la livraison</h2>
            <p style={t.muted}>Laissez vos coordonnées — rappel WhatsApp sous 24 h.</p>
            <label style={t.label}>Nom du commerce *</label>
            <input style={t.input} value={business} onChange={(e) => setBusiness(e.target.value)} placeholder="Ex : Restaurant Chez Awa" />
            <label style={t.label}>Quartier / commune *</label>
            <input style={t.input} value={zone} onChange={(e) => setZone(e.target.value)} placeholder="Ex : Marcory, rue Princesse" list="zones" />
            <datalist id="zones">{ZONES.map((z) => <option key={z} value={z} />)}</datalist>
            <label style={t.label}>Numéro WhatsApp *</label>
            <input style={t.input} value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="Ex : 07 08 09 10 11" inputMode="tel" />
            <label style={t.label}>Votre prénom / nom (facultatif)</label>
            <input style={t.input} value={contact} onChange={(e) => setContact(e.target.value)} placeholder="Ex : Awa Koné" />
            {error && <div style={t.err}>{error}</div>}
            <button style={{ ...t.btn, opacity: busy ? 0.6 : 1 }} disabled={busy} onClick={submit}>
              {busy ? 'Envoi…' : 'Recevoir mon activation gratuite'}
            </button>
            <p style={t.small}>Sans engagement — nous ne partageons jamais vos coordonnées.</p>
          </div>
        )}
      </section>

      <footer style={t.footer}>⚡ WAZAP — La livraison en un éclair, directement dans WhatsApp.</footer>
    </div>
  )
}

