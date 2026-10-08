// Pure logic of the dispute console (no React, so `node --test` covers it). Rules: .spec/contracts/disputes.md 2.2-2.3.
import { disputeCode, formatDateTime, formatSlaRemaining, formatVnd, workerTypeLabel } from '../../lib/format.ts'
import type { ComparePhoto } from '../../components/photo-compare/pair-photos'
import type { CasePhoto, DisputeSummary, DisputeWorker, ResolveRequest, TimelineEntry } from './types'

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger' | 'accent'

export const NOTE_MAX_LENGTH = 255 // ADMIN_AUDIT_LOG.reason NVARCHAR(255)

// ---- labels ---------------------------------------------------------------------------------

export function priorityInfo(priority: string): { label: string; tone: Tone } {
  switch (priority) {
    case 'HIGH':
      return { label: 'Cao', tone: 'danger' }
    case 'MEDIUM':
      return { label: 'Trung bình', tone: 'warning' }
    case 'LOW':
      return { label: 'Thấp', tone: 'neutral' }
    default:
      return { label: priority, tone: 'neutral' }
  }
}

export function statusInfo(status: string): { label: string; tone: Tone } {
  switch (status) {
    case 'OPEN':
      return { label: 'Chờ tiếp nhận', tone: 'neutral' }
    case 'IN_REVIEW':
      return { label: 'Đang xử lý', tone: 'warning' }
    case 'RESOLVED':
      return { label: 'Đã phán quyết', tone: 'success' }
    case 'DISMISSED':
      return { label: 'Đã bác bỏ', tone: 'info' }
    default:
      return { label: status, tone: 'neutral' }
  }
}

const CATEGORIES: Record<string, string> = {
  QUALITY: 'Chất lượng kém',
  ATTITUDE: 'Thái độ',
  PROPERTY_DAMAGE: 'Hư hỏng tài sản',
  ABSENT_FEE: 'Phí vắng mặt',
  OTHER: 'Khác',
}

export function categoryLabel(category: string): string {
  return CATEGORIES[category] ?? category
}

export function faultLabel(fault: string | null): string {
  switch (fault) {
    case 'FREELANCER':
      return 'Lỗi thuộc Freelancer'
    case 'AGENCY':
      return 'Lỗi thuộc Agency'
    case 'CUSTOMER':
      return 'Lỗi thuộc khách hàng'
    default:
      return 'Không bên nào có lỗi'
  }
}

// ---- queue ----------------------------------------------------------------------------------

export const PRIORITY_CHIPS = [
  { value: '', label: 'Tất cả' },
  { value: 'HIGH', label: 'Ưu tiên cao' },
  { value: 'MEDIUM', label: 'Trung bình' },
  { value: 'LOW', label: 'Thấp' },
]

/** `status` query of the queue; the priority filter only applies to unresolved tickets (the server ignores it otherwise). */
export const STATUS_CHIPS = [
  { value: 'OPEN,IN_REVIEW', label: 'Đang mở' },
  { value: 'RESOLVED,DISMISSED', label: 'Đã xử lý' },
]

export interface QueueRow {
  disputeId: number
  code: string
  customerName: string
  workers: { name: string; kind: string }[]
  category: string
  /** The "Auto-Cancelled Dispute" tag of the design, shown for the absence-fee dispute. */
  autoTag: boolean
  priority: { label: string; tone: Tone }
  slaText: string
  overdue: boolean
  status: { label: string; tone: Tone }
  detailPath: string
}

export function queueRow(s: DisputeSummary): QueueRow {
  const open = s.disputeStatus === 'OPEN' || s.disputeStatus === 'IN_REVIEW'
  return {
    disputeId: s.disputeId,
    code: disputeCode(s.disputeId),
    customerName: s.customerName,
    workers: s.workers.map((w) => ({ name: w.fullName, kind: workerTypeLabel(w.workerType, w.agencyName) })),
    category: categoryLabel(s.category),
    autoTag: s.autoCancelled,
    priority: priorityInfo(s.priority),
    slaText: open ? formatSlaRemaining(s.slaSecondsRemaining) : '—',
    overdue: open && s.slaSecondsRemaining < 0,
    status: statusInfo(s.disputeStatus),
    detailPath: `/admin/disputes/${s.disputeId}`,
  }
}

// ---- case file ------------------------------------------------------------------------------

const TIMELINE_LABELS: Record<string, string> = {
  CHECK_IN: 'Check-in',
  PHOTO_AFTER: 'Ảnh After',
  CUSTOMER_DISPUTED: 'Khiếu nại',
  CHECK_OUT: 'Hoàn tất ca',
}

export interface TimelineRow {
  key: string
  time: string
  label: string
  detail: string
  /** Badge next to the line: GPS result or the VoL score. */
  badge: { text: string; tone: Tone } | null
}

export function timelineRows(entries: TimelineEntry[]): TimelineRow[] {
  return entries.map((e, i) => {
    let badge: TimelineRow['badge'] = null
    if (e.type === 'CHECK_IN' && e.gpsVerified !== null && e.gpsVerified !== undefined) {
      badge = e.gpsVerified ? { text: 'GPS ✓', tone: 'success' } : { text: 'GPS ✗', tone: 'danger' }
    } else if (e.type === 'PHOTO_AFTER' && e.volScore !== null && e.volScore !== undefined) {
      badge = { text: `VoL ${Math.round(e.volScore)}`, tone: 'info' }
    }
    return {
      key: `${e.at}-${e.type}-${i}`,
      time: formatDateTime(e.at),
      label: TIMELINE_LABELS[e.type] ?? e.type,
      detail: e.detail,
      badge,
    }
  })
}

/** The assignments of the order that have photos, ascending; the viewer shows one at a time. */
export function photoAssignmentIds(photos: CasePhoto[]): number[] {
  return [...new Set(photos.map((p) => p.assignmentId))].sort((a, b) => a - b)
}

/** Before and after photos of one assignment in the shape of the shared `BeforeAfterViewer`. */
export function toComparePhotos(photos: CasePhoto[], assignmentId: number): { before: ComparePhoto[]; after: ComparePhoto[] } {
  const mine = photos.filter((p) => p.assignmentId === assignmentId)
  const map = (p: CasePhoto): ComparePhoto => ({ angleNo: p.angleNo, url: p.url, volScore: p.volScore, isAccepted: p.isAccepted })
  return { before: mine.filter((p) => p.phase === 'BEFORE').map(map), after: mine.filter((p) => p.phase === 'AFTER').map(map) }
}

// ---- verdict --------------------------------------------------------------------------------

export type FaultChoice = '' | 'FREELANCER' | 'AGENCY' | 'CUSTOMER' | 'DISMISS'

export interface ResolveForm {
  fault: FaultChoice
  /** What the Admin typed; digits only (a dot or a space as thousands separator is tolerated). */
  amount: string
  lockWorker: boolean
  note: string
}

export const EMPTY_FORM: ResolveForm = { fault: '', amount: '', lockWorker: false, note: '' }

export interface FaultOption {
  value: Exclude<FaultChoice, ''>
  label: string
  hint: string
}

/**
 * The verdicts the server will accept for this order: FREELANCER needs a freelancer worker and AGENCY an agency worker on it
 * (otherwise the server answers 400); CUSTOMER and a dismissal are always possible.
 */
export function faultOptions(workers: DisputeWorker[]): FaultOption[] {
  const options: FaultOption[] = []
  if (workers.some((w) => w.workerType === 'FREELANCER')) {
    options.push({ value: 'FREELANCER', label: 'Lỗi thuộc Freelancer', hint: 'Hoàn tiền cho khách, trừ vào kỳ payout của thợ; có thể khoá tài khoản.' })
  }
  if (workers.some((w) => w.workerType !== 'FREELANCER')) {
    options.push({ value: 'AGENCY', label: 'Lỗi thuộc Agency', hint: 'Hoàn tiền cho khách, trừ 5 điểm SLA và ký quỹ của Agency (Q09).' })
  }
  options.push({ value: 'CUSTOMER', label: 'Lỗi thuộc khách hàng', hint: 'Chỉ ghi nhận, không có tiền nào chuyển cho khách.' })
  options.push({ value: 'DISMISS', label: 'Không bên nào có lỗi (bác bỏ)', hint: 'Đóng khiếu nại, không có tác động nào.' })
  return options
}

/** The amount typed as a whole number; null when it is not one. Empty means 0. */
export function parseAmount(text: string): number | null {
  const cleaned = text.trim().replace(/[.\s]/g, '')
  if (cleaned === '') return 0
  if (!/^\d+$/.test(cleaned)) return null
  const n = Number(cleaned)
  return Number.isSafeInteger(n) ? n : null
}

export type ResolveErrors = Partial<Record<'faultParty' | 'compensationAmount' | 'lockWorker' | 'note', string>>

/** The client-side mirror of the contract's 400 rules, so the Admin sees the problem before sending. */
export function validateResolve(form: ResolveForm, workers: DisputeWorker[]): ResolveErrors {
  const errors: ResolveErrors = {}
  if (form.fault === '') errors.faultParty = 'Chọn bên có lỗi hoặc bác bỏ khiếu nại.'
  else if (!faultOptions(workers).some((o) => o.value === form.fault)) errors.faultParty = 'Đơn này không có thợ thuộc loại đã chọn.'

  const amount = parseAmount(form.amount)
  if (amount === null) errors.compensationAmount = 'Số tiền phải là số nguyên VND, không âm.'
  else if (amount > 0 && form.fault !== 'FREELANCER' && form.fault !== 'AGENCY') {
    errors.compensationAmount = 'Chỉ lỗi thuộc Freelancer hoặc Agency mới có tiền bồi hoàn.'
  }

  if (form.lockWorker && form.fault !== 'FREELANCER') errors.lockWorker = 'Chỉ khoá tài khoản khi lỗi thuộc Freelancer.'

  const note = form.note.trim()
  if (note.length === 0) errors.note = 'Vui lòng nhập ghi chú phán quyết.'
  else if (note.length > NOTE_MAX_LENGTH) errors.note = `Ghi chú tối đa ${NOTE_MAX_LENGTH} ký tự (đang ${note.length}).`
  return errors
}

/** The request body; call only after `validateResolve` returned no errors. A dismissal sends `faultParty: null`. */
export function resolveRequest(form: ResolveForm): ResolveRequest {
  return {
    faultParty: form.fault === 'DISMISS' || form.fault === '' ? null : form.fault,
    compensationAmount: form.fault === 'FREELANCER' || form.fault === 'AGENCY' ? (parseAmount(form.amount) ?? 0) : 0,
    lockWorker: form.fault === 'FREELANCER' && form.lockWorker,
    note: form.note.trim(),
  }
}

/** The effect of the verdict in words, for the confirmation step. */
export function effectText(form: ResolveForm): string[] {
  const amount = parseAmount(form.amount) ?? 0
  switch (form.fault) {
    case 'FREELANCER': {
      const lines = amount > 0 ? [`Hoàn ${formatVnd(amount)} cho khách ngay.`, `Trừ ${formatVnd(amount)} vào kỳ payout kế tiếp của thợ.`] : ['Ghi nhận lỗi của Freelancer, không có khoản tiền nào.']
      if (form.lockWorker) lines.push('Ghi nhận yêu cầu khoá tài khoản thợ (việc khoá do module Thợ thực hiện).')
      return lines
    }
    case 'AGENCY':
      return [
        ...(amount > 0 ? [`Hoàn ${formatVnd(amount)} cho khách ngay.`] : []),
        'Trừ 5 điểm SLA của Agency (khiếu nại được chấp nhận) và khấu trừ ký quỹ theo số tiền hoàn.',
      ]
    case 'CUSTOMER':
      return ['Ghi nhận lỗi thuộc khách hàng. Không có tiền nào chuyển cho khách.']
    case 'DISMISS':
      return ['Đóng khiếu nại, không có tác động nào.']
    default:
      return []
  }
}

/** The part of an `ApiError` this file needs, so it does not import the client (which reads `import.meta`). */
export interface FailedCall {
  status: number
  message: string
  data: unknown
  fieldErrors?: Record<string, string[]>
}

export interface VerdictError {
  message: string
  fields: ResolveErrors
}

/** Turns a failed take or resolve call into what the Admin reads (contract disputes.md 2.3: 400, 404, 409, 502). */
export function verdictError(e: FailedCall): VerdictError {
  switch (e.status) {
    case 400: {
      const f = e.fieldErrors ?? {}
      const fields: ResolveErrors = {}
      for (const key of ['faultParty', 'compensationAmount', 'lockWorker', 'note'] as const) {
        if (f[key]?.[0]) fields[key] = f[key][0]
      }
      return { message: Object.keys(fields).length > 0 ? 'Kiểm tra lại các ô báo lỗi.' : e.message || 'Dữ liệu không hợp lệ.', fields }
    }
    case 404:
      return { message: 'Không tìm thấy khiếu nại này.', fields: {} }
    case 409:
      return { message: 'Khiếu nại này đã được xử lý (có thể bởi Admin khác). Đã tải lại hồ sơ.', fields: {} }
    case 502:
      return { message: 'Không hoàn được tiền cho khách (cổng hoàn tiền từ chối). Chưa có gì thay đổi, có thể thử lại.', fields: {} }
    case 0:
      return { message: 'Không kết nối được máy chủ.', fields: {} }
    default:
      return { message: e.message || `Lỗi ${e.status}`, fields: {} }
  }
}

/**
 * Evidence links come from customers and workers, so they are untrusted text: only an http(s) address or a path on this site may become
 * a link or an image (a `javascript:` or `data:` address must never reach `href`). Returns null for anything else.
 */
export function safeEvidenceUrl(url: string): string | null {
  const text = url.trim()
  if (/^https?:\/\/[^\s]+$/i.test(text)) return text
  if (/^\/(?![/\\])[^\s]*$/.test(text)) return text
  return null
}
