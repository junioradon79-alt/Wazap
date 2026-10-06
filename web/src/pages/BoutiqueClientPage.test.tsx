import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import BoutiqueClientPage from './BoutiqueClientPage'
import type { PublicShopDto } from '../api/types'

const mocks = vi.hoisted(() => ({
  get: vi.fn(),
}))

vi.mock('../api/client', () => ({
  api: {
    get: mocks.get,
  },
}))

function renderWithRoute(identifier = 'demo') {
  return render(
    <MemoryRouter initialEntries={[`/b/${identifier}`]}>
      <Routes>
        <Route path="/b/:identifier" element={<BoutiqueClientPage />} />
      </Routes>
    </MemoryRouter>
  )
}

describe('BoutiqueClientPage (Mini-Vitrine Marchande 1-Tap)', () => {
  const mockShop: PublicShopDto = {
    vendorId: 'vendor-123',
    shopName: 'Boutique Wax Ivoire',
    phone: '2250544051972',
    zone: 'Cocody Deux-Plateaux',
    products: [
      {
        id: 'prod-1',
        name: 'Robe Wax Royale',
        price: 20000,
        emoji: '👗',
        description: 'Tissu 100% coton véritable',
        isAvailable: true,
      },
      {
        id: 'prod-2',
        name: 'Sacoche Cuir Artisanale',
        price: 15000,
        emoji: '👜',
        description: 'Cuir véritable teinté main',
        isAvailable: true,
      },
    ],
  }

  beforeEach(() => {
    mocks.get.mockReset()
  })

  it('affiche la vitrine marchande et ses articles disponibles', async () => {
    mocks.get.mockResolvedValueOnce(mockShop)

    renderWithRoute('boutique-wax')

    expect(screen.getByText(/Chargement de la vitrine marchande WAZAP…/)).toBeInTheDocument()

    expect(await screen.findByText('Boutique Wax Ivoire')).toBeInTheDocument()
    expect(screen.getByText(/Cocody Deux-Plateaux/)).toBeInTheDocument()
    expect(screen.getByText(/Partenaire Vérifié Colis Sûr/)).toBeInTheDocument()
    expect(screen.getByText('Robe Wax Royale')).toBeInTheDocument()
    expect(screen.getByText('Sacoche Cuir Artisanale')).toBeInTheDocument()
    expect(screen.getByText('20 000 FCFA')).toBeInTheDocument()
    expect(screen.getByText('15 000 FCFA')).toBeInTheDocument()
  })

  it('permet d’ajouter des articles au panier et calcule les montants', async () => {
    mocks.get.mockResolvedValueOnce(mockShop)

    renderWithRoute('boutique-wax')

    await screen.findByText('Boutique Wax Ivoire')

    // Panier invisible avant ajout
    expect(screen.queryByText(/Votre Panier/)).not.toBeInTheDocument()

    // Clic sur '+' pour Robe Wax Royale
    const addRobeBtn = screen.getByRole('button', { name: /Ajouter Robe Wax Royale/ })
    await userEvent.click(addRobeBtn)

    // Panier maintenant visible avec 1 article
    expect(await screen.findByText(/Votre Panier \(1 articles\)/)).toBeInTheDocument()
    expect(screen.getAllByText(/20.*000.*FCFA/).length).toBeGreaterThanOrEqual(2)

    // Clic une 2ème fois sur '+'
    await userEvent.click(addRobeBtn)
    expect(await screen.findByText(/Votre Panier \(2 articles\)/)).toBeInTheDocument()

    // Sous-total = 40 000 FCFA, Livraison Cocody = 1 000 FCFA => Total = 41 000 FCFA
    expect(screen.getByText(/40.*000.*FCFA/)).toBeInTheDocument()
    expect(screen.getByText(/41.*000.*FCFA/)).toBeInTheDocument()
  })

  it('permet de changer de commune et met à jour le tarif de livraison et le lien WhatsApp', async () => {
    mocks.get.mockResolvedValueOnce(mockShop)

    renderWithRoute('boutique-wax')

    await screen.findByText('Boutique Wax Ivoire')

    const addRobeBtn = screen.getByRole('button', { name: /Ajouter Robe Wax Royale/ })
    await userEvent.click(addRobeBtn)

    // Par défaut Cocody : 1 000 F => Total = 21 000 FCFA
    expect(screen.getByText(/21.*000.*FCFA/)).toBeInTheDocument()

    // Clic sur la commune 'Yopougon' (2 000 FCFA)
    const yopBtn = screen.getByRole('button', { name: /Yopougon/ })
    await userEvent.click(yopBtn)

    // Total = 20 000 + 2 000 = 22 000 FCFA
    expect(screen.getByText(/22.*000.*FCFA/)).toBeInTheDocument()

    // Vérification du bouton WhatsApp
    const waLink = screen.getByRole('link', { name: /Envoyer ma commande sur WhatsApp/ })
    expect(waLink).toBeInTheDocument()
    const href = waLink.getAttribute('href') || ''
    expect(href).toContain('https://wa.me/2250544051972?text=')
    const decoded = decodeURIComponent(href)
    expect(decoded).toContain('NOUVELLE COMMANDE SUR VITRINE WAZAP')
    expect(decoded).toContain('Robe Wax Royale')
    expect(decoded).toContain('Yopougon')
    expect(decoded).toMatch(/22.*000.*FCFA/)
  })

  it('affiche un message clair si la boutique est introuvable', async () => {
    mocks.get.mockRejectedValueOnce(new Error('Not found'))

    renderWithRoute('inconnu')

    expect(await screen.findByText('Boutique introuvable')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Retour à l’accueil WAZAP/ })).toBeInTheDocument()
  })
})
