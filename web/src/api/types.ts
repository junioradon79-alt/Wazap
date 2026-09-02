// Types alignés sur les DTOs C# (ASP.NET sérialise en camelCase).

export type UserRole = 'Admin' | 'Vendor' | 'Rider' | 'Client'

export interface AuthResponse {
  userId: string
  token: string
  username: string
  role: UserRole
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
}

export type OrderStatus =
  | 'New'
  | 'Confirmed'
  | 'RiderAssigned'
  | 'InDelivery'
  | 'Delivered'
  | 'Cancelled'

export interface OrderDto {
  id: string
  clientName: string
  description: string
  amount: number
  status: OrderStatus
  createdAt: string
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

export interface ClientOrderStatus {
  id: string
  code: string
  vendorName: string | null
  status: string
  description: string | null
  needsCoordinates: boolean
  hasCoordinates: boolean
  address: string | null
  riderAssigned: boolean
  delivered: boolean
}

export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
}
