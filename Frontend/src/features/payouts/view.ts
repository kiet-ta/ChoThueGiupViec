// Pure logic of the payout pages (no React, so `node --test` covers it). Rules: .spec/contracts/payouts.md 2.1-2.4.
import { DISPLAY_TIME_ZONE, formatDateTime, formatVnd } from '../../lib/format.ts'
import type { BatchStatus, ExportType, PayoutBatch, PayoutItem } from './types'

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger' | 'accent'

// ---- months ---------------------------------------------------------------------------------

/** The year and month of an instant on the Asia/Ho_Chi_Minh calendar (decision G-3). */
export function localYearMonth(now: Date): { year: number; month: number } {
  const parts = new Intl.DateTimeFormat('en-GB', { timeZone: DISPLAY_TIME_ZONE, year: 'numeric', month: '2-digit' }).formatToParts(now)
  const get = (type: string) => Number(parts.find((p) => p.type === type)?.value)
  return { year: get('year'), month: get('month') }
}

export function periodValue(year: number, month: number): string {
  return `${year}-${String(month).padStart(2, '0')}`
}

/** `2026-10` becomes `Tháng 10/2026`; anything else is shown as it is. */
export function periodLabel(period: string): string {
  const m = /^(\d{4})-(\d{2})$/.exec(period)
  return m ? `Tháng ${Number(m[2])}/${m[1]}` : period
}

/**
 * The months a batch can be built for: finished months only (contract payouts.md 2.1, P2: the running month is refused), newest
 * first, `count` of them. The "today" is the Asia/Ho_Chi_Minh calendar, so just after midnight local time on the 1st the previous
 * month is already offered even if the UTC date is still the 31st.
 */
export function finishedMonths(now: Date, count = 12): { value: string; label: string }[] {
  const { year, month } = localYearMonth(now)
  const options: { value: string; label: string }[] = []
  let y = year
  let m = month
  for (let i = 0; i < count; i++) {
    m -= 1
    if (m === 0) {
      m = 12
      y -= 1
    }
    const value = periodValue(y, m)
    options.push({ value, label: periodLabel(value) })
  }
  return options
}

// ---- labels ---------------------------------------------------------------------------------

export function batchStatusInfo(status: string): { label: string; tone: Tone } {
  switch (status) {
    case 'DRAFT':
      return { label: 'Nháp (chưa giải ngân)', tone: 'warning' }
    case 'CLOSED':
      return { label: 'Đã giải ngân', tone: 'success' }
    default:
      return { label: status, tone: 'neutral' }
  }
}

export function itemStatusInfo(status: string): { label: string; tone: Tone } {
  switch (status) {
    case 'PENDING':
      return { label: 'Chờ chuyển', tone: 'warning' }
    case 'TRANSFERRED':
      return { label: 'Đã chuyển', tone: 'success' }
    default:
      return { label: status, tone: 'neutral' }
  }
}

export function payeeTypeLabel(type: string): string {
  return type === 'AGENCY' ? 'Agency' : type === 'FREELANCER' ? 'Freelancer' : type
}

export const PAYEE_CHIPS = [
  { value: '', label: 'Tất cả' },
  { value: 'FREELANCER', label: 'Freelancer' },
  { value: 'AGENCY', label: 'Agency' },
]

export const EXPORTS: { type: ExportType; label: string }[] = [
  { type: 'freelancer', label: 'File chuyển khoản Freelancer' },
  { type: 'agency-summary', label: 'File chuyển khoản Agency (tổng hợp)' },
  { type: 'agency-detail', label: 'File chi tiết từng ca của Agency' },
]

export function exportFallbackName(period: string, type: ExportType): string {
  return `payout-${period}-${type}.xlsx`
}

// ---- rows -----------------------------------------------------------------------------------

export interface BatchRow {
  batchId: number
  month: string
  status: { label: string; tone: Tone }
  items: number
  total: string
  confirmedAt: string
  detailPath: string
}

export function batchRow(b: PayoutBatch): BatchRow {
  return {
    batchId: b.batchId,
    month: periodLabel(b.periodMonth),
    status: batchStatusInfo(b.batchStatus),
    items: b.itemCount,
    total: formatVnd(b.totalAmount),
    confirmedAt: b.confirmedAt ? formatDateTime(b.confirmedAt) : '—',
    detailPath: `/admin/payout-batches/${b.batchId}`,
  }
}

export interface ItemRow {
  itemId: number
  type: string
  name: string
  jobs: number
  gross: string
  commission: string
  penalty: string
  net: string
  bank: string
  account: string
  /** True when the payee has no account number: the row is flagged so the gap is seen (question P4). */
  missingAccount: boolean
  status: { label: string; tone: Tone }
}

export function itemRow(i: PayoutItem): ItemRow {
  const missing = i.bankAccountNo.trim() === ''
  return {
    itemId: i.itemId,
    type: payeeTypeLabel(i.payeeType),
    name: i.payeeName,
    jobs: i.jobCount,
    gross: formatVnd(i.grossAmount),
    commission: formatVnd(i.commissionAmount),
    penalty: i.penaltyAmount > 0 ? formatVnd(-i.penaltyAmount) : '—',
    net: formatVnd(i.netAmount),
    bank: i.bankName?.trim() ? i.bankName : '—',
    account: missing ? 'Chưa có' : i.bankAccountNo,
    missingAccount: missing,
    status: itemStatusInfo(i.itemStatus),
  }
}

/** What confirming does, in words, for the confirmation step. No bank is called by the system (decision G-1). */
export function confirmLines(b: Pick<PayoutBatch, 'periodMonth' | 'itemCount' | 'totalAmount'>): string[] {
  return [
    `${periodLabel(b.periodMonth)}: ${b.itemCount} người nhận, tổng ${formatVnd(b.totalAmount)}.`,
    'Hệ thống không chuyển tiền: bạn xác nhận đã chuyển khoản theo file đã xuất.',
    'Kỳ sẽ đóng và không thể dựng lại hay sửa.',
  ]
}

/** The server's warning line (English, `<name> has no bank account number.`) in Vietnamese; any other line is shown as it is. */
export function warningText(line: string): string {
  const m = /^(.*) has no bank account number\.$/.exec(line)
  return m ? `${m[1]}: chưa có số tài khoản ngân hàng.` : line
}

// ---- errors ---------------------------------------------------------------------------------

/** The part of an `ApiError` this file needs, so it does not import the client (which reads `import.meta`). */
export interface FailedCall {
  status: number
  message: string
  data: unknown
  fieldErrors?: Record<string, string[]>
}

/** Build and rebuild (contract payouts.md 2.1): 400 a bad or unfinished month, 409 a closed batch or a busy month. */
export function buildError(e: FailedCall): string {
  switch (e.status) {
    case 400:
      return e.fieldErrors?.periodMonth?.[0] === 'Only a finished month can be built.'
        ? 'Chỉ dựng được kỳ của tháng đã kết thúc.'
        : 'Tháng không hợp lệ, hãy chọn lại.'
    case 409:
      return 'Kỳ của tháng này đã đóng, không thể dựng lại. Hoặc đang có lần dựng khác, hãy thử lại sau ít giây.'
    case 0:
      return 'Không kết nối được máy chủ.'
    default:
      return e.message || `Lỗi ${e.status}`
  }
}

/** Confirm (contract payouts.md 2.4): 404, 409 already closed / no items / the month is not over. */
export function confirmError(e: FailedCall): string {
  switch (e.status) {
    case 404:
      return 'Không tìm thấy kỳ này.'
    case 409:
      return 'Không xác nhận được: kỳ đã đóng, chưa có người nhận nào, hoặc tháng chưa kết thúc. Đã tải lại kỳ.'
    case 0:
      return 'Không kết nối được máy chủ.'
    default:
      return e.message || `Lỗi ${e.status}`
  }
}

// ---- file download --------------------------------------------------------------------------

/**
 * The file name of a `Content-Disposition` header (`filename="x.xlsx"`, `filename=x.xlsx` or `filename*=UTF-8''x.xlsx`), reduced to
 * a plain name: a path or a name with control characters falls back to `fallback`.
 */
export function parseFileName(header: string | null | undefined, fallback: string): string {
  if (!header) return fallback
  const star = /filename\*\s*=\s*[^']*'[^']*'([^;]+)/i.exec(header)
  const plain = /filename\s*=\s*(?:"([^"]*)"|([^;]+))/i.exec(header)
  let name = star ? safeDecode(star[1].trim()) : plain ? (plain[1] ?? plain[2] ?? '').trim() : ''
  name = name.trim()
  // eslint-disable-next-line no-control-regex
  if (name === '' || /[\\/\u0000-\u001f]/.test(name) || name === '.' || name === '..') return fallback
  return name
}

function safeDecode(text: string): string {
  try {
    return decodeURIComponent(text)
  } catch {
    return text
  }
}

/** The message of a failed file request: the `message` of the JSON envelope when the body has one, else a line for the status. */
export function fileErrorMessage(status: number, bodyText: string): string {
  try {
    const body: unknown = JSON.parse(bodyText)
    if (typeof body === 'object' && body !== null) {
      const message = (body as { message?: unknown }).message
      if (typeof message === 'string' && message.trim() !== '') return message
    }
  } catch {
    // not JSON: fall through to the status text
  }
  switch (status) {
    case 400:
      return 'Loại file không hợp lệ.'
    case 401:
      return 'Phiên đăng nhập đã hết hạn.'
    case 403:
      return 'Bạn không có quyền xuất file này.'
    case 404:
      return 'Không tìm thấy kỳ này.'
    default:
      return `Không tải được file (lỗi ${status}).`
  }
}

export type { BatchStatus }
