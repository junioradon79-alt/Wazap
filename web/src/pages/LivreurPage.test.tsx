import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import LivreurPage from './LivreurPage'

describe('LivreurPage (Landing page dédiée au recrutement et orientation des livreurs)', () => {
  it('affiche le titre principal, les badges et la promesse 1 000 F net', () => {
    render(
      <MemoryRouter>
        <LivreurPage />
      </MemoryRouter>
    )

    expect(screen.getAllByText(/1 000 FCFA net/).length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText(/minimum par course./)).toBeInTheDocument()
    expect(screen.getByText(/0% de commission. 100% sur WhatsApp./)).toBeInTheDocument()
    expect(screen.getAllByText(/05 44 05 19 72/).length).toBeGreaterThanOrEqual(1)
  })

  it('affiche les 4 paliers tarifaires Grand Abidjan', () => {
    render(
      <MemoryRouter>
        <LivreurPage />
      </MemoryRouter>
    )

    expect(screen.getByText(/Palier 1 · Intra-commune/)).toBeInTheDocument()
    expect(screen.getByText(/Palier 2 · Communes voisines/)).toBeInTheDocument()
    expect(screen.getByText(/Palier 3 · Traversée \/ Pont/)).toBeInTheDocument()
    expect(screen.getByText(/Palier 4 · Périphérie express/)).toBeInTheDocument()
  })

  it('propose les 4 étapes simples d’onboarding sans friction', () => {
    render(
      <MemoryRouter>
        <LivreurPage />
      </MemoryRouter>
    )

    expect(screen.getByText(/Envoie « DISPO »/)).toBeInTheDocument()
    expect(screen.getByText(/Choisis ta commune/)).toBeInTheDocument()
    expect(screen.getByText(/Photo de ta CNI/)).toBeInTheDocument()
    expect(screen.getByText(/Bouton 🟢 DISPO/)).toBeInTheDocument()
  })

  it('permet de tester le simulateur de gains interactif', async () => {
    render(
      <MemoryRouter>
        <LivreurPage />
      </MemoryRouter>
    )

    // Par défaut 8 courses / jour
    expect(screen.getByText('10 000 F')).toBeInTheDocument()

    // Clic sur 12 courses / jour
    const btn12 = screen.getByRole('button', { name: /12 courses \/ jour/ })
    await userEvent.click(btn12)

    expect(screen.getByText('15 000 F')).toBeInTheDocument()
  })

  it('ouvre et ferme les questions de la FAQ', async () => {
    render(
      <MemoryRouter>
        <LivreurPage />
      </MemoryRouter>
    )

    const question = screen.getByText('Combien coûte l’inscription pour devenir livreur WAZAP ?')
    await userEvent.click(question)

    expect(screen.getByText(/C’est 100% GRATUIT/)).toBeInTheDocument()
  })

  it('comporte les liens d’action directs vers WhatsApp avec le numéro terrain', () => {
    render(
      <MemoryRouter>
        <LivreurPage />
      </MemoryRouter>
    )

    const ctaLinks = screen.getAllByRole('link', { name: /WHATSAPP|DISPO/i })
    expect(ctaLinks.length).toBeGreaterThanOrEqual(1)
    const waLink = ctaLinks.find((l) => l.getAttribute('href')?.includes('2250544051972'))
    expect(waLink).toBeDefined()
    expect(waLink?.getAttribute('href')).toContain('text=DISPO')
  })
})
