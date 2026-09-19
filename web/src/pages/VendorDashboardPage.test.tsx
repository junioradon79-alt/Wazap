/**
 * Tests unitaires — VendorDashboardPage (Étape 2)
 * Pattern : vi.mock('../api/client') identique aux autres tests du projet.
 * Couverture : chargement, credit wallet, filtres, modal d'expédition, modal recharge, parrainage.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import VendorDashboardPage from './VendorDashboardPage'
import type { VendorDashboard, PackDto } from '../api/types'

/* ─── Mocks ──────────────────────────────────────────────── */
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

/* ─── Données de test ────────────────────────────────────── */
const MOCK_DASH: VendorDashboard = {
  id: 'vendor-uuid-001',
  username: 'Chez Mama Bofou',
  phoneNumber: '2250701234567',
  zone: 'Marcory Zone 4',
  credits: 42,
  referralCode: 'MAMA007',
  inProgressOrders: 3,
  deliveredThisMonth: 78,
  recentOrders: [
    {
      id: 'order-aaa-001',
      code: 'ABC123',
      clientName: 'Koné Fatou',
      description: '2 Attiéké poisson braisé',
      status: 'InTransit',
      createdAt: new Date().toISOString(),
    },
    {
      id: 'order-aaa-002',
      code: 'DEF456',
      clientName: 'Kouassi Yao',
      description: '1 Riz sauce graine',
      status: 'Delivered',
      createdAt: new Date(Date.now() - 3600000).toISOString(),
    },
    {
      id: 'order-aaa-003',
      code: 'GHI789',
      clientName: 'Mariam Diallo',
      description: '3 Alloco banane frite',
      status: 'Cancelled',
      createdAt: new Date(Date.now() - 7200000).toISOString(),
    },
  ],
  totalReferrals: 5,
  referralCreditsEarned: 25,
  referrals: [],
  monthlyRevenue: 875000,
  averageBasket: 4500,
  deliveryRate: 0.94,
  ordersThisWeek: 12,
  ordersLastMonth: 83,
  deliveredLastMonth: 78,
  topClients: [
    { clientName: 'Koné Fatou', orderCount: 12, totalSpent: 54000 },
    { clientName: 'Kouassi Yao', orderCount: 8, totalSpent: 36000 },
  ],
}

const MOCK_PACKS: PackDto[] = [
  { name: 'Mini', price: 1000, credits: 6 },
  { name: 'Découverte', price: 2500, credits: 15 },
  { name: 'Petit', price: 5000, credits: 35 },
  { name: 'Moyen', price: 10000, credits: 80 },
  { name: 'Grand', price: 25000, credits: 220 },
  { name: 'Pro', price: 100000, credits: 1000 },
]

const renderPage = () =>
  render(
    <MemoryRouter>
      <VendorDashboardPage />
    </MemoryRouter>
  )

beforeEach(() => {
  vi.clearAllMocks()
  mocks.get.mockImplementation((url: string) => {
    if (url === '/vendors/dashboard') return Promise.resolve(MOCK_DASH)
    if (url === '/packs') return Promise.resolve(MOCK_PACKS)
    return Promise.reject(new Error(`Route inconnue : ${url}`))
  })
})

/* ─── Tests ──────────────────────────────────────────────── */
describe('VendorDashboardPage (Espace marchand premium)', () => {

  it('affiche le nom du marchand, la zone, le wallet crédits et le code parrainage', async () => {
    renderPage()

    await waitFor(() => {
      expect(screen.getByText('Chez Mama Bofou')).toBeInTheDocument()
    })

    // Zone / informations marchand
    expect(screen.getByText(/Marcory Zone 4/)).toBeInTheDocument()

    // Credit wallet — le solde 42 dans le wallet (balise précise)
    expect(screen.getByText('42')).toBeInTheDocument()

    // Livraisons ce mois dans les KPI (il y a 78 livraisons)
    expect(screen.getAllByText('78').length).toBeGreaterThanOrEqual(1)

    // Code parrainage visible
    expect(screen.getByText('MAMA007')).toBeInTheDocument()
  })

  it('filtre les courses : toutes → en cours → livrées', async () => {
    renderPage()

    await waitFor(() => {
      expect(screen.getByText('Chez Mama Bofou')).toBeInTheDocument()
    })

    // Toutes les commandes visibles par défaut
    await waitFor(() => {
      expect(screen.getAllByText('Koné Fatou').length).toBeGreaterThanOrEqual(1)
    })
    expect(screen.getAllByText('Kouassi Yao').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('Mariam Diallo').length).toBeGreaterThanOrEqual(1)

    // Filtre « En cours » : seule la commande InTransit dans la table
    await act(async () => {
      fireEvent.click(screen.getByText('🔴 En cours'))
    })

    // Koné Fatou reste (InTransit) — dans la table + dans le top clients
    await waitFor(() => {
      expect(screen.getAllByText('Koné Fatou').length).toBeGreaterThanOrEqual(1)
    })
    // Mariam Diallo (Cancelled) disparaît de la table
    const allMariamAfterFilter = screen.queryAllByText('Mariam Diallo')
    expect(allMariamAfterFilter.length).toBe(0)

    // Filtre « Livrées »
    await act(async () => {
      fireEvent.click(screen.getByText('✅ Livrées'))
    })

    // DEF456 (Kouassi Yao Delivered) dans la table
    await waitFor(() => {
      const koussiElements = screen.queryAllByText('Kouassi Yao')
      // Au moins un (dans table ou top clients)
      expect(koussiElements.length).toBeGreaterThanOrEqual(1)
    })
  })

  it('ouvre le modal d\'expédition et crée une nouvelle livraison avec lien de suivi', async () => {
    mocks.post.mockResolvedValueOnce({ id: 'new-order-uuid', code: 'NEW001' })

    renderPage()

    await waitFor(() => {
      expect(screen.getByText('Chez Mama Bofou')).toBeInTheDocument()
    })

    // Ouvre le modal via bouton header (id unique)
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /Expédier un colis/i }))
    })

    // Modal visible
    await waitFor(() => {
      expect(screen.getByText('🚀 Nouvelle livraison')).toBeInTheDocument()
    })

    // Formulaire (utilise getByLabelText car placeholders pourraient varier)
    const user = userEvent.setup()
    await user.type(screen.getByLabelText(/Nom du destinataire/i), 'Test Client')
    await user.type(screen.getByLabelText(/WhatsApp du destinataire/i), '0700000000')
    await user.type(screen.getByLabelText(/Description du colis/i), '2 plats Riz sauce')

    // Soumet
    await act(async () => {
      fireEvent.click(screen.getByText('🚀 Lancer la livraison'))
    })

    // Succès
    await waitFor(() => {
      expect(screen.getByText(/Commande #NEW001 créée/i)).toBeInTheDocument()
    })

    // Bouton partage WhatsApp
    expect(screen.getByText(/Partager le suivi à Test Client/i)).toBeInTheDocument()
  })

  it('ouvre le modal de recharge, sélectionne Grand et confirme le paiement', async () => {
    mocks.post.mockResolvedValueOnce({
      success: true,
      transactionReference: 'TXN-001',
      paymentLink: null,
      message: 'Paiement simulé avec succès.',
    })

    renderPage()

    await waitFor(() => {
      expect(screen.getByText('Chez Mama Bofou')).toBeInTheDocument()
    })

    // Ouvre modal recharge
    await act(async () => {
      fireEvent.click(screen.getByText('💳 Recharger'))
    })

    await waitFor(() => {
      expect(screen.getByText('💳 Recharger mes crédits')).toBeInTheDocument()
    })

    // Tous les packs sont affichés
    expect(screen.getByText('Mini')).toBeInTheDocument()
    expect(screen.getByText('Pro')).toBeInTheDocument()

    // Sélectionne Grand (pack card title)
    const grandCards = screen.getAllByText('Grand')
    await act(async () => {
      fireEvent.click(grandCards[0])
    })

    // Payer — il peut y avoir plusieurs boutons "Payer maintenant"
    await act(async () => {
      const payBtn = screen.getByText('💳 Payer maintenant')
      fireEvent.click(payBtn)
    })

    // Confirmation
    await waitFor(() => {
      expect(screen.getByText(/Paiement confirmé/i)).toBeInTheDocument()
    })
    expect(screen.getByText(/Paiement simulé avec succès/i)).toBeInTheDocument()
  })

  it('affiche les meilleurs clients avec classement et boutons de fidélisation', async () => {
    renderPage()

    await waitFor(() => {
      expect(screen.getByText('🏆 Meilleurs clients')).toBeInTheDocument()
    })

    // Le premier client apparaît dans la section clients (peut aussi être dans le tableau)
    const allFatou = screen.getAllByText('Koné Fatou')
    expect(allFatou.length).toBeGreaterThanOrEqual(1)

    // Le deuxième client apparaît aussi
    const allKouassi = screen.getAllByText('Kouassi Yao')
    expect(allKouassi.length).toBeGreaterThanOrEqual(1)

    // Boutons fidélisation WhatsApp dans la section top clients
    const loyaltyBtns = screen.getAllByText('💬 Fidéliser')
    expect(loyaltyBtns.length).toBe(2)
  })

})
