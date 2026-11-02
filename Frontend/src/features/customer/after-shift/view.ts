// Pure logic of the acceptance, rating and dispute screens (no React, so `node --test` covers it).
// Rules: workers.md 2.5.2/2.5.3 (accept, redo), ratings.md (window, criteria Q14), disputes.md 2.1/2.1a (1-1000, 1-10 photos, jpeg/png/webp <= 5 MB).
import type { Dispute, JobPhoto, RatingWindow } from './types.ts'

export const COMMENT_MAX = 500
export const DESCRIPTION_MAX = 1000
export const EVIDENCE_MAX = 10
export const EVIDENCE_MAX_BYTES = 5 * 1024 * 1024
export const EVIDENCE_TYPES = ['image/jpeg', 'image/png', 'image/webp']
export const REDO_REASON_MAX = 255
export const ANGLES = [1, 2, 3, 4, 5]

// ---------- Photos (5 angles, before and after) ----------
export interface AnglePair {
  angleNo: number
  before: JobPhoto | null
  after: JobPhoto | null
}

/** One row per angle 1 to 5; the newest accepted photo of each phase wins, a missing photo is null. */
export function anglePairs(photos: JobPhoto[]): AnglePair[] {
  const pick = (angle: number, phase: string) =>
    photos
      .filter((p) => p.angleNo === angle && p.photoPhase === phase && p.isAccepted)
      .sort((a, b) => b.uploadedAt.localeCompare(a.uploadedAt))[0] ?? null
  return ANGLES.map((angleNo) => ({ angleNo, before: pick(angleNo, 'BEFORE'), after: pick(angleNo, 'AFTER') }))
}

/** `5 / 5 góc có ảnh trước và sau.` */
export function anglesSummary(pairs: AnglePair[]): string {
  const complete = pairs.filter((p) => p.before && p.after).length
  return `${complete} / ${ANGLES.length} góc có ảnh trước và sau.`
}

// ---------- Acceptance ----------
export function redoReasonError(reason: string): string | null {
  const text = reason.trim()
  if (!text) return 'Hãy cho biết cần dọn lại điều gì.'
  if (text.length > REDO_REASON_MAX) return `Tối đa ${REDO_REASON_MAX} ký tự.`
  return null
}

// ---------- Rating ----------
export const CRITERIA = [
  { key: 'punctuality', label: 'Đúng giờ' },
  { key: 'cleaningQuality', label: 'Sạch sẽ' },
  { key: 'attitude', label: 'Thái độ' },
] as const
export type CriterionKey = (typeof CRITERIA)[number]['key']
export type Scores = Partial<Record<CriterionKey, number>>

const isScore = (n: unknown): n is number => typeof n === 'number' && Number.isInteger(n) && n >= 1 && n <= 5

/** Stars of the whole rating: the average of the three criteria, rounded to the nearest whole star (never below 1). */
export function overallStars(scores: Scores): number | null {
  const values = CRITERIA.map((c) => scores[c.key])
  if (!values.every(isScore)) return null
  return Math.max(1, Math.min(5, Math.round(values.reduce((a, b) => a + b, 0) / values.length)))
}

export interface RatingErrors {
  criteria?: string
  comment?: string
}

export function validateRating(scores: Scores, comment: string): RatingErrors {
  const errors: RatingErrors = {}
  if (!CRITERIA.every((c) => isScore(scores[c.key]))) errors.criteria = 'Hãy chấm đủ 3 tiêu chí từ 1 đến 5 sao.'
  if (comment.trim().length > COMMENT_MAX) errors.comment = `Nhận xét tối đa ${COMMENT_MAX} ký tự.`
  return errors
}

/** `46:12:05` for the seconds left of a window (hours may pass 24); 0 or less reads `00:00:00`. */
export function formatWindow(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds))
  const two = (n: number) => String(n).padStart(2, '0')
  return `${two(Math.floor(s / 3600))}:${two(Math.floor((s % 3600) / 60))}:${two(s % 60)}`
}

/** Seconds left to `closesAt` at `now` (never below 0); null when there is no close time. */
export function secondsUntil(closesAt: string | null, now: Date): number | null {
  if (!closesAt) return null
  const end = new Date(closesAt).getTime()
  return Number.isNaN(end) ? null : Math.max(0, Math.floor((end - now.getTime()) / 1000))
}

/** A closed window is explained, not hidden. */
export function ratingWindowText(window: Pick<RatingWindow, 'reason'>): string {
  switch (window.reason) {
    case 'OPEN':
      return 'Bạn có thể đánh giá ca này.'
    case 'ALREADY_RATED':
      return 'Bạn đã đánh giá ca này rồi. Mỗi ca chỉ đánh giá một lần.'
    case 'WINDOW_CLOSED':
      return 'Đã quá 48 giờ sau ca nên không thể đánh giá nữa.'
    case 'NOT_COMPLETED':
      return 'Ca này chưa hoàn tất nên chưa đánh giá được. Hãy nghiệm thu ca trước.'
    default:
      return 'Hiện chưa đánh giá được ca này.'
  }
}

// ---------- Disputes ----------
export const CATEGORIES = [
  { value: 'QUALITY', label: 'Chất lượng kém' },
  { value: 'ATTITUDE', label: 'Thái độ' },
  { value: 'PROPERTY_DAMAGE', label: 'Hư hỏng tài sản' },
  { value: 'ABSENT_FEE', label: 'Phí vắng mặt' },
  { value: 'OTHER', label: 'Khác' },
]
export const categoryLabel = (value: string) => CATEGORIES.find((c) => c.value === value)?.label ?? value

const STATUS_LABELS: Record<string, string> = { OPEN: 'Đã gửi', IN_REVIEW: 'Đang xử lý', RESOLVED: 'Đã giải quyết' }
/** The label of the server value; an unknown value is shown as the server sent it. */
export const disputeStatusLabel = (status: string) => STATUS_LABELS[status] ?? status

export interface DisputeForm {
  category: string
  description: string
  evidenceCount: number
}
export interface DisputeErrors {
  category?: string
  description?: string
  evidence?: string
}

export function validateDispute(form: DisputeForm): DisputeErrors {
  const errors: DisputeErrors = {}
  if (!CATEGORIES.some((c) => c.value === form.category)) errors.category = 'Hãy chọn loại vấn đề.'
  const length = form.description.trim().length
  if (length === 0) errors.description = 'Hãy mô tả vấn đề.'
  else if (length > DESCRIPTION_MAX) errors.description = `Mô tả tối đa ${DESCRIPTION_MAX} ký tự.`
  if (form.evidenceCount < 1) errors.evidence = 'Cần ít nhất 1 ảnh bằng chứng.'
  else if (form.evidenceCount > EVIDENCE_MAX) errors.evidence = `Tối đa ${EVIDENCE_MAX} ảnh.`
  return errors
}

/** Why a picked file cannot be uploaded (type jpeg/png/webp, 1 byte to 5 MB), or null. */
export function evidenceFileError(file: { type: string; size: number; name: string }): string | null {
  if (!EVIDENCE_TYPES.includes(file.type)) return `${file.name}: chỉ nhận ảnh JPG, PNG hoặc WebP.`
  if (file.size <= 0) return `${file.name}: tệp rỗng.`
  if (file.size > EVIDENCE_MAX_BYTES) return `${file.name}: ảnh lớn hơn 5 MB.`
  return null
}

/** Whole hours left of the 24 h window to file a dispute, measured from the end of the shift or the completion; 0 once closed. */
export function disputeHoursLeft(endedAt: string | null, now: Date, windowHours = 24): number | null {
  if (!endedAt) return null
  const end = new Date(endedAt).getTime()
  if (Number.isNaN(end)) return null
  return Math.max(0, Math.ceil((end + windowHours * 3600_000 - now.getTime()) / 3600_000))
}

/** Text of a failed form call: field messages of a 400, the dispute rules of a 409, the server message otherwise. */
export function afterShiftProblem(error: { status?: number; message?: string; fieldErrors?: Record<string, string[]> }): string {
  if (error.status === 400) {
    const first = Object.values(error.fieldErrors ?? {}).flat()[0]
    if (first) return first
  }
  if (error.status === 409) {
    if (/already exists/i.test(error.message ?? '')) return 'Đơn này đã có khiếu nại. Mỗi đơn chỉ có một khiếu nại.'
    return 'Thao tác không còn hợp lệ với trạng thái hiện tại của đơn. Hãy tải lại trang.'
  }
  if (error.status === 404) return 'Không tìm thấy ca hoặc đơn này.'
  return error.message || 'Đã có lỗi xảy ra. Vui lòng thử lại.'
}

export const byNewest = (a: Dispute, b: Dispute) => b.createdAt.localeCompare(a.createdAt)
