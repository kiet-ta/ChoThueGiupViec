// Wire shapes of .spec/contracts/booking.md and customers.md (camelCase, UTC ISO-8601).
export type ServiceTier = 'ECONOMY' | 'PREMIUM'

export interface ShiftOption {
  shiftCode: string
  startLocal: string
  endLocal: string
}

export interface BookingOptions {
  serviceTiers: ServiceTier[]
  shifts: ShiftOption[]
  premiumMinLeadHours: number
  shiftMaxHours: number
  standardMaxAreaM2: number
  paymentQrExpiryMinutes: number
  sandbox: boolean
}

export interface PriceQuote {
  addressId: number
  serviceTier: ServiceTier
  totalAreaM2: number
  areaBracket: string
  requiredWorkers: number
  unitPrice: number
  totalAmount: number
  currency: string
}

export interface CustomerAddress {
  addressId: number
  label: string
  addressLine: string
  district: string
  city: string
  totalAreaM2: number
  isDefault: boolean
}

export interface Order {
  orderId: number
  orderCode: string
  serviceTier: ServiceTier
  addressId: number
  scheduledDate: string
  shiftCode: string
  shiftStartAt: string
  shiftEndAt: string
  requiredWorkers: number
  totalAmount: number
  orderStatus: string
  customerNote: string | null
  paymentDeadlineAt: string | null
}

export interface OrderSummary {
  orderId: number
  orderCode: string
  serviceTier: ServiceTier
  scheduledDate: string
  shiftCode: string
  requiredWorkers: number
  totalAmount: number
  orderStatus: string
  createdAt: string
}

export interface OrderPage {
  items: OrderSummary[]
  page: number
  pageSize: number
  total: number
}

export interface CreateOrderBody {
  addressId: number
  serviceTier: ServiceTier
  scheduledDate: string
  shiftCode: string
  customerNote: string | null
}
