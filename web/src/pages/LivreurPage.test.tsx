import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import LivreurPage from './LivreurPage'

describe('LivreurPage', () => {
  it('met en avant un parcours d’inscription direct sur WhatsApp', () => {
    render(<MemoryRouter><LivreurPage /></MemoryRouter>)

    expect(screen.getByRole('heading', { name: /Livrez à votre rythme/i })).toBeInTheDocument()
    expect(screen.getAllByText(/05 44 05 19 72/).length).toBeGreaterThanOrEqual(1)
    const links = screen.getAllByRole('link', { name: /DISPO/i })
    expect(links.some((link) => link.getAttribute('href')?.includes('2250544051972'))).toBe(true)
  })

  it('présente les quatre paliers du barème Grand Abidjan', () => {
    render(<MemoryRouter><LivreurPage /></MemoryRouter>)

    expect(screen.getByText('Intra-commune')).toBeInTheDocument()
    expect(screen.getByText('Communes voisines')).toBeInTheDocument()
    expect(screen.getByText('Traversée / pont')).toBeInTheDocument()
    expect(screen.getByText('Périphérie express')).toBeInTheDocument()
  })

  it('met à jour l’estimation lorsque le nombre de courses change', async () => {
    const user = userEvent.setup()
    render(<MemoryRouter><LivreurPage /></MemoryRouter>)

    expect(screen.getByText(/10\s+000 F/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '12 courses' }))
    expect(screen.getByText(/15\s+000 F/)).toBeInTheDocument()
  })

  it('affiche les réponses aux questions fréquentes', async () => {
    const user = userEvent.setup()
    render(<MemoryRouter><LivreurPage /></MemoryRouter>)

    await user.click(screen.getByText(/L’inscription pour devenir livreur WAZAP/i))
    expect(screen.getByText(/L’enrôlement démarre gratuitement/i)).toBeInTheDocument()
  })

  it('oriente les commerçants vers la page dédiée', () => {
    render(<MemoryRouter><LivreurPage /></MemoryRouter>)
    expect(screen.getAllByRole('link', { name: /Je suis commerçant|Vous êtes commerçant/i })[0]).toHaveAttribute('href', '/vente')
  })
})
