// Pure logic of the ledger page (no React, so `node --test` covers it). Rules: .spec/contracts/ledger.md sections 3 and 9.
import { formatDateTime, formatVnd } from '../../lib/format.ts'
import type { ChainResult, LedgerEntry, LedgerFilters, LedgerSummary, ReconciliationReport } from './types'

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger' | 'accent'

// ---- labels ---------------------------------------------------------------------------------

/** The names of the entry types as the Admin reads them (handoff appendix D). */
export const ENTRY_TYPE_LABELS: Record<string, string> = {
  ORDER_PAID: 'Khách thanh toán đơn',
  EXTENSION_PAID: 'Khách thanh toán làm thêm',
  JOB_EARNING: 'Tiền công ca',
  EXTENSION_EARNING: 'Tiền công làm thêm',
  COMMISSION: 'Phí nền tảng',
  REFUND: 'Hoàn tiền khách',
  ABSENCE_FEE: 'Phí khách vắng mặt',
  PENALTY: 'Khấu trừ khiếu nại',
  SUBSCRIPTION: 'Mua gói thuê bao',
  ESCROW_DEPOSIT: 'Nạp ký quỹ',
  ESCROW_PENALTY: 'Trừ ký quỹ',
  ESCROW_REVERSAL: 'Hoàn ký quỹ',
  WITHDRAWAL: 'Rút tiền (ứng lương)',
  SETTLEMENT: 'Tất toán lương tháng',
  REVERSAL: 'Bút toán đảo',
}

export function entryTypeLabel(type: string): string {
  return ENTRY_TYPE_LABELS[type] ?? type
}

export const ENTRY_TYPE_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: 'Tất cả loại' },
  ...Object.entries(ENTRY_TYPE_LABELS).map(([value, label]) => ({ value, label })),
]

const PARTY_LABELS: Record<string, string> = {
  EXTERNAL: 'Bên ngoài',
  PLATFORM_CLEARING: 'Tiền giữ hộ khách',
  PLATFORM_REVENUE: 'Doanh thu nền tảng',
  WORKER: 'Thợ',
  AGENCY: 'Agency',
  AGENCY_ESCROW: 'Ký quỹ Agency',
}

export const PARTY_TYPE_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: 'Tất cả bên' },
  ...Object.entries(PARTY_LABELS).map(([value, label]) => ({ value, label })),
]

/** `Thợ #7`, `Ký quỹ Agency #3`, `Doanh thu nền tảng` (the platform's own accounts have no id). */
export function partyLabel(type: string, id: number | null): string {
  const name = PARTY_LABELS[type] ?? type
  return id === null ? name : `${name} #${id}`
}

const REF_LABELS: Record<string, string> = {
  ORDER: 'Đơn',
  EXTENSION: 'Làm thêm',
  ASSIGNMENT: 'Ca',
  PAYMENT: 'Thanh toán',
  DISPUTE: 'Khiếu nại',
  ESCROW_TXN: 'Ký quỹ',
  WITHDRAWAL: 'Lệnh rút',
  PAYOUT_ITEM: 'Kỳ payout',
  ENTRY: 'Bút toán',
}

/** `Ca #10` and, when the entry belongs to an order that is not the reference itself, `Ca #10 · đơn #41`. */
export function referenceLabel(refType: string, refId: number, orderId: number | null): string {
  const base = `${REF_LABELS[refType] ?? refType} #${refId}`
  return orderId !== null && !(refType === 'ORDER' && refId === orderId) ? `${base} · đơn #${orderId}` : base
}

/** The first 8 characters of a SHA-256 hash and an ellipsis; the whole hash goes in the tooltip. */
export function shortHash(hash: string): string {
  return hash.length > 8 ? `${hash.slice(0, 8)}…` : hash
}

// ---- rows -----------------------------------------------------------------------------------

export interface EntryRow {
  entryId: number
  code: string
  time: string
  from: string
  to: string
  type: string
  amount: string
  reference: string
  hash: string
  fullHash: string
}

export function entryRow(e: LedgerEntry): EntryRow {
  return {
    entryId: e.entryId,
    code: e.entryCode,
    time: formatDateTime(e.createdAt),
    from: partyLabel(e.fromPartyType, e.fromPartyId),
    to: partyLabel(e.toPartyType, e.toPartyId),
    type: entryTypeLabel(e.entryType),
    amount: formatVnd(e.amount),
    reference: referenceLabel(e.refType, e.refId, e.orderId),
    hash: shortHash(e.entryHash),
    fullHash: e.entryHash,
  }
}

// ---- summary --------------------------------------------------------------------------------

export interface SummaryCard {
  key: string
  title: string
  value: string
  hint: string
}

/** The five cards at the top of the page; a missing summary shows a dash, never a made-up 0. */
export function summaryCards(summary: LedgerSummary | null): SummaryCard[] {
  const money = (n: number | undefined) => (summary && n !== undefined ? formatVnd(n) : '–')
  return [
    { key: 'revenue', title: 'Doanh thu nền tảng', value: money(summary?.revenue), hint: 'Phí nền tảng và gói thuê bao đã ghi nhận' },
    { key: 'clearing', title: 'Tiền đang giữ hộ khách', value: money(summary?.clearing), hint: 'Khách đã trả, chưa chia cho thợ' },
    { key: 'workerDebt', title: 'Nợ thợ tự do', value: money(summary?.workerDebt), hint: 'Tổng số dư ví thợ, chưa tất toán' },
    { key: 'agencyDebt', title: 'Nợ Agency', value: money(summary?.agencyDebt), hint: 'Tổng số dư ví Agency, chưa tất toán' },
    { key: 'held', title: 'Đang chờ rút', value: money(summary?.held), hint: 'Các lệnh rút chờ Admin duyệt' },
  ]
}

// ---- chain check ----------------------------------------------------------------------------

export function verifyMessage(result: ChainResult): { tone: Tone; text: string } {
  if (result.ok) return { tone: 'success', text: `Sổ nguyên vẹn: đã kiểm ${result.checkedCount} bút toán.` }
  const code = result.firstBrokenEntryCode
  return { tone: 'danger', text: code ? `Phát hiện sai lệch tại bút toán ${code}.` : 'Phát hiện sai lệch trong chuỗi hash.' }
}

// ---- reconciliation -------------------------------------------------------------------------

export interface ReconciliationRow {
  type: string
  missingCount: number
  missing: string
  excess: string
}

/** Empty when the ledger and the sources agree: the page then says "Không có khoản lệch". */
export function reconciliationRows(report: ReconciliationReport | null): ReconciliationRow[] {
  if (!report) return []
  return report.items.map((i) => ({
    type: entryTypeLabel(i.entryType),
    missingCount: i.missingCount,
    missing: i.missingAmount > 0 ? formatVnd(i.missingAmount) : '—',
    excess: i.excessAmount > 0 ? formatVnd(i.excessAmount) : '—',
  }))
}

export const NO_DISCREPANCY = 'Không có khoản lệch'

// ---- filters --------------------------------------------------------------------------------

export const EMPTY_FILTERS: LedgerFilters = { from: '', to: '', type: '', partyType: '', partyId: '' }

const DAY = /^\d{4}-\d{2}-\d{2}$/

function isDay(text: string): boolean {
  if (!DAY.test(text)) return false
  const date = new Date(`${text}T00:00:00Z`)
  return !Number.isNaN(date.getTime()) && date.toISOString().startsWith(text)
}

/** Field messages for the filter form; empty when the filters can be sent. The server checks them again. */
export function filterErrors(f: LedgerFilters): Partial<Record<keyof LedgerFilters, string>> {
  const errors: Partial<Record<keyof LedgerFilters, string>> = {}
  if (f.from !== '' && !isDay(f.from)) errors.from = 'Ngày bắt đầu không hợp lệ.'
  if (f.to !== '' && !isDay(f.to)) errors.to = 'Ngày kết thúc không hợp lệ.'
  if (!errors.from && !errors.to && f.from !== '' && f.to !== '' && f.from > f.to) errors.from = 'Ngày bắt đầu phải trước ngày kết thúc.'
  if (f.partyId !== '' && !/^\d+$/.test(f.partyId)) errors.partyId = 'Mã bên phải là số.'
  else if (f.partyId !== '' && f.partyType === '') errors.partyType = 'Chọn loại bên trước khi nhập mã.'
  return errors
}

/** The query of the list or the statement: empty filters are left out. */
export function toQuery(f: LedgerFilters, page?: number, pageSize?: number): Record<string, string | number | undefined> {
  return {
    from: f.from || undefined,
    to: f.to || undefined,
    type: f.type || undefined,
    partyType: f.partyType || undefined,
    partyId: f.partyType !== '' && f.partyId !== '' ? Number(f.partyId) : undefined,
    page,
    pageSize,
  }
}

export function sameFilters(a: LedgerFilters, b: LedgerFilters): boolean {
  return a.from === b.from && a.to === b.to && a.type === b.type && a.partyType === b.partyType && a.partyId === b.partyId
}

/** The statement has opening and closing rows only for one account; the page says so next to the button. */
export function exportHint(f: LedgerFilters): string {
  return f.partyType !== '' && (f.partyId !== '' || f.partyType.startsWith('PLATFORM'))
    ? 'File có số dư đầu kỳ và cuối kỳ của bên đã chọn.'
    : 'Chọn một bên cụ thể để file có số dư đầu kỳ và cuối kỳ.'
}

export function exportFallbackName(f: LedgerFilters): string {
  return `ledger-${f.from || 'start'}_${f.to || 'now'}.xlsx`
}

// ---- errors ---------------------------------------------------------------------------------

export function loadErrorText(status: number): string {
  switch (status) {
    case 0:
      return 'Không kết nối được máy chủ.'
    case 401:
      return 'Phiên đăng nhập đã hết hạn.'
    case 403:
      return 'Bạn không có quyền xem sổ cái.'
    default:
      return `Không tải được dữ liệu (lỗi ${status}).`
  }
}

/** The message of a failed statement request: the first field message of a 400, else the envelope's message, else a line for the status. */
export function exportErrorMessage(status: number, bodyText: string): string {
  try {
    const body: unknown = JSON.parse(bodyText)
    if (typeof body === 'object' && body !== null) {
      const errors = (body as { data?: { errors?: Record<string, unknown> } | null }).data?.errors
      if (errors && typeof errors === 'object') {
        for (const messages of Object.values(errors)) {
          if (Array.isArray(messages) && typeof messages[0] === 'string') return messages[0]
        }
      }

      const message = (body as { message?: unknown }).message
      if (typeof message === 'string' && message.trim() !== '' && status !== 400) return message
    }
  } catch {
    // not JSON: fall through to the status text
  }

  switch (status) {
    case 400:
      return 'Bộ lọc không hợp lệ hoặc sao kê quá dài, hãy chọn khoảng thời gian ngắn hơn.'
    case 401:
      return 'Phiên đăng nhập đã hết hạn.'
    case 403:
      return 'Bạn không có quyền xuất sao kê.'
    default:
      return `Không tải được sao kê (lỗi ${status}).`
  }
}
