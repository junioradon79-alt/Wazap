/**
 * Tests unitaires — RiderDashboardPage
 * Pattern : vi.mock('../api/client') identique aux autres tests du projet.
 * Couverture : chargement cockpit livreur, KPIs, toggle disponibilité, sélection de commune,
 * affichage de course active, challenge ambassadeur et partage parrainage.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import RiderDashboardPage from './RiderDashboardPage'
import type { RiderDashboard } from '../api/types'

/* ─── Mocks ──────────────────────────────────────────────── */
const mocks = vi.hoisted(() => ({
  get: vi.fn(),
  put: vi.fn(),
}))

vi.mock('../api/client', () => ({
  api: {
    get: mocks.get,
    put: mocks.put,
  },
}))

/* ─── Données de test ────────────────────────────────────── */
const MOCK_RIDER_DASH: RiderDashboard = {
  id: 'rider-uuid-001',
  username: 'bakary_marcory',
  fullName: 'Bakary Touré',
  phoneNumber: '+2250544051972',
  zone: 'Marcory',
  isAvailable: true,
  isVerified: true,
  identityStatus: 'Verified',
  idNumber: 'CI-00123984',
  documentType: 'CNI / Permis',
  deliveriesToday: 7,
  deliveriesThisMonth: 84,
  totalDeliveries: 120,
  totalEarningsEstimated: 154000,
  ratingAverage: 4.9,
  ratingCount: 46,
  referralCode: 'WA-BAK9',
  validatedReferrals: 3,
  programProgress: {
    deliveries: 120,
    deliveriesTarget: 50,
    validatedReferrals: 3,
    referralsTarget: 5,
    averageRating: 4.9,
    certified: true,
    ratingMet: true,
    rewardLabel: 'Smartphone Redmi 15C Neuf',
    conditionsMet: 2,
    rewardUnlocked: false,
  },
  activeOrder: {
    orderId: 'order-act-001',
    code: 'ABC89012',
    clientName: 'Awa Diallo',
    clientPhone: '+2250700000001',
    vendorName: 'Pâtisserie Chez Fatou',
    vendorPhone: '+2250500000002',
    pickupAddress: 'Marcory Boulevard VGE',
    deliveryAddress: 'Cocody Cité des Arts',
    amount: 15000,
    deliveryFee: 1500,
    totalAmount: 16500,
    status: 'InTransit',
    assignedAt: new Date().toISOString(),
  },
  recentOrders: [
    {
      id: 'recent-001',
      code: 'REC12345',
      clientName: 'Kouassi Jean',
      deliveryAddress: 'Koumassi Remblais',
      deliveryFee: 1000,
      status: 'Delivered',
      deliveredAt: new Date().toISOString(),
    },
  ],
}

describe('RiderDashboardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.get.mockResolvedValue(MOCK_RIDER_DASH)
    mocks.put.mockResolvedValue({})
  })

  it('affiche le nom du livreur, son badge certifié et sa commune active', async () => {
    render(<RiderDashboardPage />)

    expect(screen.getByText(/Chargement de votre cockpit livreur/i)).toBeInTheDocument()

    await waitFor(() => {
      expect(screen.getByText(/Bakary Touré/i)).toBeInTheDocument()
    })

    expect(screen.getByText(/CERTIFIÉ WAZAP/i)).toBeInTheDocument()
    expect(screen.getAllByText(/Marcory/i).length).toBeGreaterThan(0)
  })

  it('affiche les 4 KPIs principaux avec montants nets sans commission', async () => {
    render(<RiderDashboardPage />)

    await waitFor(() => {
      expect(screen.getByText('7')).toBeInTheDocument() // Courses aujourd'hui
    })

    expect(screen.getByText(/154\s?000\s?F/i)).toBeInTheDocument() // Gains nets estimés
    expect(screen.getByText(/4.9 \/ 5/i)).toBeInTheDocument() // Note
    expect(screen.getByText('120')).toBeInTheDocument() // Total historique
  })

  it('permet de changer sa disponibilité en 1 tap', async () => {
    render(<RiderDashboardPage />)

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /EN LIGNE \(DISPO\)/i })).toBeInTheDocument()
    })

    const dispoBtn = screen.getByRole('button', { name: /EN LIGNE \(DISPO\)/i })
    fireEvent.click(dispoBtn)

    await waitFor(() => {
      expect(mocks.put).toHaveBeenCalledWith('/riders/rider-uuid-001/availability', {
        isAvailable: false,
      })
    })
  })

  it('permet de changer sa commune d intervention en 1 tap', async () => {
    render(<RiderDashboardPage />)

    await waitFor(() => {
      expect(screen.getByText(/Bakary Touré/i)).toBeInTheDocument()
    })

    const cocodyPill = screen.getByRole('button', { name: /^Cocody$/i })
    fireEvent.click(cocodyPill)

    await waitFor(() => {
      expect(mocks.put).toHaveBeenCalledWith('/riders/rider-uuid-001/zone', {
        zone: 'Cocody',
      })
    })
  })

  it('affiche la course active avec les détails retrait et livraison', async () => {
    render(<RiderDashboardPage />)

    await waitFor(() => {
      expect(screen.getByText(/COURSE EN COURS DE LIVRAISON/i)).toBeInTheDocument()
    })

    expect(screen.getByText(/Course #ABC89012/i)).toBeInTheDocument()
    expect(screen.getByText(/Pâtisserie Chez Fatou/i)).toBeInTheDocument()
    expect(screen.getByText(/Awa Diallo/i)).toBeInTheDocument()
    expect(screen.getAllByText(/1\s?500\s?FCFA/i).length).toBeGreaterThan(0)
  })

  it('affiche le challenge ambassadeur et le code parrain', async () => {
    render(<RiderDashboardPage />)

    await waitFor(() => {
      expect(screen.getByText(/CHALLENGE LIVREURS WAZAP/i)).toBeInTheDocument()
    })

    expect(screen.getByText(/Smartphone Redmi 15C Neuf/i)).toBeInTheDocument()
    expect(screen.getByText('WA-BAK9')).toBeInTheDocument()
  })
})
