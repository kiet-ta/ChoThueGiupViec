import { formatDateTime } from '../../lib/format.ts'
import type { EkycFilterStatus, EkycQueueItemResponse, EkycReviewRequest } from './types'

export type StatusTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger' | 'accent'

export const PAGE_SIZE = 20
export const REASON_MAX_LENGTH = 255
export const AUTO_APPROVE_THRESHOLD = 85.0

export interface EkycRow {
  workerId: number
  fullName: string
  phoneNumber: string
  nationalId: string
  confidenceScore: number | null
  confidenceText: string
  confidenceTone: StatusTone
  submittedAtFormatted: string
  raw: EkycQueueItemResponse
}

export interface FailedCall {
  status: number
  message: string
  data: unknown
  fieldErrors?: Record<string, string[]>
}

export interface ActionError {
  message: string
  reasons: string[]
}

export const TABS: { status: EkycFilterStatus; label: string }[] = [
  { status: 'PENDING', label: 'Chờ hậu kiểm' },
  { status: 'APPROVED', label: 'Đã duyệt' },
  { status: 'REJECTED', label: 'Từ chối' },
]

/** A worker who has not been scored (no eKYC submitted yet) has a null score: never shown as a number. */
export const NO_SCORE_LABEL = 'Chưa có điểm'

export function confidenceInfo(score: number | null): { label: string; tone: StatusTone } {
  if (score == null) {
    return { label: NO_SCORE_LABEL, tone: 'neutral' }
  }
  if (score >= AUTO_APPROVE_THRESHOLD) {
    return { label: `${score.toFixed(1)}% (Tự động duyệt)`, tone: 'success' }
  }
  if (score >= 70.0) {
    return { label: `${score.toFixed(1)}% (Cần xem xét)`, tone: 'warning' }
  }
  return { label: `${score.toFixed(1)}% (Nghi vấn thấp)`, tone: 'danger' }
}

/** True when the score is missing or below the automatic threshold: the Admin has to compare the photos by hand. */
export function isUnderThreshold(score: number | null): boolean {
  return score == null || score < AUTO_APPROVE_THRESHOLD
}

export function scoreBadgeText(score: number | null): string {
  return score == null ? 'Điểm tin cậy: chưa có' : `Điểm tin cậy: ${score.toFixed(1)}%`
}

export function assessmentText(score: number | null): string {
  if (score == null) {
    return 'Chưa có điểm tin cậy eKYC (thợ chưa nộp hồ sơ hoặc hệ thống chưa chấm). Cần Admin đối chiếu thủ công.'
  }
  return isUnderThreshold(score)
    ? `Điểm tin cậy (${score.toFixed(1)}%) thấp hơn ngưỡng tự động (${AUTO_APPROVE_THRESHOLD}%). Cần Admin đối chiếu khuôn mặt trên selfie với ảnh trên CCCD.`
    : `Điểm tin cậy đạt ${score.toFixed(1)}%. Hồ sơ hợp lệ.`
}

export function ekycQueueRow(item: EkycQueueItemResponse): EkycRow {
  const conf = confidenceInfo(item.confidenceScore)
  return {
    workerId: item.workerId,
    fullName: item.fullName,
    phoneNumber: item.phoneNumber,
    nationalId: item.nationalId,
    confidenceScore: item.confidenceScore,
    confidenceText: conf.label,
    confidenceTone: conf.tone,
    submittedAtFormatted: formatDateTime(item.submittedAt),
    raw: item,
  }
}

export function validateRejectionReason(reason: string): string | null {
  const trimmed = reason.trim()
  if (!trimmed) return 'Vui lòng nhập lý do từ chối hồ sơ eKYC.'
  if (trimmed.length > REASON_MAX_LENGTH) {
    return `Lý do từ chối quá dài (${trimmed.length}/${REASON_MAX_LENGTH} ký tự).`
  }
  return null
}

export function parseActionError(err: FailedCall): ActionError {
  if (err.status === 400 && err.fieldErrors?.rejectionReason) {
    return { message: err.fieldErrors.rejectionReason[0], reasons: [] }
  }
  if (err.status === 409) {
    return { message: 'Hồ sơ eKYC này đã được xử lý hoặc thay đổi trạng thái.', reasons: [] }
  }
  if (err.status === 404) {
    return { message: 'Không tìm thấy hồ sơ thợ.', reasons: [] }
  }
  return { message: err.message || 'Thao tác không thành công.', reasons: [] }
}

/** Path and query of the list call: the tab is sent, so each tab shows its own list. */
export function ekycQueueRequest(page: number, pageSize: number, status: EkycFilterStatus) {
  return { path: '/admin/workers/ekyc-queue', query: { page, pageSize, status } }
}

/** Path and body of the review call. The body is the request itself: wrapping it (`{ body }`) made the server read "approved" as false. */
export function ekycReviewRequest(workerId: number, body: EkycReviewRequest) {
  return { path: `/admin/workers/${workerId}/ekyc-review`, body }
}
