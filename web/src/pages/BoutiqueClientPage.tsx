import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'
import type { PublicShopDto, PublicShopProduct } from '../api/types'
import BrandLogo from '../components/BrandLogo'
import '../styles/boutique.css'

interface CartItem {
  product: PublicShopProduct
  quantity: number
}

const ABIDJAN_COMMUNES = [
  { name: 'Cocody', fee: 1000, desc: 'Centre / Nord' },
  { name: 'Plateau', fee: 1000, desc: 'Centre affaires' },
  { name: 'Marcory', fee: 1500, desc: 'Zone Sud' },
  { name: 'Treichville', fee: 1500, desc: 'Zone Sud' },
  { name: 'Koumassi', fee: 1500, desc: 'Zone Sud' },
  { name: 'Yopougon', fee: 2000, desc: 'Grand Ouest' },
  { name: 'Abobo', fee: 2000, desc: 'Grand Nord' },
  { name: 'Port-Bouët', fee: 2000, desc: 'Littoral / Aéroport' },
  { name: 'Bingerville', fee: 2000, desc: 'Périphérie Est' },
  { name: 'Adjamé', fee: 1500, desc: 'Centre commercial' },
]

const TIME_SLOTS = [
  { id: 'immediate', label: '⚡ Immédiat (~30 min)' },
  { id: 'afternoon', label: '🕒 Cet après-midi' },
  { id: 'evening', label: '🌙 En soirée (17h-19h)' },
]

const INSTRUCTIONS = [
  '🚪 Appeler au portail',
  '👮 Laisser au gardien',
  '🏢 Monter à l’étage / bureau',
  '📞 Appeler 10 min avant',
]

export default function BoutiqueClientPage() {
  const { identifier } = useParams<{ identifier: string }>()
  const [shop, setShop] = useState<PublicShopDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Panier
  const [cart, setCart] = useState<Record<string, number>>({})
  const [selectedCommune, setSelectedCommune] = useState(ABIDJAN_COMMUNES[0])
  const [selectedSlot, setSelectedSlot] = useState(TIME_SLOTS[0].label)
  const [selectedInstruction, setSelectedInstruction] = useState<string>(INSTRUCTIONS[0])

  useEffect(() => {
    let isMounted = true
    const fetchShop = async () => {
      try {
        setLoading(true)
        setError(null)
        const target = identifier || 'demo'
        const res = await api.get<PublicShopDto>(`/vendors/public/${target}`)
        if (isMounted) {
          setShop(res)
        }
      } catch (err: any) {
        if (isMounted) {
          setError(err?.response?.data?.message || 'Boutique introuvable ou momentanément indisponible.')
        }
      } finally {
        if (isMounted) {
          setLoading(false)
        }
      }
    }
    fetchShop()
    return () => {
      isMounted = false
    }
  }, [identifier])

  const updateQuantity = (productId: string, delta: number) => {
    setCart((prev) => {
      const current = prev[productId] || 0
      const next = Math.max(0, current + delta)
      const copy = { ...prev }
      if (next === 0) {
        delete copy[productId]
      } else {
        copy[productId] = next
      }
      return copy
    })
  }

  // Calculs du panier
  const cartItems: CartItem[] = useMemo(() => {
    if (!shop) return []
    return Object.entries(cart)
      .map(([id, qty]) => {
        const prod = shop.products.find((p) => p.id === id)
        return prod ? { product: prod, quantity: qty } : null
      })
      .filter((item): item is CartItem => item !== null)
  }, [cart, shop])

  const itemsSubtotal = useMemo(() => {
    return cartItems.reduce((acc, item) => acc + item.product.price * item.quantity, 0)
  }, [cartItems])

  const deliveryFee = selectedCommune.fee
  const totalAmount = itemsSubtotal + (cartItems.length > 0 ? deliveryFee : 0)

  // Message WhatsApp 100% pré-formaté
  const whatsappUrl = useMemo(() => {
    if (!shop || cartItems.length === 0) return '#'
    const rawPhone = shop.phone.replace(/[^0-9]/g, '')
    const targetPhone = rawPhone.startsWith('225') ? rawPhone : `225${rawPhone}`

    const lines = [
      `*NOUVELLE COMMANDE SUR VITRINE WAZAP* 🛍️`,
      `Boutique : ${shop.shopName}`,
      ``,
      `📦 *Articles commandés :*`,
      ...cartItems.map(
        (ci) => `• ${ci.quantity}x ${ci.product.name} — ${(ci.product.price * ci.quantity).toLocaleString('fr-FR')} FCFA`
      ),
      ``,
      `💵 *Sous-total articles :* ${itemsSubtotal.toLocaleString('fr-FR')} FCFA`,
      `🛵 *Livraison WAZAP :* ${deliveryFee.toLocaleString('fr-FR')} FCFA (${selectedCommune.name})`,
      `💰 *TOTAL À RÉGLER :* ${totalAmount.toLocaleString('fr-FR')} FCFA`,
      ``,
      `📍 *Commune :* ${selectedCommune.name} (${selectedCommune.desc})`,
      `⏰ *Créneau :* ${selectedSlot}`,
      `📝 *Consigne :* ${selectedInstruction}`,
      ``,
      `🛡️ *Règlement par QR Code Universel Colis Sûr à la livraison (Wave, OM, MTN, Moov).*`,
      `Merci de confirmer la disponibilité pour que le livreur prenne la course ! ⚡`,
    ]

    return `https://wa.me/${targetPhone}?text=${encodeURIComponent(lines.join('\n'))}`
  }, [shop, cartItems, itemsSubtotal, deliveryFee, totalAmount, selectedCommune, selectedSlot, selectedInstruction])

  if (loading) {
    return (
      <div className="btq-page">
        <div className="btq-container" style={{ textAlign: 'center', paddingTop: 80 }}>
          <div className="loading__spinner" style={{ margin: '0 auto 16px', width: 40, height: 40 }} />
          <p style={{ color: '#9cb3a8', fontSize: 15 }}>Chargement de la vitrine marchande WAZAP…</p>
        </div>
      </div>
    )
  }

  if (error || !shop) {
    return (
      <div className="btq-page">
        <div className="btq-container" style={{ paddingTop: 60 }}>
          <div className="btq-empty-box">
            <span style={{ fontSize: 44, display: 'block', marginBottom: 12 }}>🏪</span>
            <h2 style={{ fontSize: 20, color: '#fff', marginBottom: 8 }}>Boutique introuvable</h2>
            <p style={{ color: '#9cb3a8', fontSize: 14, marginBottom: 20 }}>
              {error || "Cette vitrine n'est pas encore activée ou le lien est incomplet."}
            </p>
            <Link
              to="/app/suivi"
              style={{
                display: 'inline-block',
                background: 'rgba(0, 214, 108, 0.15)',
                border: '1px solid #00d66c',
                color: '#00d66c',
                padding: '10px 18px',
                borderRadius: 12,
                fontWeight: 700,
                textDecoration: 'none',
                fontSize: 14,
              }}
            >
              Retour à l’accueil WAZAP
            </Link>
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="btq-page">
      <div className="btq-container">
        {/* EN-TÊTE DE LA VITRINE */}
        <header className="btq-header">
          <div className="btq-header-top">
            <div className="btq-badge-brand">
              <div className="btq-logo-dot">W</div>
              <span className="btq-brand-label">VITRINE OFFICIELLE</span>
            </div>
            <span className="btq-verified-pill">
              <span>✓</span> Partenaire Vérifié Colis Sûr
            </span>
          </div>

          <h1 className="btq-shop-name">{shop.shopName}</h1>
          <div className="btq-shop-zone">
            <span>📍 {shop.zone || 'Grand Abidjan'}</span>
            <span>•</span>
            <span style={{ color: '#00d66c' }}>Livraison express en 30 min</span>
          </div>

          <div className="btq-assurance-bar">
            <span>🛡️</span>
            <span>
              <strong>Garantie Colis Sûr :</strong> Règlement à la livraison exclusivement par Scan du QR Code Universel (Wave, OM, MTN, Moov). Zéro espèce sur la marchandise.
            </span>
          </div>
        </header>

        {/* LISTE DES ARTICLES */}
        <section>
          <div className="btq-section-title">
            <span>Catalogue Produits</span>
            <span className="btq-count-badge">{shop.products.length} articles disponibles</span>
          </div>

          <div className="btq-products-grid">
            {shop.products.map((product) => {
              const qty = cart[product.id] || 0
              return (
                <article key={product.id} className={`btq-product-card ${qty > 0 ? 'has-qty' : ''}`}>
                  <div className="btq-product-img">
                    {product.imageUrl ? (
                      <img src={product.imageUrl} alt={product.name} />
                    ) : (
                      <span>{product.emoji || '📦'}</span>
                    )}
                  </div>

                  <div className="btq-product-info">
                    <div className="btq-product-name">{product.name}</div>
                    {product.description && <div className="btq-product-desc">{product.description}</div>}
                    <div className="btq-product-price">{product.price.toLocaleString('fr-FR')} FCFA</div>
                  </div>

                  <div className="btq-qty-stepper">
                    {qty > 0 && (
                      <button
                        type="button"
                        aria-label={`Diminuer ${product.name}`}
                        className="btq-btn-qty"
                        onClick={() => updateQuantity(product.id, -1)}
                      >
                        -
                      </button>
                    )}
                    {qty > 0 && <span className="btq-qty-val">{qty}</span>}
                    <button
                      type="button"
                      aria-label={`Ajouter ${product.name}`}
                      className="btq-btn-qty plus"
                      onClick={() => updateQuantity(product.id, 1)}
                    >
                      +
                    </button>
                  </div>
                </article>
              )
            })}
          </div>
        </section>

        {/* PANIER ET OPTIONS DE LIVRAISON 1-TAP (SI ARTICLES CHOISIS) */}
        {cartItems.length > 0 && (
          <section className="btq-cart-card">
            <div className="btq-cart-head">
              <div className="btq-cart-title">
                <span>🛒</span>
                <span>Votre Panier ({cartItems.reduce((acc, i) => acc + i.quantity, 0)} articles)</span>
              </div>
              <button
                type="button"
                onClick={() => setCart({})}
                style={{
                  background: 'none',
                  border: 'none',
                  color: '#f87171',
                  fontSize: 12,
                  cursor: 'pointer',
                  fontWeight: 600,
                }}
              >
                Vider le panier
              </button>
            </div>

            {/* SELECTION COMMUNE D'ABIDJAN 1-TAP */}
            <div>
              <div className="btq-pills-label">📍 Choisissez votre commune de livraison :</div>
              <div className="btq-pills-grid">
                {ABIDJAN_COMMUNES.map((commune) => {
                  const isActive = selectedCommune.name === commune.name
                  return (
                    <button
                      key={commune.name}
                      type="button"
                      className={`btq-pill-btn ${isActive ? 'active' : ''}`}
                      onClick={() => setSelectedCommune(commune)}
                    >
                      <span>{commune.name}</span>
                      <span style={{ fontSize: 11, opacity: 0.8 }}>({commune.fee.toLocaleString('fr-FR')} F)</span>
                    </button>
                  )
                })}
              </div>
            </div>

            {/* SELECTION DU CRÉNEAU 1-TAP */}
            <div>
              <div className="btq-pills-label">⏰ Moment souhaité :</div>
              <div className="btq-pills-grid">
                {TIME_SLOTS.map((slot) => {
                  const isActive = selectedSlot === slot.label
                  return (
                    <button
                      key={slot.id}
                      type="button"
                      className={`btq-pill-btn ${isActive ? 'active' : ''}`}
                      onClick={() => setSelectedSlot(slot.label)}
                    >
                      {slot.label}
                    </button>
                  )
                })}
              </div>
            </div>

            {/* SELECTION CONSIGNE DE LIVRAISON 1-TAP */}
            <div>
              <div className="btq-pills-label">📝 Consigne pour le livreur (1 tap) :</div>
              <div className="btq-pills-grid">
                {INSTRUCTIONS.map((inst) => {
                  const isActive = selectedInstruction === inst
                  return (
                    <button
                      key={inst}
                      type="button"
                      className={`btq-pill-btn ${isActive ? 'active' : ''}`}
                      onClick={() => setSelectedInstruction(inst)}
                    >
                      {inst}
                    </button>
                  )
                })}
              </div>
            </div>

            {/* TOTAL & RÉCAPITULATIF */}
            <div className="btq-summary-box">
              <div className="btq-summary-row">
                <span>Sous-total articles</span>
                <span>{itemsSubtotal.toLocaleString('fr-FR')} FCFA</span>
              </div>
              <div className="btq-summary-row">
                <span>Livraison WAZAP ({selectedCommune.name})</span>
                <span>{deliveryFee.toLocaleString('fr-FR')} FCFA</span>
              </div>
              <div className="btq-summary-row total">
                <span>Total estimé à la livraison</span>
                <span className="btq-summary-total-val">{totalAmount.toLocaleString('fr-FR')} FCFA</span>
              </div>
            </div>

            {/* BOUTON GÉANT COMMANDER SUR WHATSAPP (1-TAP) */}
            <a
              href={whatsappUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="btq-btn-order-wa"
            >
              <span>🚀</span>
              <span>Envoyer ma commande sur WhatsApp (1 tap)</span>
            </a>

            <div className="btq-notice-payment">
              <span>💳</span>
              <span>Paiement 100% sécurisé par QR Code Universel WAZAP lors de la remise en main propre.</span>
            </div>
          </section>
        )}

        {/* PIED DE PAGE DISCRET */}
        <footer style={{ textAlign: 'center', marginTop: 30, opacity: 0.7 }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 6, marginBottom: 4 }}>
            <BrandLogo size="sm" showTagline={false} />
            <span style={{ fontSize: 12, fontWeight: 700, color: '#fff' }}>WAZAP Réseau Logistique Abidjan</span>
          </div>
          <p style={{ margin: 0, fontSize: 11, color: '#9cb3a8' }}>
            0% commission vendeur • Suivi GPS en direct • Colis Sûr certifié
          </p>
        </footer>
      </div>
    </div>
  )
}
