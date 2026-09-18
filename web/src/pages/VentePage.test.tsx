import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import VentePage from './VentePage'

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

describe('VentePage (Landing page vitrine ultra-premium WAZAP)', () => {
  beforeEach(() => {
    mocks.get.mockReset()
    mocks.post.mockReset()
    mocks.get.mockResolvedValue({ whatsappNumber: '+2250104320317' })
  })

  it('affiche le Hero, les badges de réassurance et le mockup WhatsApp', async () => {
    render(
      <MemoryRouter>
        <VentePage />
      </MemoryRouter>
    )

    expect(await screen.findByText(/LE YANGO DE LA MARCHANDISE À ABIDJAN/)).toBeInTheDocument()
    expect(screen.getByText(/100% sur WhatsApp./)).toBeInTheDocument()
    expect(screen.getAllByText(/15 livraisons offertes/).length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText(/WAZAP Livraison/)).toBeInTheDocument()
    expect(screen.getByText(/Ibrahim K. \(Yamaha YBR\)/)).toBeInTheDocument()
  })

  it('permet de basculer entre l’offre Commerçant et l’offre Livreur', async () => {
    render(
      <MemoryRouter>
        <VentePage />
      </MemoryRouter>
    )

    const riderTab = screen.getByRole('tab', { name: /Vous êtes Livreur Indépendant/ })
    await userEvent.click(riderTab)

    expect(await screen.findByText(/POUR LES LIVREURS INDÉPENDANTS/)).toBeInTheDocument()
    expect(screen.getByText(/100% des frais de course pour vous/)).toBeInTheDocument()

    const vendorTab = screen.getByRole('tab', { name: /Vous êtes Commerçant \/ Vendeur/ })
    await userEvent.click(vendorTab)

    expect(await screen.findByText(/POUR LES COMMERÇANTS ET RESTAURATEURS/)).toBeInTheDocument()
    expect(screen.getByText(/Votre client commande/)).toBeInTheDocument()
  })

  it('permet de sélectionner une commune sur le radar d’Abidjan', async () => {
    render(
      <MemoryRouter>
        <VentePage />
      </MemoryRouter>
    )

    const cocodyBtn = screen.getByRole('button', { name: /📍 Cocody/ })
    await userEvent.click(cocodyBtn)

    expect(screen.getByText('56+')).toBeInTheDocument()
  })

  it('calcule la rentabilité et le pack conseillé via le simulateur', async () => {
    render(
      <MemoryRouter>
        <VentePage />
      </MemoryRouter>
    )

    // 15 courses/jour * 26 jours ouvrés = 390 courses/mois -> Pack Grand (220 courses)
    expect(await screen.findByText('Pack Grand')).toBeInTheDocument()
    expect(screen.getByText(/220 courses/)).toBeInTheDocument()
  })

  it('valide les champs obligatoires du formulaire d’activation', async () => {
    render(
      <MemoryRouter>
        <VentePage />
      </MemoryRouter>
    )

    const submitBtn = screen.getByRole('button', { name: /Valider et recevoir mes 15 courses gratuites/ })
    await userEvent.click(submitBtn)

    expect(await screen.findByText(/Indiquez le nom de votre commerce./)).toBeInTheDocument()
  })

  it('envoie le formulaire avec succès et affiche l’écran de félicitations', async () => {
    mocks.post.mockResolvedValue({ leadId: 'lead-123' })

    render(
      <MemoryRouter>
        <VentePage />
      </MemoryRouter>
    )

    const businessInput = screen.getByLabelText(/Nom de votre commerce/)
    await userEvent.type(businessInput, 'Restaurant Chez Awa')

    const phoneInput = screen.getByLabelText(/Numéro WhatsApp joignable/)
    await userEvent.type(phoneInput, '+2250701020304')

    const submitBtn = screen.getByRole('button', { name: /Valider et recevoir mes 15 courses gratuites/ })
    await userEvent.click(submitBtn)

    await waitFor(() => {
      expect(mocks.post).toHaveBeenCalledWith('/public/leads', expect.objectContaining({
        businessName: 'Restaurant Chez Awa',
        whatsappNumber: '+2250701020304',
        zone: 'Marcory',
      }))
    })

    expect(await screen.findByText(/Félicitations Restaurant Chez Awa !/)).toBeInTheDocument()
    expect(screen.getByText(/Ouvrir la conversation WhatsApp prioritaire/)).toBeInTheDocument()
  })
})
