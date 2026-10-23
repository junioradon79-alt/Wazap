import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import VentePage from './VentePage'

const mocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))

vi.mock('../api/client', () => ({ api: { get: mocks.get, post: mocks.post } }))

describe('VentePage', () => {
  beforeEach(() => {
    mocks.get.mockReset()
    mocks.post.mockReset()
    mocks.get.mockResolvedValue({ whatsappNumber: '+2250104320317' })
  })

  it('présente une promesse claire, l’offre découverte et le portrait héro', async () => {
    render(<MemoryRouter><VentePage /></MemoryRouter>)

    expect(await screen.findByRole('heading', { name: /Votre commerce livre/i })).toBeInTheDocument()
    expect(screen.getByRole('img', { name: /Commerçante souriante utilisant WAZAP/i })).toBeInTheDocument()
    expect(screen.getAllByText(/15 livraisons offertes/i).length).toBeGreaterThanOrEqual(2)
    expect(screen.getByText(/Trois étapes. Votre vente continue d’avancer./i)).toBeInTheDocument()
  })

  it('oriente les livreurs vers leur page dédiée', async () => {
    render(<MemoryRouter><VentePage /></MemoryRouter>)

    expect(await screen.findByRole('link', { name: /Je suis livreur/i })).toHaveAttribute('href', '/livreurs')
  })

  it('déploie une réponse de la FAQ', async () => {
    const user = userEvent.setup()
    render(<MemoryRouter><VentePage /></MemoryRouter>)

    await user.click(await screen.findByText(/Mes clients doivent-ils installer une application/i))
    expect(screen.getByText(/Vos clients continuent de commander naturellement par WhatsApp/i)).toBeInTheDocument()
  })

  it('valide les champs obligatoires du formulaire d’activation', async () => {
    const user = userEvent.setup()
    render(<MemoryRouter><VentePage /></MemoryRouter>)

    await user.click(await screen.findByRole('button', { name: /Valider et recevoir mes 15 courses gratuites/i }))
    expect(await screen.findByText(/Indiquez le nom de votre commerce/i)).toBeInTheDocument()
  })

  it('envoie le formulaire avec succès et affiche la confirmation', async () => {
    const user = userEvent.setup()
    mocks.post.mockResolvedValue({ leadId: 'lead-123' })
    render(<MemoryRouter><VentePage /></MemoryRouter>)

    await user.type(await screen.findByLabelText(/Nom de votre commerce ou boutique/i), 'Restaurant Chez Awa')
    await user.type(screen.getByLabelText(/Numéro WhatsApp joignable/i), '+2250701020304')
    await user.click(screen.getByRole('button', { name: /Valider et recevoir mes 15 courses gratuites/i }))

    await waitFor(() => expect(mocks.post).toHaveBeenCalledWith('/public/leads', expect.objectContaining({
      businessName: 'Restaurant Chez Awa',
      whatsappNumber: '+2250701020304',
      zone: 'Marcory',
    })))
    expect(await screen.findByText(/Félicitations Restaurant Chez Awa/i)).toBeInTheDocument()
  })
})
