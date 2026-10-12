// Pure logic of the price table page (no React, so `node --test` covers it).
import { formatDateTime, formatVnd } from '../../../lib/format.ts'
import type { AuditLogEntry, PriceRule } from './types'

export const REASON_MAX_LENGTH = 255 // ADMIN_AUDIT_LOG.reason NVARCHAR(255), contract booking.md 4.2
/**
 * The server accepts up to 9,999,999,999,999,999 (DECIMAL(18,2), whole VND), but a JavaScript number is exact only up to
 * Number.MAX_SAFE_INTEGER (9,007,199,254,740,991): a larger price cannot be typed or sent without losing digits, so the form
 * stops there. Real prices are far below both.
 */
export const MAX_UNIT_PRICE = Number.MAX_SAFE_INTEGER

const TIER_LABELS: Record<string, string> = { ECONOMY: 'Economy', PREMIUM: 'Premium' }

const BRACKET_LABELS: Record<string, string> = {
  UP_TO_30: 'Đến 30 m²',
  FROM_31_TO_80: 'Trên 30 đến 80 m²',
  OVER_80: 'Trên 80 m² (2 thợ)',
}

export function tierLabel(tier: string): string {
  return TIER_LABELS[tier] ?? tier
}

export function bracketLabel(bracket: string): string {
  return BRACKET_LABELS[bracket] ?? bracket
}

export interface RuleRow {
  ruleId: number
  tier: string
  bracket: string
  unitPrice: number
  priceText: string
  updatedText: string
  active: boolean
}

export function ruleRow(rule: PriceRule): RuleRow {
  return {
    ruleId: rule.ruleId,
    tier: tierLabel(rule.serviceTier),
    bracket: bracketLabel(rule.areaBracket),
    unitPrice: rule.unitPrice,
    priceText: formatVnd(rule.unitPrice),
    updatedText: formatDateTime(rule.updatedAt),
    active: rule.isActive,
  }
}

/** Economy before Premium, then the brackets from small to large, whatever order the server used. */
export function sortRules(rules: PriceRule[]): PriceRule[] {
  const tiers = ['ECONOMY', 'PREMIUM']
  const brackets = ['UP_TO_30', 'FROM_31_TO_80', 'OVER_80']
  const rank = (list: string[], value: string) => (list.indexOf(value) === -1 ? list.length : list.indexOf(value))
  return [...rules].sort(
    (a, b) =>
      rank(tiers, a.serviceTier) - rank(tiers, b.serviceTier) ||
      rank(brackets, a.areaBracket) - rank(brackets, b.areaBracket) ||
      a.ruleId - b.ruleId,
  )
}

export interface PriceInput {
  /** The parsed whole-VND amount when the text is acceptable. */
  value: number | null
  error: string | null
}

/**
 * The price an Admin typed: digits, optionally grouped with dots, commas or spaces (`260.000`). Whole VND, greater than 0,
 * at most MAX_UNIT_PRICE, and different from the current price (the server writes nothing for the same price).
 */
export function parseUnitPrice(text: string, currentPrice: number): PriceInput {
  const trimmed = text.trim()
  if (trimmed.length === 0) return { value: null, error: 'Vui lòng nhập đơn giá mới.' }
  // A separator is accepted only between groups of exactly three digits. `160000.5` must never be read as 1,600,005.
  const plain = /^\d+$/.test(trimmed)
  const grouped = /^\d{1,3}([.,\s]\d{3})+$/.test(trimmed)
  if (!plain && !grouped) return { value: null, error: 'Đơn giá chỉ gồm chữ số (VND, không có phần lẻ).' }
  const compact = trimmed.replace(/[.,\s]/g, '')
  const value = Number(compact)
  if (value <= 0) return { value: null, error: 'Đơn giá phải lớn hơn 0.' }
  // Above this a number is no longer exact, so "greater than the limit" is decided before any arithmetic on it.
  if (!Number.isSafeInteger(value)) return { value: null, error: 'Đơn giá vượt quá giới hạn cho phép.' }
  if (value === currentPrice) return { value: null, error: 'Đơn giá mới trùng với đơn giá hiện tại.' }
  return { value, error: null }
}

export function validateReason(text: string): string | null {
  const trimmed = text.trim()
  if (trimmed.length === 0) return 'Vui lòng nhập lý do thay đổi giá.'
  if (trimmed.length > REASON_MAX_LENGTH) return `Lý do tối đa ${REASON_MAX_LENGTH} ký tự (đang ${trimmed.length}).`
  return null
}

/** The part of an `ApiError` the page needs, so this file does not import the client (which reads `import.meta`). */
export interface FailedCall {
  status: number
  message: string
  fieldErrors?: Record<string, string[]>
}

export interface SaveError {
  /** A message for the whole form; null when every problem belongs to a field. */
  message: string | null
  unitPrice: string | null
  reason: string | null
}

/** Turns a failed save into what the Admin should read (contract booking.md 4.2: 400, 404; plus 401/403 and network). */
export function saveError(e: FailedCall): SaveError {
  const none: SaveError = { message: null, unitPrice: null, reason: null }
  switch (e.status) {
    case 400: {
      const unitPrice = e.fieldErrors?.unitPrice?.[0] ?? null
      const reason = e.fieldErrors?.reason?.[0] ?? null
      return { message: unitPrice || reason ? null : e.message || 'Dữ liệu không hợp lệ.', unitPrice, reason }
    }
    case 404:
      return { ...none, message: 'Không tìm thấy dòng giá này. Hãy tải lại bảng giá.' }
    case 401:
    case 403:
      return { ...none, message: 'Bạn không có quyền sửa bảng giá. Hãy đăng nhập lại bằng tài khoản Admin.' }
    case 0:
      return { ...none, message: 'Không kết nối được máy chủ.' }
    default:
      return { ...none, message: e.message || `Lỗi ${e.status}` }
  }
}

export interface HistoryRow {
  logId: number
  when: string
  who: string
  oldPrice: string
  newPrice: string
  reason: string
}

/** An audit value is text (`"160000"`); anything that is not a number is shown as it is, a missing one as a dash. */
function priceText(value: string | null): string {
  if (value === null || value.trim() === '') return '–'
  const amount = Number(value)
  return Number.isFinite(amount) ? formatVnd(amount) : value
}

export function historyRow(entry: AuditLogEntry): HistoryRow {
  return {
    logId: entry.logId,
    when: formatDateTime(entry.changedAt),
    who: entry.actorType === 'ADMIN' ? (entry.adminId === null ? 'Admin' : `Admin #${entry.adminId}`) : 'Hệ thống',
    oldPrice: priceText(entry.oldValue),
    newPrice: priceText(entry.newValue),
    reason: entry.reason?.trim() ? entry.reason : '–',
  }
}
