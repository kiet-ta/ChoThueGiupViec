// Pure logic of the order list, order detail and extension screens (no React, so `node --test` covers it).
// Rules: .spec/contracts/booking.md 1.1 (order states), 3.7 (cancel), 3.8 (extension), decisions B5/B11; ratings 48 h, disputes 24 h.
import type { Extension, ProgressAssignment } from './types.ts'

export const POLL_PROGRESS_MS = 5000
export const CANCEL_REASON_MAX = 255
export const RATING_WINDOW_HOURS = 48
export const DISPUTE_WINDOW_HOURS = 24

/** Customer-facing reason for a cancelled order. */
export function cancelReasonText(order: { cancelReason: string | null }): string {
  const reason = order.cancelReason?.trim()
  if (!reason) return 'Đơn đã được hủy.'
  if (reason.startsWith('Quá bán kính') || reason.startsWith('Không tìm thấy thợ')) {
    return 'Hiện chưa có thợ rảnh ở khu vực và ca bạn chọn nên hệ thống đã tự hủy đơn. Bạn không bị trừ tiền: khoản đã thanh toán được hoàn 100%. Hãy thử chọn ca hoặc ngày khác.'
  }
  return order.cancelReason as string
}

/** A refunded extension does not imply that the order payment itself was refunded. */
export function hasOrderRefund(payments: { purpose: string; txnStatus: string }[]): boolean {
  return payments.some((payment) => payment.purpose === 'ORDER' && payment.txnStatus === 'REFUNDED')
}

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
  /** No assignment waits for acceptance and at least one is COMPLETED: the customer can look back at the accepted photos. */
  showAcceptanceReview: boolean
  showRating: boolean
  showDispute: boolean
}

/**
 * What the customer may do on an order, from the order status, its visible assignments and the extension.
 * Cancel (3.7): not COMPLETED / CANCELLED, and not once the work has started (except PENDING_PAYMENT / PAID, which have no worker).
 * Extension (3.8): order ASSIGNED, a worker on site, no extension yet (one per order).
 * After the shift: acceptance while a worker waits for it, then a review of the accepted shift; rating within 48 h and dispute within 24 h of a completed assignment.
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
  const awaiting = assignments.some((a) => a.assignmentStatus === 'AWAITING_ACCEPTANCE')
  const within = (hours: number) => completedAt.some((t) => now.getTime() - t <= hours * 3600_000)
  return {
    canPay: orderStatus === 'PENDING_PAYMENT',
    canCancel: cancellableStatus && (noWorkerYet || !started),
    canExtend: orderStatus === 'ASSIGNED' && extension === null && assignments.some((a) => ON_SITE.includes(a.assignmentStatus)),
    showAcceptance: awaiting,
    showAcceptanceReview: !awaiting && assignments.some((a) => a.assignmentStatus === 'COMPLETED'),
    showRating: within(RATING_WINDOW_HOURS),
    showDispute: within(DISPUTE_WINDOW_HOURS),
  }
}

export type StepState = 'done' | 'current' | 'todo' | 'cancelled'
export interface TimelineStep {
  key: 'paid' | 'dispatch' | 'assigned' | 'arrived' | 'working' | 'acceptance' | 'completed'
  label: string
  state: StepState
  /** A short line under the current step. */
  hint?: string
}

/** Decision D18: the work starts with the worker's first accepted BEFORE photo, not when they arrive (#659). */
export const ARRIVED_HINT = 'Ca bắt đầu khi thợ chụp ảnh trước khi dọn.'

const TIMELINE: { key: TimelineStep['key']; label: string }[] = [
  { key: 'paid', label: 'Thanh toán' },
  { key: 'dispatch', label: 'Tìm thợ' },
  { key: 'assigned', label: 'Đã có thợ' },
  { key: 'arrived', label: 'Thợ đã đến' },
  { key: 'working', label: 'Thợ đang làm' },
  { key: 'acceptance', label: 'Chờ nghiệm thu' },
  { key: 'completed', label: 'Hoàn tất' },
]

/** How far an assignment got on the timeline (index of its step); unknown statuses do not move it. */
const ASSIGNMENT_STEP: Record<string, number> = { ASSIGNED: 2, CHECKED_IN: 3, IN_PROGRESS: 4, AWAITING_ACCEPTANCE: 5, COMPLETED: 5 }

/**
 * The 7-step timeline (CUST-ARRIVAL-01): Thanh toán, Tìm thợ, Đã có thợ, Thợ đã đến, Thợ đang làm, Chờ nghiệm thu, Hoàn tất.
 * An ASSIGNED order is at the step of its most advanced visible assignment. A cancelled order marks the step it stopped at.
 */
export function timeline(orderStatus: string, cancelledAfterPayment = false, assignments: ProgressAssignment[] = []): TimelineStep[] {
  if (orderStatus === 'CANCELLED') {
    const stop = cancelledAfterPayment ? 1 : 0
    return TIMELINE.map((s, i) => ({ ...s, state: i < stop ? 'done' : i === stop ? 'cancelled' : 'todo' }))
  }
  const base: Record<string, number> = { PENDING_PAYMENT: 0, PAID: 1, DISPATCHING: 1, AWAITING_FALLBACK: 1, ASSIGNED: 2, COMPLETED: TIMELINE.length }
  let index = base[orderStatus] ?? 0
  if (orderStatus === 'ASSIGNED') index = Math.max(index, ...assignments.map((a) => ASSIGNMENT_STEP[a.assignmentStatus] ?? 2))
  return TIMELINE.map((s, i) => {
    const state: StepState = i < index ? 'done' : i === index ? 'current' : 'todo'
    return s.key === 'arrived' && state === 'current' ? { ...s, state, hint: ARRIVED_HINT } : { ...s, state }
  })
}

/** `GET /dispatch/assignments/{id}/check-in` (CUST-ARRIVAL-01). */
export interface ArrivalStatus {
  assignmentId: number
  status: string
  awaitingCustomerConfirmation: boolean
  distanceMeters: number | null
  attemptedAt: string | null
}

/** The sentence of the "worker says they arrived" card. */
export function arrivalText(workerName: string | null, distanceMeters: number | null): string {
  const who = workerName?.trim() ? `Thợ ${workerName.trim()}` : 'Thợ'
  const far = distanceMeters !== null && Number.isFinite(distanceMeters) ? ` (cách khoảng ${new Intl.NumberFormat('vi-VN').format(Math.round(distanceMeters))} m)` : ''
  return `${who} báo đã đến nhưng vị trí GPS chưa khớp địa chỉ${far}.`
}

/** The assignments of an ASSIGNED order whose check-in the customer may have to confirm. */
export function arrivalCandidates(orderStatus: string, assignments: ProgressAssignment[]): number[] {
  return orderStatus === 'ASSIGNED' ? assignments.filter((a) => a.assignmentStatus === 'ASSIGNED').map((a) => a.assignmentId) : []
}

/** Error of the confirmation, in Vietnamese. */
export function arrivalProblem(error: { status?: number; message?: string }): string {
  if (error.status === 409) return 'Thợ chưa báo đến nơi hoặc đã được xác nhận.'
  if (error.status === 404) return 'Không tìm thấy ca làm này.'
  return 'Không xác nhận được. Vui lòng thử lại.'
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

// ---------- BR-02 fallback (dispatch.md 3.2): one worker found for a 2-worker order ----------
/** `GET /dispatch/orders/{id}/fallback`; `state` = AWAITING_CUSTOMER | AWAITING_WORKER | NOT_WAITING (other fields null then). */
export interface FallbackStatus {
  orderId: number
  state: string
  workerId: number | null
  secondShiftCode: string | null
  secondShiftDate: string | null
  deadlineUtc: string | null
}
/** `POST /dispatch/orders/{id}/fallback/respond`; `outcome` = WORKER_ASKED | CANCELLED. */
export interface FallbackAnswer {
  orderId: number
  outcome: string
  workerDeadlineUtc: string | null
  message: string | null
}

/** Only an order in AWAITING_FALLBACK can wait for the customer's choice. */
export const hasFallback = (orderStatus: string) => orderStatus === 'AWAITING_FALLBACK'

/** `13:30, 15/10` in Vietnam time; empty for a missing or invalid value. */
export function vnDateTime(iso: string | null): string {
  const t = iso ? new Date(iso).getTime() : NaN
  if (Number.isNaN(t)) return ''
  const d = new Date(t + VN_OFFSET_MS).toISOString()
  return `${d.slice(11, 16)}, ${d.slice(8, 10)}/${d.slice(5, 7)}`
}

/** The question of the card; `shift` is the label of `secondShiftCode`, `date` is `yyyy-MM-dd`. */
export function fallbackQuestion(shift: string, date: string | null): string {
  const day = date && /^\d{4}-\d{2}-\d{2}$/.test(date) ? ` ngày ${date.slice(8, 10)}/${date.slice(5, 7)}` : ''
  return `Thợ đã nhận đơn có thể làm luôn ${shift}${day} ngay sau ca đã nhận (2 ca liên tiếp). Nếu không đồng ý, đơn sẽ được hủy và hoàn tiền 100 %.`
}

export function fallbackDeadlineText(state: string, deadlineUtc: string | null): string {
  const at = vnDateTime(deadlineUtc)
  if (state === 'AWAITING_WORKER') return `Thợ có${at ? ` đến ${at}` : ' 2 phút'} để xác nhận ca thứ hai; nếu không, đơn sẽ được hủy và hoàn tiền 100 %.`
  return `Hãy trả lời${at ? ` trước ${at}` : ' trước giờ bắt đầu ca'}. Nếu không trả lời, đơn sẽ tự hủy và hoàn tiền 100 %.`
}

export function fallbackOutcomeText(outcome: string): string {
  if (outcome === 'WORKER_ASKED') return 'Đã gửi đề nghị. Thợ có 2 phút để xác nhận ca thứ hai.'
  if (outcome === 'CANCELLED') return 'Đơn đã được hủy và hoàn tiền 100 %.'
  return 'Đã ghi nhận lựa chọn của bạn.'
}

/** Text of a failed answer: the 409 codes of the contract, 404, the server message otherwise. */
export function fallbackProblem(error: { status?: number; message?: string; data?: unknown }): string {
  const code = error.data && typeof error.data === 'object' ? (error.data as { code?: unknown }).code : undefined
  if (code === 'FALLBACK_NOT_WAITING') return 'Lựa chọn này không còn hiệu lực. Trang đã được tải lại.'
  if (code === 'FALLBACK_ALREADY_ANSWERED') return 'Bạn đã đồng ý rồi, đang chờ thợ xác nhận ca thứ hai.'
  if (error.status === 404) return 'Không tìm thấy đơn.'
  return error.message || 'Đã có lỗi xảy ra. Vui lòng thử lại.'
}

/** Number of pages for `total` items. */
export const pageCount = (total: number, pageSize: number) => Math.max(1, Math.ceil(total / pageSize))
