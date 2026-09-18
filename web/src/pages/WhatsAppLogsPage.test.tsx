import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import WhatsAppLogsPage from './WhatsAppLogsPage'
import type { WhatsAppCostSummaryDto, WhatsAppMessageLogDto } from '../api/types'

const mocks = vi.hoisted(() => ({
  get: vi.fn(),
}))

vi.mock('../api/client', () => ({
  api: {
    get: mocks.get,
    post: vi.fn(),
  },
  getToken: () => 'fake-admin-token',
  getUser: () => ({ userId: 'admin-1', username: 'admin', role: 'Admin' }),
}))

const mockSummary: WhatsAppCostSummaryDto = {
  totalMessages: 4,
  outboundCount: 3,
  inboundCount: 1,
  failedCount: 1,
  totalEstimatedCostFcfa: 15.06,
  messagesByCategory: {
    utility: 2,
    marketing: 1,
    service: 1,
    authentication: 0,
  },
  costByCategory: {
    utility: 4.54,
    marketing: 12.79,
    service: 0,
    authentication: 0,
  },
  averageCostPerOrder: 5.02,
}

const mockLogs: WhatsAppMessageLogDto[] = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    orderId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    recipientUserId: null,
    recipientPhone: '+2250701020304',
    senderPhone: null,
    direction: 'Outbound',
    messageType: 'Template',
    templateName: 'order_status_update_v2',
    category: 'utility',
    provider: 'MetaCloudApi',
    providerMessageId: 'wamid.HBgLMTIz',
    status: 'Delivered',
    errorCode: null,
    errorMessage: null,
    estimatedCostFcfa: 2.27,
    createdAt: '2026-09-18T18:00:00Z',
  },
  {
    id: '22222222-2222-2222-2222-222222222222',
    orderId: null,
    recipientUserId: null,
    recipientPhone: '+2250505050505',
    senderPhone: null,
    direction: 'Inbound',
    messageType: 'Text',
    templateName: null,
    category: 'service',
    provider: 'MetaCloudApi',
    providerMessageId: 'wamid.HBgLMjM0',
    status: 'Received',
    errorCode: null,
    errorMessage: null,
    estimatedCostFcfa: 0,
    createdAt: '2026-09-18T18:05:00Z',
  },
  {
    id: '33333333-3333-3333-3333-333333333333',
    orderId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    recipientUserId: null,
    recipientPhone: '+2250102030405',
    senderPhone: null,
    direction: 'Outbound',
    messageType: 'Template',
    templateName: 'prospect_offer_v1',
    category: 'marketing',
    provider: 'MetaCloudApi',
    providerMessageId: null,
    status: 'Failed',
    errorCode: 131026,
    errorMessage: 'Message undeliverable',
    estimatedCostFcfa: 0,
    createdAt: '2026-09-18T18:10:00Z',
  },
]

describe('WhatsAppLogsPage (T2 - Suivi des coûts & audit WhatsApp)', () => {
  beforeEach(() => {
    mocks.get.mockReset()
  })

  it('affiche les indicateurs KPI et la répartition par catégorie Meta', async () => {
    mocks.get.mockImplementation((path: string) => {
      if (path.includes('/admin/whatsapp/costs')) return Promise.resolve(mockSummary)
      if (path.includes('/admin/whatsapp/logs')) return Promise.resolve(mockLogs)
      return Promise.reject(new Error(`Unknown path: ${path}`))
    })

    render(<WhatsAppLogsPage />)

    // Vérification des cartes KPI
    expect(await screen.findByText('Coût Total Estimé')).toBeInTheDocument()
    expect(screen.getByText('15,06 FCFA')).toBeInTheDocument()
    expect(screen.getByText('Coût Moyen / Course')).toBeInTheDocument()
    expect(screen.getByText('5,02 FCFA')).toBeInTheDocument()
    expect(screen.getByText('Total Messages')).toBeInTheDocument()
    expect(screen.getByText('4')).toBeInTheDocument()
    expect(screen.getByText("Échecs d'Envoi")).toBeInTheDocument()

    // Vérification de la répartition par catégorie Meta
    expect(screen.getByText(/Répartition par Catégorie Meta/)).toBeInTheDocument()
    expect(screen.getAllByText('Utility').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('Marketing').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('Service').length).toBeGreaterThanOrEqual(1)
  })

  it('affiche le journal des messages avec détails et badges de statut', async () => {
    mocks.get.mockImplementation((path: string) => {
      if (path.includes('/admin/whatsapp/costs')) return Promise.resolve(mockSummary)
      if (path.includes('/admin/whatsapp/logs')) return Promise.resolve(mockLogs)
      return Promise.reject(new Error(`Unknown path: ${path}`))
    })

    render(<WhatsAppLogsPage />)

    expect(await screen.findByText('+2250701020304')).toBeInTheDocument()
    expect(screen.getByText('+2250505050505')).toBeInTheDocument()
    expect(screen.getByText('+2250102030405')).toBeInTheDocument()

    // Modèle de template et types
    expect(screen.getByText('order_status_update_v2')).toBeInTheDocument()
    expect(screen.getByText('prospect_offer_v1')).toBeInTheDocument()

    // Sens des messages (2 dans le tableau + 1 dans le <select> de filtre)
    expect(screen.getAllByText('↗️ Sortant')).toHaveLength(3)
    // 1 dans le tableau + 1 dans le <select> de filtre
    expect(screen.getAllByText('↙️ Entrant')).toHaveLength(2)

    // Erreur d'échec
    expect(screen.getByText(/Message undeliverable/)).toBeInTheDocument()
  })

  it('permet de basculer la période temporelle', async () => {
    mocks.get.mockImplementation((path: string) => {
      if (path.includes('/admin/whatsapp/costs')) return Promise.resolve(mockSummary)
      if (path.includes('/admin/whatsapp/logs')) return Promise.resolve(mockLogs)
      return Promise.reject(new Error(`Unknown path: ${path}`))
    })

    render(<WhatsAppLogsPage />)
    await screen.findByText('Coût Total Estimé')

    // Cliquer sur le filtre "Aujourd'hui"
    const todayBtn = screen.getByRole('button', { name: "Aujourd'hui" })
    await userEvent.click(todayBtn)

    await waitFor(() => {
      const calls = mocks.get.mock.calls
      const lastCostCall = calls.find((c) => String(c[0]).includes('/admin/whatsapp/costs?from='))
      expect(lastCostCall).toBeDefined()
    })
  })

  it('permet de filtrer en direct par sens ou statut', async () => {
    mocks.get.mockImplementation((path: string) => {
      if (path.includes('/admin/whatsapp/costs')) return Promise.resolve(mockSummary)
      if (path.includes('/admin/whatsapp/logs')) return Promise.resolve(mockLogs)
      return Promise.reject(new Error(`Unknown path: ${path}`))
    })

    render(<WhatsAppLogsPage />)
    await screen.findByText('+2250701020304')

    // Filtrer par sens "Entrant"
    const directionSelect = screen.getByLabelText('Filtrer par sens')
    await userEvent.selectOptions(directionSelect, 'Inbound')

    // Le message sortant ne doit plus être affiché
    expect(screen.queryByText('+2250701020304')).not.toBeInTheDocument()
    // Le message entrant reste affiché
    expect(screen.getByText('+2250505050505')).toBeInTheDocument()
  })

  it('gère les erreurs de chargement avec le composant ErrorAlert et bouton Réessayer', async () => {
    mocks.get.mockRejectedValueOnce(new Error('Connexion réseau interrompue'))

    render(<WhatsAppLogsPage />)

    expect(await screen.findByRole('alert')).toHaveTextContent('Connexion réseau interrompue')
    const retryBtn = screen.getByRole('button', { name: 'Réessayer' })
    expect(retryBtn).toBeInTheDocument()

    // Quand on réessaie et que le serveur répond
    mocks.get.mockImplementation((path: string) => {
      if (path.includes('/admin/whatsapp/costs')) return Promise.resolve(mockSummary)
      if (path.includes('/admin/whatsapp/logs')) return Promise.resolve(mockLogs)
      return Promise.reject(new Error(`Unknown path: ${path}`))
    })

    await userEvent.click(retryBtn)
    expect(await screen.findByText('Coût Total Estimé')).toBeInTheDocument()
  })

  it('affiche un message explicite quand aucun log ne correspond', async () => {
    mocks.get.mockImplementation((path: string) => {
      if (path.includes('/admin/whatsapp/costs'))
        return Promise.resolve({
          ...mockSummary,
          totalMessages: 0,
          outboundCount: 0,
          inboundCount: 0,
          failedCount: 0,
          totalEstimatedCostFcfa: 0,
          averageCostPerOrder: 0,
        })
      if (path.includes('/admin/whatsapp/logs')) return Promise.resolve([])
      return Promise.reject(new Error(`Unknown path: ${path}`))
    })

    render(<WhatsAppLogsPage />)

    expect(await screen.findByText('Aucun message WhatsApp trouvé pour cette sélection.')).toBeInTheDocument()
  })
})
