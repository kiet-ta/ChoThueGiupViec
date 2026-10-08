// Pure logic of the absence approval page (no React, so `node --test` covers it).
import { formatDateTime, formatVnd, workerTypeLabel } from '../../../lib/format.ts'
import type { AbsenceReport, AbsenceStatus } from './types'

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger' | 'accent'

export const REASON_MAX_LENGTH = 255 // ADMIN_AUDIT_LOG.reason NVARCHAR(255), contract admin.md 2.2

export const TABS: { status: AbsenceStatus; label: string }[] = [
  { status: 'PENDING', label: 'Chờ duyệt' },
  { status: 'APPROVED', label: 'Đã duyệt' },
  { status: 'REJECTED', label: 'Từ chối' },
]

export function statusInfo(status: string): { label: string; tone: Tone } {
  switch (status) {
    case 'PENDING':
      return { label: 'Chờ duyệt', tone: 'warning' }
    case 'APPROVED':
      return { label: 'Đã duyệt', tone: 'success' }
    case 'REJECTED':
      return { label: 'Đã từ chối', tone: 'danger' }
    default:
      return { label: status, tone: 'neutral' }
  }
}

const BLOCK_REASONS: Record<string, string> = {
  GPS_NOT_VERIFIED: 'Chưa xác minh được vị trí GPS của thợ tại địa chỉ đơn.',
  CALLS_BELOW_MINIMUM: 'Thợ chưa gọi đủ 2 cuộc xác minh vắng mặt.',
  WAIT_BELOW_MINIMUM: 'Thợ chưa chờ đủ 15 phút kể từ lúc check-in.',
  ABSENCE_NOT_REPORTED: 'Thợ chưa bấm báo khách vắng mặt.',
}

/** The sentence an Admin reads for a `blockReasons` code; an unknown code is shown as it is. */
export function blockReasonText(code: string): string {
  return BLOCK_REASONS[code] ?? code
}

export interface ReportRow {
  assignmentId: number
  orderCode: string
  workerName: string
  workerKind: string
  customerName: string
  reportedAt: string
  gpsText: string
  gpsOk: boolean
  calls: number
  waitedText: string
  feeText: string
  status: { label: string; tone: Tone }
}

export function reportRow(r: AbsenceReport): ReportRow {
  return {
    assignmentId: r.assignmentId,
    orderCode: r.orderCode,
    workerName: r.workerName,
    workerKind: workerTypeLabel(r.workerType, r.agencyName),
    customerName: r.customerName,
    reportedAt: formatDateTime(r.customerAbsentAt),
    gpsText: r.gpsVerified ? `Đúng vị trí · ${distanceText(r.distanceM)}` : `Chưa xác minh · ${distanceText(r.distanceM)}`,
    gpsOk: r.gpsVerified,
    calls: r.callAttempts,
    waitedText: `${r.waitedMinutes} phút`,
    feeText: formatVnd(r.absenceFeeAmount),
    status: statusInfo(r.status),
  }
}

/** `12.5 m` below a kilometre, `1.2 km` above; one decimal at most, a dot as in the contract's numbers. */
export function distanceText(metres: number): string {
  if (metres >= 1000) return `${(metres / 1000).toFixed(1)} km`
  return `${Number.isInteger(metres) ? metres : metres.toFixed(1)} m`
}

/** What approving pays: the worker keeps the fee (40 %), the customer gets the rest back (60 %). */
export function moneySplitText(r: Pick<AbsenceReport, 'absenceFeeAmount' | 'customerRefundAmount'>): string {
  return `Thợ nhận ${formatVnd(r.absenceFeeAmount)} · hoàn ${formatVnd(r.customerRefundAmount)} cho khách`
}

/** The reason of a rejection must be 1-255 characters once trimmed; returns the message to show, or null when it is fine. */
export function validateReason(text: string): string | null {
  const trimmed = text.trim()
  if (trimmed.length === 0) return 'Vui lòng nhập lý do từ chối.'
  if (trimmed.length > REASON_MAX_LENGTH) return `Lý do tối đa ${REASON_MAX_LENGTH} ký tự (đang ${trimmed.length}).`
  return null
}

/** The part of an `ApiError` the page needs, so this file does not import the client (which reads `import.meta`). */
export interface FailedCall {
  status: number
  message: string
  data: unknown
  fieldErrors?: Record<string, string[]>
}

export interface ActionError {
  message: string
  /** Why an approval is refused (a 409), in words; empty otherwise. */
  reasons: string[]
}

function blockReasonsOf(data: unknown): string[] {
  if (typeof data !== 'object' || data === null) return []
  const list = (data as { blockReasons?: unknown }).blockReasons
  return Array.isArray(list) ? list.filter((x): x is string => typeof x === 'string') : []
}

/** Turns a failed approve or reject call into what the Admin should read (contract admin.md 2.2: 400, 404, 409, 502). */
export function actionError(e: FailedCall): ActionError {
  const reasons = blockReasonsOf(e.data).map(blockReasonText)
  switch (e.status) {
    case 400:
      return { message: e.fieldErrors?.reason?.[0] ?? (e.message || 'Dữ liệu không hợp lệ.'), reasons: [] }
    case 404:
      return { message: 'Không tìm thấy báo cáo này.', reasons: [] }
    case 409:
      return reasons.length > 0
        ? { message: 'Chưa đủ điều kiện để duyệt:', reasons }
        : { message: 'Báo cáo đã được xử lý hoặc ca làm không còn ở trạng thái check-in. Hãy tải lại danh sách.', reasons: [] }
    case 502:
      return { message: 'Không hoàn được tiền cho khách (cổng hoàn tiền từ chối). Chưa có gì thay đổi, có thể thử lại.', reasons: [] }
    case 0:
      return { message: 'Không kết nối được máy chủ.', reasons: [] }
    default:
      return { message: e.message || `Lỗi ${e.status}`, reasons: [] }
  }
}
