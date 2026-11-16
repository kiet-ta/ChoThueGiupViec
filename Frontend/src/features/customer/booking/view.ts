// Pure logic of the booking and home screens (no React, so `node --test` covers it).
// Time zone rule (decision G-3): shifts are local Asia/Ho_Chi_Minh (UTC+7, no daylight saving).
import { formatVnd } from '../../../lib/format.ts'
import type { BookingOptions, FreeShift, OrderSummary, PriceQuote, ServiceTier, ShiftOption } from './types.ts'

export function firstName(fullName: string): string {
  return fullName.trim().split(/\s+/).at(-1) ?? ''
}

export const NOTE_MAX_LENGTH = 500
const VN_OFFSET_MS = 7 * 3600_000
const OPEN_STATUSES = ['PENDING_PAYMENT', 'PAID', 'DISPATCHING', 'ASSIGNED']

// The same names as the Mobile app (WEB-ORDERS-02): one package has one name everywhere the customer looks.
const TIER_LABELS: Record<ServiceTier, string> = { ECONOMY: 'Cơ bản', PREMIUM: 'Cao cấp' }
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

/**
 * BOOK-BUSY-01 (decisions D41): of the `starts` of a job of `hours`, the ones the chosen worker can take on a day whose free shift is
 * `free`: the job fits in their remaining hours and overlaps none of their busy times. Without a worker's data every start stays.
 */
export function startsWorkerCanTake(starts: string[], hours: number, free: FreeShift | undefined): string[] {
  if (!free) return starts
  if (free.remainingHours != null && hours > free.remainingHours) return []
  const windows = (free.busyWindows ?? []).map((w) => [minutesOf(w.startTime), minutesOf(w.endTime)] as const)
  if (windows.length === 0) return starts
  const length = Math.round(hours * 60)
  return starts.filter((s) => {
    const from = minutesOf(s)
    return !windows.some(([start, end]) => from < end && start < from + length)
  })
}

/** `T2 26/10` for a `yyyy-MM-dd` date. */
export function dayText(date: string): string {
  const weekday = WEEKDAYS[new Date(`${date}T00:00:00Z`).getUTCDay()]
  return `${weekday} ${date.slice(8)}/${date.slice(5, 7)}`
}

export const shiftRange = (shift: Pick<ShiftOption, 'startLocal' | 'endLocal'>) => `${shift.startLocal} → ${shift.endLocal}`

export const workersPrice = (workers: number, unitPrice: number) => `${workers} × ${formatVnd(unitPrice)}`

// ---- Decisions D34: the customer picks the start time and the length of an Economy job inside the shift. ----

const minutesOf = (hhmm: string) => {
  const [h, m] = hhmm.split(':').map(Number)
  return h * 60 + m
}
const hhmm = (minutes: number) => `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`

type TimeRules = Partial<Pick<BookingOptions, 'shiftMaxHours' | 'minDurationHours' | 'durationStepHours' | 'startStepMinutes' | 'economyMinLeadMinutes'>>

/** The lengths offered: from `minDurationHours` (2) to `shiftMaxHours` (4) in `durationStepHours` (0.5). */
export function durationChoices(options: TimeRules): number[] {
  const min = options.minDurationHours ?? 2
  const max = options.shiftMaxHours ?? 4
  const step = options.durationStepHours && options.durationStepHours > 0 ? options.durationStepHours : 0.5
  const list: number[] = []
  for (let h = min; h <= max + 1e-9; h += step) list.push(Math.round(h * 10) / 10)
  return list
}

/** `2` -> `2 giờ`, `2.5` -> `2,5 giờ`. */
export const hoursLabel = (hours: number) => `${String(hours).replace('.', ',')} giờ`

/** End `HH:mm` of a job that starts at `start` and lasts `hours`. */
export const endTimeOf = (start: string, hours: number) => hhmm(minutesOf(start) + Math.round(hours * 60))

/** `08:30 → 11:30 (3 giờ)`. */
export const jobRange = (start: string, hours: number) => `${start} → ${endTimeOf(start, hours)} (${hoursLabel(hours)})`

/**
 * The start times a job of `hours` can take inside `shift` on `date`: on the start step (30 min), ending by the shift end, and at least
 * `economyMinLeadMinutes` (60) after `now`. Empty when the shift is too short or already too late.
 */
export function startTimeChoices(date: string, shift: ShiftOption, hours: number, options: TimeRules, now: Date): string[] {
  const step = options.startStepMinutes && options.startStepMinutes > 0 ? options.startStepMinutes : 30
  const lead = (options.economyMinLeadMinutes ?? 60) * 60_000
  const shiftStartMin = minutesOf(shift.startLocal)
  const shiftEndMin = minutesOf(shift.endLocal)
  const length = Math.round(hours * 60)
  const list: string[] = []
  for (let m = shiftStartMin; m + length <= shiftEndMin; m += step) {
    if (shiftStart(date, hhmm(m)).getTime() >= now.getTime() + lead) list.push(hhmm(m))
  }
  return list
}

/** D36: what the summary says about the workers; a large ECONOMY house is one worker at the single large-house price (no "x N" to confuse). */
export const quoteWorkersLabel = (quote: Pick<PriceQuote, 'requiredWorkers' | 'unitPrice' | 'totalAmount' | 'isContinuousWork'>) =>
  quote.isContinuousWork ? `1 thợ · giá nhà lớn ${formatVnd(quote.totalAmount)}` : workersPrice(quote.requiredWorkers, quote.unitPrice)

/** D36: the notice and the agreement for a large ECONOMY house. */
export const CONTINUOUS_WORK_NOTICE = 'Nhà bạn lớn nên thợ có thể cần làm 2 đơn liên tiếp: đơn 2 là làm thêm giờ ngay sau ca (tối đa 4 giờ). Bạn chỉ trả một lần, không phát sinh thêm.'
export const CONTINUOUS_WORK_AGREEMENT = 'Tôi đồng ý: nhà tôi do một thợ làm và có thể làm 2 đơn liên tiếp.'


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
  if (code === 'LEAD_TIME_TOO_SHORT') return 'Giờ này gần quá, thợ không kịp đến. Hãy chọn giờ muộn hơn.'
  if (code === 'PREFERRED_WORKER_BUSY') return 'Thợ bạn chọn đã bận giờ này. Chọn giờ khác hoặc để hệ thống chọn thợ.'
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
