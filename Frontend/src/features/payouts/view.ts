// Pure logic of the payout pages (no React, so `node --test` covers it). Rules: .spec/contracts/payouts.md 2.1-2.4.
import { sortParams, type SortState } from '../../components/data-table/sort.ts'
import { formatDateTime, formatVnd } from '../../lib/format.ts'
import type { BatchStatus, ExportType, PayoutBatch, PayoutItem } from './types'

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger' | 'accent'

// ---- sorting by a column header (contract payouts.md 2.2: the `sort` and `dir` parameters) -------------------------------

/** The `sort` names of the batch table: the month, the status, the number of payees, the total paid out and the time the system closed it. */
export const BATCH_SORT = { period: 'period', status: 'status', items: 'items', total: 'total', closed: 'closedAt' } as const

/** The table opens with the newest month first, which is `period` descending (what the server does without a sort). */
export const BATCH_DEFAULT_SORT: SortState = { key: BATCH_SORT.period, dir: 'desc' }

/** The query of `GET /admin/payout-batches`: the page and the order. */
export function batchQuery(page: number, pageSize: number, sort: SortState | null): Record<string, string | number | undefined> {
  return { page, pageSize, ...sortParams(sort) }
}

// ---- months ---------------------------------------------------------------------------------

/** `2026-10` becomes `Tháng 10/2026`; anything else is shown as it is. */
export function periodLabel(period: string): string {
  const m = /^(\d{4})-(\d{2})$/.exec(period)
  return m ? `Tháng ${Number(m[2])}/${m[1]}` : period
}

// ---- labels ---------------------------------------------------------------------------------

export function batchStatusInfo(status: string): { label: string; tone: Tone } {
  switch (status) {
    case 'DRAFT':
      return { label: 'Đang tất toán', tone: 'warning' }
    case 'CLOSED':
      return { label: 'Đã tất toán', tone: 'success' }
    default:
      return { label: status, tone: 'neutral' }
  }
}

export function itemStatusInfo(status: string): { label: string; tone: Tone } {
  switch (status) {
    case 'PENDING':
      return { label: 'Chờ tất toán', tone: 'warning' }
    case 'TRANSFERRED':
      return { label: 'Đã tất toán', tone: 'success' }
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
  /** `Hệ thống tự chốt lúc …` for a month the system settled, `Admin #3 lúc …` for an old confirmation, a dash while open. */
  closedBy: string
  detailPath: string
}

/** Who closed the month and when: the system for every batch since decision D28 (`confirmedBy` is null), an Admin for an old one. */
export function closedByText(b: Pick<PayoutBatch, 'confirmedBy' | 'confirmedAt'>): string {
  if (!b.confirmedAt) return '—'
  const at = formatDateTime(b.confirmedAt)
  return b.confirmedBy === null ? `Hệ thống tự chốt lúc ${at}` : `Admin #${b.confirmedBy} lúc ${at}`
}

export function batchRow(b: PayoutBatch): BatchRow {
  return {
    batchId: b.batchId,
    month: periodLabel(b.periodMonth),
    status: batchStatusInfo(b.batchStatus),
    items: b.itemCount,
    total: formatVnd(b.totalAmount),
    closedBy: closedByText(b),
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
  advance: string
  carryIn: string
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
    advance: i.advanceAmount > 0 ? formatVnd(-i.advanceAmount) : '—',
    carryIn: i.carryInAmount !== 0 ? formatVnd(i.carryInAmount) : '—',
    net: formatVnd(i.netAmount),
    bank: i.bankName?.trim() ? i.bankName : '—',
    account: missing ? 'Chưa có' : i.bankAccountNo,
    missingAccount: missing,
    status: itemStatusInfo(i.itemStatus),
  }
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
