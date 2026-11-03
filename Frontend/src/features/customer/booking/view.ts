// Pure logic of the booking and home screens (no React, so `node --test` covers it).
// Time zone rule (decision G-3): shifts are local Asia/Ho_Chi_Minh (UTC+7, no daylight saving).
import { formatVnd } from '../../../lib/format.ts'
import type { BookingOptions, FreeShift, OrderSummary, ServiceTier, ShiftOption } from './types.ts'

export function firstName(fullName: string): string {
  return fullName.trim().split(/\s+/).at(-1) ?? ''
}

export const NOTE_MAX_LENGTH = 500
const VN_OFFSET_MS = 7 * 3600_000
const OPEN_STATUSES = ['PENDING_PAYMENT', 'PAID', 'DISPATCHING', 'ASSIGNED']

const TIER_LABELS: Record<ServiceTier, string> = { ECONOMY: 'Economy', PREMIUM: 'Premium' }
const SHIFT_LABELS: Record<string, string> = {
  SHIFT_MORNING: 'Buổi sáng',
  SHIFT_AFTERNOON: 'Buổi chiều',
  SHIFT_EVENING: 'Buổi tối',
}
const STATUS_LABELS: Record<string, string> = {
  PENDING_PAYMENT: 'Chờ thanh toán',
  PAID: 'Đã thanh toán',
  DISPATCHING: 'Đang tìm thợ',
  ASSIGNED: 'Đã có thợ',
  COMPLETED: 'Hoàn tất',
  CANCELLED: 'Đã hủy',
}
const WEEKDAYS = ['CN', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7']

export const tierLabel = (tier: string) => TIER_LABELS[tier as ServiceTier] ?? tier
export const shiftLabel = (code: string) => SHIFT_LABELS[code] ?? code
export const statusLabel = (status: string) => STATUS_LABELS[status] ?? status

/** `2026-11-02` for the Vietnam calendar day of an instant. */
export function vnDate(at: Date): string {
  return new Date(at.getTime() + VN_OFFSET_MS).toISOString().slice(0, 10)
}

/** The first instant of a shift: local date + `HH:mm` converted to UTC. */
export function shiftStart(date: string, startLocal: string): Date {
  return new Date(`${date}T${startLocal}:00+07:00`)
}

export interface DayOption {
  date: string
  weekday: string
  day: string
}

/** The next `count` Vietnam calendar days starting today. */
export function upcomingDays(now: Date, count = 7): DayOption[] {
  return Array.from({ length: count }, (_, i) => {
    const date = vnDate(new Date(now.getTime() + i * 86_400_000))
    const weekday = WEEKDAYS[new Date(`${date}T00:00:00Z`).getUTCDay()]
    return { date, weekday, day: date.slice(8) }
  })
}

/** Why a shift cannot be booked right now, or null when it can (every tier needs a future shift; Premium needs `premiumMinLeadHours`). */
export function shiftBlockedReason(
  date: string,
  shift: ShiftOption,
  tier: ServiceTier,
  options: Pick<BookingOptions, 'premiumMinLeadHours'>,
  now: Date,
  freeShifts: FreeShift[] | null = null,
): string | null {
  const start = shiftStart(date, shift.startLocal).getTime()
  if (start <= now.getTime()) return 'Đã qua'
  if (tier === 'PREMIUM' && start < now.getTime() + options.premiumMinLeadHours * 3600_000) {
    return `Đặt trước tối thiểu ${options.premiumMinLeadHours} giờ`
  }
  if (tier === 'ECONOMY' && freeShifts && freeShifts.length > 0 && !freeShifts.some((free) => free.date === date && free.shiftCode === shift.shiftCode)) {
    return 'Thợ không rảnh ca này'
  }
  return null
}

/** `T2 26/10` for a `yyyy-MM-dd` date. */
export function dayText(date: string): string {
  const weekday = WEEKDAYS[new Date(`${date}T00:00:00Z`).getUTCDay()]
  return `${weekday} ${date.slice(8)}/${date.slice(5, 7)}`
}

export const shiftRange = (shift: Pick<ShiftOption, 'startLocal' | 'endLocal'>) => `${shift.startLocal} → ${shift.endLocal}`

export const workersPrice = (workers: number, unitPrice: number) => `${workers} × ${formatVnd(unitPrice)}`

interface ProblemInput {
  status?: number
  message?: string
  data?: unknown
  fieldErrors?: Record<string, string[]>
}

/** Text of a failed booking call: the first field message for 400, a fixed sentence for the contract 409 codes, the server message otherwise. */
export function bookingProblem(error: ProblemInput): string {
  if (error.status === 400) {
    const first = Object.values(error.fieldErrors ?? {}).flat()[0]
    if (first) return first
  }
  const code = error.data && typeof error.data === 'object' ? (error.data as { code?: unknown }).code : undefined
  if (code === 'SHIFT_IN_PAST') return 'Ca đã chọn đã qua. Hãy chọn ca khác.'
  if (code === 'PREMIUM_LEAD_TIME') return 'Gói Premium cần đặt trước tối thiểu 4 giờ.'
  if (code === 'FULLY_BOOKED') return 'Ca này hiện không còn thợ rảnh ở khu vực của bạn. Hãy chọn ca hoặc ngày khác.'
  if (error.status === 404) return 'Không tìm thấy địa chỉ. Hãy chọn lại địa chỉ.'
  return error.message || 'Không đặt được ca. Vui lòng thử lại.'
}

/**
 * The nearest upcoming open order (waiting for payment, paid, dispatching or assigned) whose shift has not ended yet; null when there
 * is none. With `shifts` (from GET /booking/options) a shift of today that already ended is skipped; without them every order of today counts.
 */
export function nextOrder(orders: OrderSummary[], now: Date, shifts: ShiftOption[] = []): OrderSummary | null {
  const today = vnDate(now)
  const notEnded = (o: OrderSummary) => {
    if (o.scheduledDate > today) return true
    if (o.scheduledDate < today) return false
    const shift = shifts.find((s) => s.shiftCode === o.shiftCode)
    return !shift || shiftStart(o.scheduledDate, shift.endLocal).getTime() > now.getTime()
  }
  const startOf = (o: OrderSummary) => shifts.find((s) => s.shiftCode === o.shiftCode)?.startLocal ?? o.shiftCode
  const upcoming = orders
    .filter((o) => OPEN_STATUSES.includes(o.orderStatus) && notEnded(o))
    .sort((a, b) => a.scheduledDate.localeCompare(b.scheduledDate) || startOf(a).localeCompare(startOf(b)))
  return upcoming[0] ?? null
}
