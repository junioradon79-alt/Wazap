import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import SuiviPage from './SuiviPage'
import type { ClientOrderStatus } from '../api/types'

const mocks = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
}))

vi.mock('../api/client', () => ({
  api: {
    get: mocks.get,
    post: mocks.post,
  },
}))

function renderWithRoute(orderId = 'order-123') {
  return render(
    <MemoryRouter initialEntries={[`/suivi/${orderId}`]}>
      <Routes>
        <Route path="/suivi/:id" element={<SuiviPage />} />
      </Routes>
    </MemoryRouter>
  )
}

describe('SuiviPage (Suivi client en direct)', () => {
  beforeEach(() => {
    mocks.get.mockReset()
    mocks.post.mockReset()
  })

  it('affiche l’état de chargement initial puis les détails de la commande', async () => {
    const mockOrder: ClientOrderStatus = {
      id: 'order-123',
      code: '8A9B1C2D',
      vendorName: 'Boutique Wax Ivoire',
      status: 'AwaitingRiderAcceptance',
      description: '2x Robes Africaines',
      amount: 25000,
      needsCoordinates: false,
      hasCoordinates: true,
      address: 'Cocody Angré 8ème tranche',
      riderAssigned: false,
      delivered: false,
      payment: null,
    }

    mocks.get.mockResolvedValueOnce(mockOrder)

    renderWithRoute()

    expect(screen.getByText(/Connexion au suivi WAZAP en direct…/)).toBeInTheDocument()

    expect(await screen.findByText(/COMMANDE #8A9B1C2D/)).toBeInTheDocument()
    expect(screen.getByText(/Recherche d’un livreur…/)).toBeInTheDocument()
    expect(screen.getAllByText(/25.*000.*FCFA/).length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText(/Boutique Wax Ivoire/).length).toBeGreaterThanOrEqual(1)
  })

  it('affiche le formulaire de géolocalisation si les coordonnées sont requises', async () => {
    const mockOrder: ClientOrderStatus = {
      id: 'order-need-geo',
      code: 'GEO12345',
      vendorName: 'Pâtisserie Abidjan',
      status: 'VendorConfirmed',
      description: 'Gâteau d’anniversaire',
      amount: 15000,
      needsCoordinates: true,
      hasCoordinates: false,
      address: null,
      riderAssigned: false,
      delivered: false,
      payment: null,
    }

    mocks.get.mockResolvedValueOnce(mockOrder)

    renderWithRoute('order-need-geo')

    expect(await screen.findByText(/Où souhaitez-vous être livré \?/)).toBeInTheDocument()
    expect(screen.getByText(/Détecter ma position GPS exacte/)).toBeInTheDocument()
    expect(screen.getByText(/Confirmer et lancer la livraison ⚡/)).toBeInTheDocument()
  })

  it('affiche la Garantie Colis Sûr (Scan 1-Tap) et le profil du livreur sans code PIN', async () => {
    const mockOrder: ClientOrderStatus = {
      id: 'order-with-security',
      code: 'SEC77889',
      vendorName: 'Electro Abidjan',
      status: 'InTransit',
      description: 'Écouteurs sans fil',
      amount: 18000,
      riderPhone: '+2250700000002',
      needsCoordinates: false,
      hasCoordinates: true,
      address: 'Marcory Zone 4',
      riderAssigned: true,
      delivered: false,
      payment: null,
    }

    mocks.get.mockImplementation(async (url: string) => {
      if (url.includes('/rider-location')) {
        return {
          tracking: true,
          riderName: 'Mamadou Touré',
          location: { latitude: 5.3012, longitude: -4.0125 },
        }
      }
      return mockOrder
    })

    renderWithRoute('order-with-security')

    expect(await screen.findByText(/Garantie Colis Sûr WAZAP/)).toBeInTheDocument()
    expect(screen.getByText(/Remise 1-Tap Sécurisée • Zéro Espèces sur la Marchandise/)).toBeInTheDocument()
    expect(screen.getByText(/Validation instantanée 1-tap • Aucun code PIN nécessaire/)).toBeInTheDocument()
    expect(screen.getByText(/Paiement 100% Digital par QR Code Universel/)).toBeInTheDocument()

    // Vérification du livreur
    expect((await screen.findAllByText(/Mamadou Touré/)).length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText(/Pourboire au livreur \(Wave \/ OM\)/)).toBeInTheDocument()
  })

  it('permet de noter le livreur et affiche le Reçu Officiel lorsque le colis est livré', async () => {
    const mockOrder: ClientOrderStatus = {
      id: 'order-delivered',
      code: 'DELIV999',
      vendorName: 'Mode & Style',
      status: 'Delivered',
      description: 'Sac en cuir',
      amount: 30000,
      deliveryFee: 1500,
      totalAmount: 31500,
      hasProofPhoto: true,
      needsCoordinates: false,
      hasCoordinates: true,
      address: 'Plateau',
      riderAssigned: true,
      delivered: true,
      payment: {
        status: 'Completed',
        amount: 31500,
        paymentLink: null,
      },
    }

    mocks.get.mockResolvedValue(mockOrder)
    mocks.post.mockResolvedValue({ success: true, score: 5, comment: 'Livreur au top' })

    renderWithRoute('order-delivered')

    expect(await screen.findByText(/Livré ✓/)).toBeInTheDocument()
    expect(screen.getByText(/Preuve Photo de Livraison/)).toBeInTheDocument()
    expect(screen.getByText(/REÇU OFFICIEL WAZAP/)).toBeInTheDocument()
    expect(screen.getByText(/LIVRÉ & SCELLÉ ✓/)).toBeInTheDocument()
    expect(screen.getByText(/Imprimer ou Enregistrer le Reçu \(PDF\)/)).toBeInTheDocument()
    expect(screen.getByText(/Partager ce Reçu sur WhatsApp/)).toBeInTheDocument()
    expect(screen.getByText(/Notez votre livraison/)).toBeInTheDocument()
    expect(screen.getByText(/Commande payée par Mobile Money/)).toBeInTheDocument()

    // Clic sur puce tag rapide
    const fastChip = screen.getByRole('button', { name: /⚡ Rapide & efficace/ })
    await userEvent.click(fastChip)

    // Soumission de la note
    const submitBtn = screen.getByRole('button', { name: /Envoyer mon avis ⭐/ })
    await userEvent.click(submitBtn)

    await waitFor(() => {
      expect(mocks.post).toHaveBeenCalledWith('/client/orders/order-delivered/rate', {
        score: 5,
        comment: '⚡ Rapide & efficace',
      })
    })

    expect(await screen.findByText(/Merci pour votre avis !/)).toBeInTheDocument()
  })
})
