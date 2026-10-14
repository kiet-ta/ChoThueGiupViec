// Pure logic of the Partner Portal pages (no React, so `node --test` covers it).
import { formatDateTime, formatVnd } from '../../lib/format.ts'
import type {
  AgencyJob,
  EscrowState,
  EscrowTransaction,
  ImportRowError,
  RosterEntry,
  SubscriptionPackage,
  SubscriptionState,
  SwapWindow,
} from './types'

// ---- codes of `data.code` (409) and of rows, in words

const ERROR_TEXT: Record<string, string> = {
  PACKAGE_NOT_PURCHASABLE: 'Gói này không thể mua trực tiếp.',
  AGENCY_SUSPENDED: 'Doanh nghiệp đang bị tạm ngưng, hãy nạp ký quỹ trước.',
  WORKER_QUOTA_EXCEEDED: 'Vượt số thợ tối đa của gói hiện tại.',
  GUARANTEE_REQUIRED: 'Cần tải lên cam kết bảo lãnh đã ký trước khi import thợ.',
  IMPORT_INVALID: 'File có dòng lỗi, chưa tạo thợ nào.',
  FEATURE_NOT_IN_PLAN: 'Tính năng này thuộc gói Pro.',
  SLOT_NOT_EDITABLE: 'Ca này đang được giữ hoặc đã có đơn, không sửa được.',
  APPEAL_WINDOW_CLOSED: 'Đã quá hạn khiếu nại (48 giờ).',
  APPEAL_EXISTS: 'Khoản phạt này đã được khiếu nại hoặc đã hoàn lại.',
  SWAP_WINDOW_CLOSED: 'Cửa sổ 30 phút đã kết thúc, hệ thống đã chuyển sang cứu hộ.',
  SWAP_WINDOW_NOT_OPEN: 'Chưa mở cửa sổ đổi thợ cho ca này.',
  WORKER_NOT_OF_AGENCY: 'Thợ này không thuộc doanh nghiệp của bạn.',
  WORKER_NOT_AVAILABLE: 'Thợ này đang bị khoá hoặc chưa kích hoạt.',
  WORKER_HAS_NO_FREE_SLOT: 'Thợ này chưa có ca trống cho khung giờ đó (hãy xếp vào lịch ca trước).',
  SAME_WORKER: 'Hãy chọn một thợ khác.',
  SLOT_TAKEN: 'Ca vừa bị thay đổi, hãy thử lại.',
  ASSIGNMENT_STATE_INVALID: 'Ca làm không còn ở trạng thái đã nhận.',
}

const ROW_ERROR_TEXT: Record<string, string> = {
  REQUIRED: 'Bắt buộc',
  TOO_LONG: 'Quá dài',
  INVALID_FORMAT: 'Sai định dạng',
  DUPLICATE_IN_FILE: 'Trùng trong file',
  ALREADY_EXISTS: 'Đã tồn tại',
  MISSING_COLUMN: 'Thiếu cột',
  QUOTA_EXCEEDED: 'Vượt số thợ tối đa',
}

const COLUMN_TEXT: Record<string, string> = {
  full_name: 'Họ tên',
  phone_number: 'Số điện thoại',
  national_id: 'CCCD',
  bank_name: 'Ngân hàng',
  bank_account_no: 'Số tài khoản',
}

/** The words for a business `data.code` of a 409, or null so the caller falls back to the server's message. */
export function codeMessage(code: string | null | undefined): string | null {
  return code ? (ERROR_TEXT[code] ?? null) : null
}

/** Reads `data.code` of an ApiError-like value. */
export function errorCode(data: unknown): string | null {
  if (data && typeof data === 'object' && 'code' in data && typeof (data as { code: unknown }).code === 'string') {
    return (data as { code: string }).code
  }
  return null
}

// ---- WEB-M5-01 subscription

export function tierLabel(tier: string): string {
  return tier === 'PRO' ? 'Pro' : tier === 'FREE' ? 'Free' : tier
}

export function cycleLabel(cycle: string): string {
  if (cycle === 'MONTHLY') return 'tháng'
  if (cycle === 'QUARTERLY') return 'quý'
  return ''
}

/** `3 / 50 thợ` and whether the quota is used up. */
export function quotaText(state: Pick<SubscriptionState, 'workerCount' | 'workerQuota'>): { text: string; full: boolean } {
  return { text: `${state.workerCount} / ${state.workerQuota} thợ`, full: state.workerCount >= state.workerQuota }
}

export function priceText(pkg: Pick<SubscriptionPackage, 'price' | 'billingCycle'>): string {
  if (pkg.price <= 0) return 'Miễn phí'
  const cycle = cycleLabel(pkg.billingCycle)
  return cycle ? `${formatVnd(pkg.price)} / ${cycle}` : formatVnd(pkg.price)
}

export function commissionText(rate: number): string {
  return `${Math.round(rate * 1000) / 10}%`
}

export function featureList(pkg: Pick<SubscriptionPackage, 'hasRosterDashboard' | 'hasAnalytics' | 'priorityDispatch'>): string[] {
  const out: string[] = []
  if (pkg.hasRosterDashboard) out.push('Bảng phân ca theo tuần')
  if (pkg.hasAnalytics) out.push('Báo cáo hiệu suất nhân viên')
  if (pkg.priorityDispatch) out.push('Ưu tiên điều phối')
  return out
}

export function subscriptionStatusText(state: SubscriptionState): string {
  if (!state.subscription) return 'Đang dùng gói Free'
  const end = state.subscription.endDate.split('-').reverse().join('/')
  return state.inGrace ? `Gói Pro đã hết hạn ngày ${end}, còn trong thời gian gia hạn 7 ngày` : `Gói Pro đến hết ngày ${end}`
}

/** Days before the end date from which the page warns that the package must be bought again (decisions D16: 7 and 1 day). */
export const EXPIRY_WARNING_DAYS = 7

/**
 * A warning while a Pro package is within a week of its end date (a package is bought by hand and never renews itself), otherwise null.
 * <paramref name="today"/> is a local date `yyyy-MM-dd`.
 */
export function expiryWarning(state: SubscriptionState, today: string): string | null {
  const sub = state.subscription
  if (!sub || state.inGrace || sub.subStatus !== 'ACTIVE') return null
  const days = Math.round((Date.parse(`${sub.endDate}T00:00:00Z`) - Date.parse(`${today}T00:00:00Z`)) / 86_400_000)
  if (days < 0 || days > EXPIRY_WARNING_DAYS) return null
  const end = sub.endDate.split('-').reverse().join('/')
  const left = days === 0 ? 'hết hạn hôm nay' : days === 1 ? 'còn 1 ngày' : `còn ${days} ngày`
  return `Gói Pro ${left} (đến hết ngày ${end}). Gói không tự gia hạn: hãy mua lại để nối tiếp, nếu không thợ vượt hạn mức Free sẽ bị khoá sau 7 ngày gia hạn.`
}

// ---- WEB-M5-02 import

export interface ErrorGroup {
  row: number
  title: string
  items: { column: string; text: string }[]
}

/** Row errors grouped by sheet row, in row order; row 0 (the whole file) first and titled "Cả file". */
export function groupImportErrors(errors: ImportRowError[]): ErrorGroup[] {
  const byRow = new Map<number, ImportRowError[]>()
  for (const e of errors) byRow.set(e.row, [...(byRow.get(e.row) ?? []), e])
  return [...byRow.entries()]
    .sort(([a], [b]) => a - b)
    .map(([row, list]) => ({
      row,
      title: row === 0 ? 'Cả file' : `Dòng ${row}`,
      items: list.map((e) => ({
        column: COLUMN_TEXT[e.column] ?? e.column,
        text: `${ROW_ERROR_TEXT[e.code] ?? e.code}${e.message ? `: ${e.message}` : ''}`,
      })),
    }))
}

/** The `.xlsx` file the import accepts: false for another type or an empty file. */
export function isXlsx(file: { name: string; size: number }): boolean {
  return file.size > 0 && file.name.toLowerCase().endsWith('.xlsx')
}

export function isPdf(file: { name: string; size: number }): boolean {
  return file.size > 0 && file.name.toLowerCase().endsWith('.pdf')
}

// ---- WEB-M5-03 roster

export const SHIFTS = [
  { code: 'SHIFT_MORNING', label: 'Ca sáng', time: '08:00–12:00' },
  { code: 'SHIFT_AFTERNOON', label: 'Ca chiều', time: '13:00–17:00' },
  { code: 'SHIFT_EVENING', label: 'Ca tối', time: '17:30–20:30' },
] as const

export function shiftLabel(code: string): string {
  return SHIFTS.find((s) => s.code === code)?.label ?? code
}

/** Monday (as `yyyy-MM-dd`) of the week of `date`; pure calendar arithmetic, no time zone. */
export function mondayOf(date: string): string {
  const [y, m, d] = date.split('-').map(Number)
  const utc = new Date(Date.UTC(y, m - 1, d))
  const shift = (utc.getUTCDay() + 6) % 7
  utc.setUTCDate(utc.getUTCDate() - shift)
  return utc.toISOString().slice(0, 10)
}

export function addDays(date: string, days: number): string {
  const [y, m, d] = date.split('-').map(Number)
  const utc = new Date(Date.UTC(y, m - 1, d + days))
  return utc.toISOString().slice(0, 10)
}

export function weekDays(monday: string): string[] {
  return Array.from({ length: 7 }, (_, i) => addDays(monday, i))
}

const WEEKDAYS = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN']

/** `T2 12/10` for a day of the week view. */
export function dayLabel(date: string, monday: string): string {
  const index = weekDays(monday).indexOf(date)
  const [, m, d] = date.split('-')
  return `${index >= 0 ? WEEKDAYS[index] : ''} ${d}/${m}`.trim()
}

export interface RosterCell {
  slotId: number
  workerName: string
  workerId: number
  status: string
  removable: boolean
}

/** The grid of one week: key `${date}|${shiftCode}` -> the workers on that shift. A HELD or BOOKED slot is not removable. */
export function rosterGrid(entries: RosterEntry[]): Map<string, RosterCell[]> {
  const grid = new Map<string, RosterCell[]>()
  for (const e of entries) {
    const key = `${e.slotDate}|${e.shiftCode}`
    grid.set(key, [
      ...(grid.get(key) ?? []),
      { slotId: e.slotId, workerName: e.workerName, workerId: e.workerId, status: e.slotStatus, removable: e.slotStatus === 'AVAILABLE' },
    ])
  }
  return grid
}

export function slotStatusText(status: string): string {
  if (status === 'AVAILABLE') return 'Trống'
  if (status === 'HELD') return 'Đang giữ cho đơn'
  if (status === 'BOOKED') return 'Đã có đơn'
  if (status === 'BLOCKED') return 'Thợ nghỉ'
  return status
}

// ---- WEB-M5-04 escrow

export function agencyStatusText(status: string): string {
  if (status === 'ACTIVE') return 'Đang hoạt động'
  if (status === 'PENDING') return 'Chờ đủ ký quỹ tối thiểu'
  if (status === 'SUSPENDED') return 'Tạm ngưng, cần nạp ký quỹ'
  return status
}

export function escrowAlerts(state: EscrowState): string[] {
  const out: string[] = []
  if (state.hasEscrowWarning) {
    out.push(`Số dư thấp hơn mức tối thiểu ${formatVnd(state.minEscrowBalance)}. Hãy nạp thêm.`)
  }
  if (state.agencyStatus === 'SUSPENDED') out.push('Doanh nghiệp đang tạm ngưng nhận đơn Premium cho đến khi nạp đủ.')
  if (state.excludedFromPremium && state.agencyStatus === 'ACTIVE') {
    out.push('Điểm SLA dưới 50: không nhận đơn Premium. Chỉ Admin điều chỉnh được điểm.')
  } else if (state.hasSlaWarning) {
    out.push('Điểm SLA từ 70 trở xuống: nguy cơ bị loại khỏi đơn Premium.')
  }
  return out
}

export function txnTypeText(type: string): string {
  if (type === 'DEPOSIT') return 'Nạp ký quỹ'
  if (type === 'PENALTY') return 'Khấu trừ phạt'
  if (type === 'REVERSAL') return 'Hoàn lại sau khiếu nại'
  return type
}

export interface TxnRow {
  id: number
  typeText: string
  amountText: string
  balanceText: string
  pointsText: string
  reason: string
  when: string
  canAppeal: boolean
}

export function txnRow(t: EscrowTransaction): TxnRow {
  const sign = t.txnType === 'PENALTY' ? '-' : '+'
  return {
    id: t.escrowTxnId,
    typeText: txnTypeText(t.txnType),
    amountText: `${sign}${formatVnd(t.amount)}`,
    balanceText: formatVnd(t.balanceAfter),
    pointsText: t.slaPointsDelta ? `${t.slaPointsDelta > 0 ? '+' : ''}${t.slaPointsDelta}` : '',
    reason: t.reason,
    when: formatDateTime(t.createdAt),
    canAppeal: t.canAppeal,
  }
}

/** Whole positive VND typed with dots, spaces or commas: `5.000.000` -> 5000000; null otherwise. */
export function parseAmount(text: string): number | null {
  const digits = text.trim().replace(/[.\s,]/g, '')
  if (!/^\d{1,15}$/.test(digits)) return null
  const value = Number(digits)
  return value > 0 ? value : null
}

// ---- WEB-M5-05 jobs and report

export function jobStatusText(status: string): string {
  const map: Record<string, string> = {
    OFFERED: 'Đang chào thợ',
    ASSIGNED: 'Đã nhận ca',
    CHECKED_IN: 'Đã đến nơi',
    IN_PROGRESS: 'Đang làm',
    AWAITING_ACCEPTANCE: 'Chờ khách nghiệm thu',
    COMPLETED: 'Hoàn thành',
    CANCELLED: 'Đã huỷ',
    CANCELLED_BY_WORKER: 'Thợ huỷ',
    ABSENT: 'Khách vắng',
    INCIDENT: 'Sự cố',
    REASSIGNED: 'Đã chuyển thợ',
  }
  return map[status] ?? status
}

export function jobRow(job: AgencyJob) {
  return {
    id: job.assignmentId,
    orderId: job.orderId,
    worker: job.workerName || `Thợ #${job.workerId}`,
    statusText: jobStatusText(job.assignmentStatus),
    gross: formatVnd(job.grossAmount),
    payout: formatVnd(job.payoutAmount),
    created: formatDateTime(job.createdAt),
  }
}

export function percent(rate: number): string {
  return `${Math.round(rate * 100)}%`
}

// ---- WEB-M5-06 swap window

/** `29:41` for the seconds left; `00:00` once over. */
export function formatCountdown(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds))
  return `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`
}

/** Seconds left to a deadline given the instant the page last read it, so the countdown ticks without asking the server again. */
export function secondsLeft(window: Pick<SwapWindow, 'deadlineAt'>, nowMs: number): number {
  return Math.max(0, Math.floor((new Date(window.deadlineAt).getTime() - nowMs) / 1000))
}

export function swapStateText(state: string): string {
  if (state === 'OPEN') return 'Đang trong 30 phút tự đổi thợ'
  if (state === 'SWAPPED') return 'Đã đổi thợ'
  if (state === 'RESCUE_STARTED') return 'Đã chuyển cho điều phối cứu hộ'
  return state
}
