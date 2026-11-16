// Wire shapes of .spec/contracts/booking.md and customers.md (camelCase, UTC ISO-8601).
export type ServiceTier = 'ECONOMY' | 'PREMIUM'

/** A time (local HH:mm) range in which the worker cannot start a job: one of their live jobs widened by the travel buffer (BOOK-BUSY-01). */
export interface BusyWindow {
  startTime: string
  endTime: string
}

export interface FreeShift {
  date: string
  shiftCode: string
  /** The worker's live jobs that day (a timed job keeps the shift open, D34). Absent from older servers. */
  busyWindows?: BusyWindow[] | null
  /** What is left of the worker's daily hours that day; absent or null = not known. */
  remainingHours?: number | null
}

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
  /** Decisions D34 (Economy only): start step, shortest length and its step, how far ahead a timed job must start. */
  startStepMinutes?: number
  minDurationHours?: number
  durationStepHours?: number
  economyMinLeadMinutes?: number
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
  /** Decisions D34: the hours this price is for (null = the whole shift) and the length suggested for the home's area. */
  durationHours?: number | null
  suggestedDurationHours?: number | null
  /** D36: an ECONOMY large house is done by ONE worker who may need a 2nd consecutive job (the next shift of the day). */
  isContinuousWork?: boolean
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
  cancelReason: string | null
  paymentDeadlineAt: string | null
  /** Decisions D34: the job's local start/end (`HH:mm`) and length when a time was picked; null = the whole shift. */
  startTime?: string | null
  endTime?: string | null
  durationHours?: number | null
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
  /** Not currently returned by the list endpoint; detail may supply it. */
  cancelReason?: string | null
  createdAt: string
  /** Decisions D34: the job's local start/end when a time was picked. */
  startTime?: string | null
  endTime?: string | null
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
  /** ECONOMY only: one of the caller's favorite workers (a wish, not a guarantee). */
  preferredWorkerId?: number | null
  /** Decisions D34 (ECONOMY only): local start `HH:mm` and length in hours; both or neither. */
  startTime?: string | null
  durationHours?: number | null
  /** D36: the customer agreed that a large house is one worker who may work two consecutive jobs (required for it). */
  acceptsContinuousWork?: boolean
}
