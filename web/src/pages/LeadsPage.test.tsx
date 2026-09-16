import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import LeadsPage from './LeadsPage'

// P2 / C-09 : l'écran des leads ne montrait que les 200 plus récents et se contentait d'un
// avertissement. Les tests portent sur ce qui manquait vraiment : savoir OÙ l'on en est
// (« 1–200 sur 1 234 ») et pouvoir ATTEINDRE la suite (décalage transmis à l'API).
//
// `api` est remplacé : le sujet est la logique d'écran (pagination, remise à la première page),
// pas le client HTTP — celui-ci a ses propres tests dans `api/client.test.ts`.
const mocks = vi.hoisted(() => ({ getPage: vi.fn(), post: vi.fn() }))

vi.mock('../api/client', () => ({
  api: { getPage: mocks.getPage, get: vi.fn(), post: mocks.post },
  getToken: () => 'jeton-de-test',
}))

const getPage = mocks.getPage

interface Lead {
  id: string
  businessName: string
  contactName: string | null
  whatsAppNumber: string
  zone: string
  source: string
  referralCode: string | null
  status: string
  createdAt: string
}

function lead(index: number): Lead {
  return {
    id: `00000000-0000-0000-0000-${String(index).padStart(12, '0')}`,
    businessName: `Commerce ${index}`,
    contactName: null,
    whatsAppNumber: '+2250700000000',
    zone: 'Marcory',
    source: 'page-vente',
    referralCode: null,
    status: 'New',
    createdAt: '2026-09-15T10:00:00Z',
  }
}

/** Page pleine de `count` leads, à partir du décalage `start`. */
function page(count: number, start = 0): Lead[] {
  return Array.from({ length: count }, (_, i) => lead(start + i))
}

/** Chemin demandé lors du dernier appel à l'API. */
function lastPath(): string {
  const calls = getPage.mock.calls
  return String(calls[calls.length - 1]?.[0] ?? '')
}

describe('page des leads (C-09)', () => {
  beforeEach(() => {
    getPage.mockReset()
  })

  it('annonce la position dans l’ensemble et le total renvoyé par l’API', async () => {
    getPage.mockResolvedValue({ items: page(200), total: 1234 })

    render(<LeadsPage />)

    expect(await screen.findByText(/Leads 1.200 sur 1234/)).toBeInTheDocument()
    // Le premier chargement porte la taille de page et le décalage initial.
    expect(lastPath()).toContain('limit=200')
    expect(lastPath()).toContain('offset=0')
  })

  it('demande la page suivante avec le bon décalage', async () => {
    getPage.mockResolvedValue({ items: page(200), total: 1234 })

    render(<LeadsPage />)
    await screen.findByText(/Leads 1.200 sur 1234/)

    getPage.mockResolvedValue({ items: page(200, 200), total: 1234 })
    await userEvent.click(screen.getByRole('button', { name: /Suivants/ }))

    await waitFor(() => expect(lastPath()).toContain('offset=200'))
    expect(await screen.findByText(/Leads 201.400 sur 1234/)).toBeInTheDocument()
  })

  it('ne propose pas de page suivante quand tout est affiché (dernière page)', async () => {
    getPage.mockResolvedValue({ items: page(34), total: 34 })

    render(<LeadsPage />)
    await screen.findByText(/Leads 1.34 sur 34/)

    expect(screen.getByRole('button', { name: /Suivants/ })).toBeDisabled()
    expect(screen.getByText(/dernière page atteinte/)).toBeInTheDocument()
  })

  it('change de filtre ramène à la première page (jamais de page vide trompeuse)', async () => {
    getPage.mockResolvedValue({ items: page(200), total: 1234 })

    render(<LeadsPage />)
    await screen.findByText(/Leads 1.200 sur 1234/)

    getPage.mockResolvedValue({ items: page(200, 200), total: 1234 })
    await userEvent.click(screen.getByRole('button', { name: /Suivants/ }))
    await screen.findByText(/Leads 201.400 sur 1234/)

    getPage.mockResolvedValue({ items: page(3), total: 3 })
    await userEvent.selectOptions(screen.getByRole('combobox', { name: /^Statut :/ }), 'Converted')

    await waitFor(() => expect(lastPath()).toContain('status=Converted'))
    expect(lastPath()).toContain('offset=0')
  })

  it('reste prudent sans total : pas de « Suivants » sur une page incomplète', async () => {
    // Sans en-tête `X-Total-Count` (API plus ancienne, proxy…), l'écran ne doit ni inventer un
    // total, ni proposer une page qui n'existe peut-être pas.
    getPage.mockResolvedValue({ items: page(12), total: null })

    render(<LeadsPage />)
    await screen.findByText(/Leads 1.12/)

    expect(screen.getByRole('button', { name: /Suivants/ })).toBeDisabled()
  })
})
