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

  it('affiche le Code Secret de Remise (PIN 4 chiffres) et le profil du livreur', async () => {
    const mockOrder: ClientOrderStatus = {
      id: 'order-with-pin',
      code: 'PIN77889',
      vendorName: 'Electro Abidjan',
      status: 'InTransit',
      description: 'Écouteurs sans fil',
      amount: 18000,
      deliveryCode: '4829',
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

    renderWithRoute('order-with-pin')

    expect(await screen.findByText(/Code Secret de Remise/)).toBeInTheDocument()
    expect(screen.getAllByText('4').length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText('8')).toBeInTheDocument()
    expect(screen.getByText('2')).toBeInTheDocument()
    expect(screen.getByText('9')).toBeInTheDocument()
    expect(screen.getByText(/Garantie Colis Sûr/)).toBeInTheDocument()

    // Vérification du livreur
    expect((await screen.findAllByText(/Mamadou Touré/)).length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText(/Pourboire au livreur \(Wave \/ OM\)/)).toBeInTheDocument()
  })

  it('permet de noter le livreur (1 à 5 étoiles) lorsque le colis est livré', async () => {
    const mockOrder: ClientOrderStatus = {
      id: 'order-delivered',
      code: 'DELIV999',
      vendorName: 'Mode & Style',
      status: 'Delivered',
      description: 'Sac en cuir',
      amount: 30000,
      hasProofPhoto: true,
      needsCoordinates: false,
      hasCoordinates: true,
      address: 'Plateau',
      riderAssigned: true,
      delivered: true,
      payment: {
        status: 'Completed',
        amount: 30000,
        paymentLink: null,
      },
    }

    mocks.get.mockResolvedValue(mockOrder)
    mocks.post.mockResolvedValue({ success: true, score: 5, comment: 'Livreur au top' })

    renderWithRoute('order-delivered')

    expect(await screen.findByText(/Livré ✓/)).toBeInTheDocument()
    expect(screen.getByText(/Preuve Photo de Livraison/)).toBeInTheDocument()
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
