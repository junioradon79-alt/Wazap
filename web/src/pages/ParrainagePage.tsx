import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'

const t = {
  wrap: { maxWidth: 700, margin: '0 auto', padding: '0 18px 60px', fontFamily: 'system-ui, sans-serif', color: '#15221b', lineHeight: 1.55 } as const,
  header: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '14px 0' } as const,
  logo: { fontWeight: 800, fontSize: 20 } as const,
  card: { background: '#fff', border: '1px solid #e3e9e4', borderRadius: 16, padding: 20, marginTop: 16 } as const,
  hero: { textAlign: 'center' as const, padding: '20px 4px 6px' },
  badge: { display: 'inline-block', background: '#e7f6ee', color: '#0e7a3e', fontWeight: 700, fontSize: 13, borderRadius: 999, padding: '6px 14px' },
  h1: { fontSize: 28, margin: '14px 0 8px' } as const,
  sub: { color: '#48604f', margin: '0 0 16px', fontSize: 16 },
  muted: { color: '#5d7164', fontSize: 14 },
  step: { display: 'flex', gap: 12, alignItems: 'flex-start', marginBottom: 12 } as const,
  num: { minWidth: 30, height: 30, borderRadius: '50%', background: '#e7f6ee', color: '#0e7a3e', fontWeight: 800, display: 'flex', alignItems: 'center', justifyContent: 'center' } as const,
  bonus: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(220px,1fr))', gap: 10 } as const,
  item: { background: '#f6f9f7', borderRadius: 12, padding: 14 } as const,
  btn: { display: 'inline-block', background: '#07a04b', color: '#fff', textDecoration: 'none', fontWeight: 800, padding: '13px 20px', borderRadius: 12, fontSize: 15, margin: '4px 8px 0 0' },
  btnDark: { background: '#15221b' },
}

export default function ParrainagePage() {
  const [whatsapp, setWhatsapp] = useState('')

  useEffect(() => {
    api.get<{ whatsappNumber: string }>('/public/sales/config')
      .then((c) => setWhatsapp(c.whatsappNumber))
      .catch(() => setWhatsapp(''))
  }, [])

  const wa = whatsapp
    ? `https://wa.me/${whatsapp.replace(/[^\d]/g, '').replace(/^0+/, '')}?text=${encodeURIComponent('Bonjour WAZAP 👋 je veux parrainer des commerçants.')}`
    : null

  return (
    <div style={t.wrap}>
      <div style={t.header}>
        <div style={t.logo}>⚡ WAZAP</div>
        <Link style={{ color: '#0e7a3e', fontWeight: 700, textDecoration: 'none', fontSize: 14 }} to="/vente">La page de vente →</Link>
      </div>

      <section style={t.hero}>
        <span style={t.badge}>🎁 PROGRAMME PARRAINAGE</span>
        <h1 style={t.h1}>Parrainez un commerce,<br />gagnez des crédits.</h1>
        <p style={t.sub}>Chaque commerçant qui s’inscrit grâce à vous fait gagner <b>+5 crédits</b> sur votre compte — et lui reçoit ses <b>15 premières commandes offertes</b>.</p>
      </section>

      <section style={t.card}>
        <h2 style={{ margin: '0 0 10px' }}>Comment ça marche ?</h2>
        {[
          ['Partagez votre code', 'Votre code parrainage WAZAP (ex. WA-4K2M) se trouve dans votre espace vendeur, sous « Parrainage ».'],
          ['Votre filleul s’inscrit', 'Il commande sur la page de vente ou s’inscrit directement via le numéro WhatsApp, en donnant votre code.'],
          ['Vous recevez +5 crédits', 'Dès son inscription confirmée, 5 crédits sont crédités sur votre compte — utilisables pour vos propres livraisons.'],
        ].map(([title, desc], i) => (
          <div key={title} style={t.step}>
            <span style={t.num}>{i + 1}</span>
            <div>
              <strong>{title}</strong>
              <div style={t.muted}>{desc}</div>
            </div>
          </div>
        ))}
      </section>

      <section style={t.card}>
        <h2 style={{ margin: '0 0 10px' }}>Pourquoi parrainer ?</h2>
        <div style={t.bonus}>
          <div style={t.item}>
            <div style={{ fontSize: 22 }}>👥</div>
            <strong>Des crédits gratuits</strong>
            <div style={t.muted}>+5 crédits par filleul inscrit, sans limite. Plus vous parrainez, plus vous livrez gratuitement.</div>
          </div>
          <div style={t.item}>
            <div style={{ fontSize: 22 }}>🏘️</div>
            <strong>Un quartier qui vit</strong>
            <div style={t.muted}>Vos voisins commerçants livrent aussi : plus de clients habitués à la livraison dans votre zone.</div>
          </div>
          <div style={t.item}>
            <div style={{ fontSize: 22 }}>🎁</div>
            <strong>Un filleul gagnant</strong>
            <div style={t.muted}>Il démarre avec 15 commandes offertes : zéro risque pour tester la livraison.</div>
          </div>
        </div>
      </section>

      <section style={t.card}>
        {wa && <a style={t.btn} href={wa} target="_blank" rel="noreferrer">💬 Demander mon code / parrainer</a>}
        <Link style={{ ...t.btn, ...t.btnDark }} to="/vente">➡️ Recommander la page à un commerçant</Link>
        <p style={t.muted}>Le commerçant recommandé n’a qu’à remplir le formulaire — votre code vous sera demandé à la suite par l’équipe WAZAP.</p>
      </section>

      <footer style={{ textAlign: 'center', color: '#8a988f', fontSize: 13, marginTop: 28 }}>⚡ WAZAP — Vendez plus, livrez sans effort.</footer>
    </div>
  )
}
