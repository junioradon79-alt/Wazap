// Types alignés sur les DTOs C# (ASP.NET sérialise en camelCase).

export type UserRole = 'Admin' | 'Vendor' | 'Rider' | 'Client'

// Aligné sur Wazap.Application/Dtos/AuthDtos.cs : `token` est NULL quand la double
// authentification est activée (le serveur attend alors le code TOTP à l'étape suivante).
export interface AuthResponse {
  userId: string
  token: string | null
  username: string
  role: UserRole
  mfaRequired: boolean
  refreshToken: string | null
}

export interface UserSummary {
  id: string
  username: string
  phoneNumber: string | null
  role: UserRole
  isAvailable: boolean
  locationSharingEnabled: boolean
  zone: string | null
  credits: number
  latitude: number | null
  longitude: number | null
  locationUpdatedAt: string | null
}

export interface PackDto {
  name: string
  price: number
  credits: number
}

export interface PaymentResponse {
  success: boolean
  transactionReference: string
  paymentLink: string | null
  message: string
}

export type TransactionStatus = 'Pending' | 'Completed' | 'Failed'

export interface CreditTransaction {
  id: string
  vendorId: string
  amount: number
  creditsPurchased: number
  createdAt: string
  transactionReference: string
  status: TransactionStatus
}

export type DashboardStatusCategory = 'RechercheLivreur' | 'EnLivraison' | 'Livre' | 'Autre'

export interface OrderInProgress {
  id: string
  vendorName: string
  vendorWhatsApp: string
  maskedClientPhone: string
  statusCategory: DashboardStatusCategory
  statusLabel: string
}

export interface DashboardSummary {
  inProgressOrdersCount: number
  activeRiders: number
  monthlyRevenue: number
  recentOrders: OrderInProgress[]
  totalVendors: number
  newVendors30d: number
  activeVendors30d: number
  totalRiders: number
  ordersThisWeek: number
  ordersLast30d: number
  ordersByZone30d: ZoneMetric[]

  averageBasket30d: number
  deliveryRate30d: number
  revenue30d: number
  revenueChangePercent: number
  topVendors30d: TopVendor[]
  leadConversionRate30d: number
  revenueByZone30d: ZoneRevenue[]
}

export interface TopVendor {
  username: string
  deliveredOrders: number
  revenue: number
}

export interface ZoneMetric {
  zone: string
  orders: number
}

export interface ZoneRevenue {
  zone: string
  revenue: number
}

// Aligné sur Wazap.Domain/Enums/OrderStatus.cs. L'ancienne union inventait
// « New »/« Confirmed »/« InDelivery » (qui n'existent pas) et omettait quatre états
// réels : tout `switch` sur le statut était faux, sans erreur de compilation.
export type OrderStatus =
  | 'PendingVendorConfirmation'
  | 'VendorConfirmed'
  | 'AwaitingRiderAcceptance'
  | 'RiderAssigned'
  | 'ReadyForPickup'
  | 'PickedUp'
  | 'InTransit'
  | 'Delivered'
  | 'Cancelled'

export interface OrderDto {
  id: string
  clientName: string
  description: string
  amount: number
  status: OrderStatus
  createdAt: string
  hasProofPhoto: boolean
}

export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
  totalPages: number
}

export interface CreateOrderRequest {
  clientName: string
  clientWhatsAppNumber: string
  vendorWhatsAppNumber: string
  description: string
  amount: number
}

export type ClientPaymentStatus = 'Pending' | 'Completed' | 'Failed' | 'NotFound' | 'Disabled' | 'Error'

export interface ClientPaymentInfo {
  status: ClientPaymentStatus
  amount: number
  paymentLink: string | null
}

export interface ClientOrderLine {
  productName: string
  quantity: number
  unitPrice: number
  totalPrice: number
}

export interface ClientOrderTimestamps {
  createdAt: string
  vendorConfirmedAt: string | null
  riderAssignedAt: string | null
  pickedUpAt: string | null
  deliveredAt: string | null
  cancelledAt: string | null
}

export interface ClientOrderRating {
  score: number
  comment: string | null
  createdAt?: string | null
}

export interface SubmitClientRatingRequest {
  score: number
  comment?: string | null
}

export interface ClientOrderStatus {
  id: string
  code: string
  vendorName: string | null
  status: string
  description: string | null
  amount?: number
  deliveryFee?: number
  totalAmount?: number
  deliveryCode?: string | null
  hasProofPhoto?: boolean
  riderPhone?: string | null
  needsCoordinates: boolean
  hasCoordinates: boolean
  address: string | null
  riderAssigned: boolean
  delivered: boolean
  cancellationReason?: string | null
  cancellationComment?: string | null
  timestamps?: ClientOrderTimestamps
  orderLines?: ClientOrderLine[]
  rating?: ClientOrderRating | null
  payment: ClientPaymentInfo | null
  qrUrl?: string
  riderName?: string | null
  trackingUrl?: string
}

export interface ClientPaymentResponse {
  status: ClientPaymentStatus
  amount: number
  paymentLink: string | null
}

export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
}

export interface VendorOrderItem {
  id: string
  code: string
  clientName: string | null
  description: string
  status: string
  createdAt: string
  riderName?: string | null
  riderPhone?: string | null
  amount?: number
  deliveryFee?: number
  totalAmount?: number
}

export interface RiderCertification {
  riderId: string
  username: string
  phoneNumber: string | null
  zone: string | null
  isAvailable: boolean
  status: 'Pending' | 'Verified' | 'Rejected' | 'Blacklisted'
  fullName: string | null
  idNumber: string | null
  motorcycle: string | null
  idScanUrl: string | null
  scanFileName: string | null
  scanReceivedAt: string | null
  consentGivenAt: string | null
  consentMethod: string | null
  blacklistReason: string | null
  createdAt: string | null
  reviewedAt: string | null
}

export type DeliveryClaimStatus = 'Pending' | 'Approved' | 'Rejected'

export interface ClaimListItem {
  claimId: string
  orderId: string
  orderCode: string
  vendorUserId: string
  vendorName: string
  vendorPhone: string | null
  riderUserId: string
  riderName: string
  status: DeliveryClaimStatus
  compensationCredits: number | null
  description: string | null
  vendorNote: string | null
  reviewNote: string | null
  createdAt: string
  reviewedAt: string | null
  compensationAmountFcfa: number | null
  riderDepositDebitedFcfa: number | null
  payoutStatus: 'None' | 'Pending' | 'Paid' | 'Failed'
  payoutReference: string | null
  paidAt: string | null
  /** Montant proposé par le barème (valeur de la course − franchise, borné par le plafond). */
  suggestedCompensationFcfa: number
}

export interface RiderRatingAdmin {
  ratingId: string
  orderCode: string
  riderId: string
  riderName: string
  score: number
  comment: string | null
  reply: string | null
  repliedAt: string | null
  maskedClientPhone: string
  createdAt: string
}

export interface RiderRatingSummary {
  riderId: string
  riderName: string
  averageScore: number
  ratingCount: number
}

export interface RiderRatingAdminBoard {
  ratings: RiderRatingAdmin[]
  riderSummaries: RiderRatingSummary[]
}

export interface ReferredVendorItem {
  id: string
  username: string
  phoneNumber: string | null
  zone: string | null
  createdAt: string
}

export interface VendorDashboard {
  id: string
  username: string
  phoneNumber: string | null
  zone: string | null
  credits: number
  referralCode: string
  inProgressOrders: number
  deliveredThisMonth: number
  recentOrders: VendorOrderItem[]
  totalReferrals: number
  referralCreditsEarned: number
  referrals: ReferredVendorItem[]

  monthlyRevenue: number
  averageBasket: number
  deliveryRate: number
  ordersThisWeek: number
  ordersLastMonth: number
  deliveredLastMonth: number
  topClients: VendorClientItem[]
}

export interface VendorClientItem {
  clientName: string
  orderCount: number
  totalSpent: number
}

/** Produit du catalogue vendeur (menu du bot de commande WhatsApp). */
export interface VendorProduct {
  id: string
  vendorId: string
  name: string
  description: string
  price: number
  emoji: string | null
  createdAt: string
}

/** Création / mise à jour d'un produit du catalogue vendeur. */
export interface VendorProductRequest {
  name: string
  description?: string | null
  price: number
  emoji?: string | null
}

/** Journalisation d'audit d'un message WhatsApp (chantier T2 / C1). */
export interface WhatsAppMessageLogDto {
  id: string
  orderId: string | null
  recipientUserId: string | null
  recipientPhone: string
  senderPhone: string | null
  direction: string
  messageType: string
  templateName: string | null
  category: string | null
  provider: string
  providerMessageId: string | null
  status: string
  errorCode: number | null
  errorMessage: string | null
  estimatedCostFcfa: number | null
  createdAt: string
}

/** Synthèse financière des coûts WhatsApp pour la Côte d'Ivoire. */
export interface WhatsAppCostSummaryDto {
  totalMessages: number
  outboundCount: number
  inboundCount: number
  failedCount: number
  totalEstimatedCostFcfa: number
  messagesByCategory: Record<string, number>
  costByCategory: Record<string, number>
  averageCostPerOrder: number
}

