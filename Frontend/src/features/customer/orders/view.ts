// Pure logic of the order list, order detail and extension screens (no React, so `node --test` covers it).
// Rules: .spec/contracts/booking.md 1.1 (order states), 3.7 (cancel), 3.8 (extension), decisions B5/B11; ratings 48 h, disputes 24 h.
import type { Extension, ProgressAssignment } from './types.ts'

export const POLL_PROGRESS_MS = 5000
export const CANCEL_REASON_MAX = 255
export const RATING_WINDOW_HOURS = 48
export const DISPUTE_WINDOW_HOURS = 24

export interface StatusTab {
  label: string
  /** `status` query of GET /booking/orders; null = every order. */
  status: string | null
}

// The list endpoint filters by one order status. "In progress" / "awaiting acceptance" are assignment states, not order states,
// so they are not tabs here (and tab counts need an API that does not exist).
export const STATUS_TABS: StatusTab[] = [
  { label: 'Tất cả', status: null },
  { label: 'Chờ thanh toán', status: 'PENDING_PAYMENT' },
  { label: 'Đang tìm thợ', status: 'DISPATCHING' },
  { label: 'Có thợ', status: 'ASSIGNED' },
  { label: 'Hoàn tất', status: 'COMPLETED' },
  { label: 'Đã hủy', status: 'CANCELLED' },
]

const ASSIGNMENT_LABELS: Record<string, string> = {
  ASSIGNED: 'Đã nhận ca',
  CHECKED_IN: 'Đã đến nơi',
  IN_PROGRESS: 'Đang làm',
  AWAITING_ACCEPTANCE: 'Chờ nghiệm thu',
  COMPLETED: 'Hoàn tất',
  CANCELLED_BY_WORKER: 'Thợ đã hủy',
  ABSENT: 'Vắng mặt',
  INCIDENT: 'Sự cố',
}
export const assignmentLabel = (status: string) => ASSIGNMENT_LABELS[status] ?? status

const EXTENSION_LABELS: Record<string, string> = {
  PENDING_PAYMENT: 'Chờ thanh toán',
  PAID: 'Đã thanh toán, chờ thợ đồng ý',
  ACCEPTED: 'Thợ đã đồng ý',
  DECLINED: 'Thợ từ chối, đã hoàn tiền',
  EXPIRED: 'Hết hạn thanh toán',
}
export const extensionLabel = (status: string) => EXTENSION_LABELS[status] ?? status

const ON_SITE = ['IN_PROGRESS', 'AWAITING_ACCEPTANCE']
const WORK_STARTED = ['IN_PROGRESS', 'AWAITING_ACCEPTANCE', 'COMPLETED']

export interface OrderActions {
  canPay: boolean
  canCancel: boolean
  canExtend: boolean
  showAcceptance: boolean
  showRating: boolean
  showDispute: boolean
}

/**
 * What the customer may do on an order, from the order status, its visible assignments and the extension.
 * Cancel (3.7): not COMPLETED / CANCELLED, and not once the work has started (except PENDING_PAYMENT / PAID, which have no worker).
 * Extension (3.8): order ASSIGNED, a worker on site, no extension yet (one per order).
 * After the shift: acceptance while a worker waits for it; rating within 48 h and dispute within 24 h of a completed assignment.
 */
export function orderActions(
  orderStatus: string,
  assignments: ProgressAssignment[],
  extension: Extension | null,
  now: Date,
): OrderActions {
  const started = assignments.some((a) => WORK_STARTED.includes(a.assignmentStatus))
  const cancellableStatus = ['PENDING_PAYMENT', 'PAID', 'DISPATCHING', 'ASSIGNED'].includes(orderStatus)
  const noWorkerYet = orderStatus === 'PENDING_PAYMENT' || orderStatus === 'PAID'
  const completedAt = assignments
    .filter((a) => a.assignmentStatus === 'COMPLETED' && a.completedAt)
    .map((a) => new Date(a.completedAt as string).getTime())
  const within = (hours: number) => completedAt.some((t) => now.getTime() - t <= hours * 3600_000)
  return {
    canPay: orderStatus === 'PENDING_PAYMENT',
    canCancel: cancellableStatus && (noWorkerYet || !started),
    canExtend: orderStatus === 'ASSIGNED' && extension === null && assignments.some((a) => ON_SITE.includes(a.assignmentStatus)),
    showAcceptance: assignments.some((a) => a.assignmentStatus === 'AWAITING_ACCEPTANCE'),
    showRating: within(RATING_WINDOW_HOURS),
    showDispute: within(DISPUTE_WINDOW_HOURS),
  }
}

export type StepState = 'done' | 'current' | 'todo' | 'cancelled'
export interface TimelineStep {
  key: 'paid' | 'dispatch' | 'assigned' | 'completed'
  label: string
  state: StepState
}

/** The 4-step timeline (Thanh toán, Tìm thợ, Có thợ, Hoàn tất) for an order status. A cancelled order marks the step it stopped at. */
export function timeline(orderStatus: string, cancelledAfterPayment = false, assignments: ProgressAssignment[] = []): TimelineStep[] {
  const keys: TimelineStep['key'][] = ['paid', 'dispatch', 'assigned', 'completed']
  const assignedLabel = assignments.some((a) => a.assignmentStatus === 'AWAITING_ACCEPTANCE') ? 'Chờ bạn nghiệm thu'
    : assignments.some((a) => a.assignmentStatus === 'IN_PROGRESS') ? 'Thợ đang làm' : 'Có thợ, chờ đến giờ làm'
  const labels = { paid: 'Thanh toán', dispatch: 'Tìm thợ', assigned: assignedLabel, completed: 'Hoàn tất' }
  const reached: Record<string, number> = { PENDING_PAYMENT: 0, PAID: 1, DISPATCHING: 1, ASSIGNED: 2, COMPLETED: 4 }
  if (orderStatus === 'CANCELLED') {
    const stop = cancelledAfterPayment ? 1 : 0
    return keys.map((key, i) => ({ key, label: labels[key], state: i < stop ? 'done' : i === stop ? 'cancelled' : 'todo' }))
  }
  const index = reached[orderStatus] ?? 0
  return keys.map((key, i) => ({ key, label: labels[key], state: i < index ? 'done' : i === index ? 'current' : 'todo' }))
}

/** 0.5 to `maxHours` (4) in 0.5 steps (decision B11). */
export function extensionHourOptions(maxHours = 4): number[] {
  return Array.from({ length: Math.round(maxHours * 2) }, (_, i) => (i + 1) / 2)
}

/** `1,5 giờ` (Vietnamese decimal comma). */
export const hoursText = (hours: number) => `${String(hours).replace('.', ',')} giờ`

const VN_OFFSET_MS = 7 * 3600_000

/** `13:30` in Asia/Ho_Chi_Minh for an ISO instant; empty for an invalid value. */
export function vnTime(iso: string): string {
  const t = new Date(iso).getTime()
  if (Number.isNaN(t)) return ''
  return new Date(t + VN_OFFSET_MS).toISOString().slice(11, 16)
}

/** `12:00 → 13:30`: the shift end and the end after `hours` more. */
export function newEndText(shiftEndAt: string, hours: number): string {
  const end = new Date(shiftEndAt).getTime()
  if (Number.isNaN(end)) return ''
  return `${vnTime(shiftEndAt)} → ${vnTime(new Date(end + hours * 3600_000).toISOString())}`
}

/** Validation of the cancel reason (1-255 characters after trimming, contract 3.7). */
export function cancelReasonError(reason: string): string | null {
  const text = reason.trim()
  if (!text) return 'Hãy cho biết lý do hủy.'
  if (text.length > CANCEL_REASON_MAX) return `Lý do tối đa ${CANCEL_REASON_MAX} ký tự.`
  return null
}

/** Text of a failed cancel or extension request: 409 codes of the contract, field messages of a 400, the server message otherwise. */
export function orderProblem(error: { status?: number; message?: string; data?: unknown; fieldErrors?: Record<string, string[]> }): string {
  if (error.status === 400) {
    const first = Object.values(error.fieldErrors ?? {}).flat()[0]
    if (first) return first
  }
  const code = error.data && typeof error.data === 'object' ? (error.data as { code?: unknown }).code : undefined
  if (code === 'EXTENSION_EXISTS') return 'Đơn này đã có một đề nghị làm thêm.'
  if (code === 'INVALID_STATE') return 'Đơn không còn ở trạng thái cho phép thao tác này. Trang đã được tải lại.'
  if (error.status === 404) return 'Không tìm thấy đơn.'
  return error.message || 'Đã có lỗi xảy ra. Vui lòng thử lại.'
}

/** Number of pages for `total` items. */
export const pageCount = (total: number, pageSize: number) => Math.max(1, Math.ceil(total / pageSize))
