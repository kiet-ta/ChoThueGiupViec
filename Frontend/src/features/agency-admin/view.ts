// Pure logic of the Admin pages of M5 (no React, so `node --test` covers it).
import { formatDateTime, formatVnd } from '../../lib/format.ts'
import type { AuditLogEntry, EscrowAppeal, PackageEdit, SubscriptionPackage } from './types'

export const REASON_MAX_LENGTH = 255
export const NOTE_MAX_LENGTH = 255

export interface PackageForm {
  price: string
  workerQuota: string
  commissionPercent: string
  reason: string
}

export interface PackageFormErrors {
  price?: string
  workerQuota?: string
  commissionPercent?: string
  reason?: string
  body?: string
}

const FIELD_TEXT: Record<string, string> = {
  price: 'Giá',
  worker_quota: 'Hạn mức thợ',
  commission_rate: 'Hoa hồng',
}

export function packageFormOf(p: SubscriptionPackage): PackageForm {
  return { price: String(p.price), workerQuota: String(p.workerQuota), commissionPercent: String(Math.round(p.commissionRate * 1000) / 10), reason: '' }
}

function parseWhole(text: string): number | null {
  const t = text.trim().replace(/[.\s]/g, '')
  return /^\d{1,15}$/.test(t) ? Number(t) : null
}

/**
 * Turns the form into the PUT body, sending only the fields that changed. Returns the errors instead when a value is wrong, nothing
 * changed, or the reason is missing (decisions G-5: every edit needs a reason).
 */
export function buildEdit(original: SubscriptionPackage, form: PackageForm): { edit: PackageEdit | null; errors: PackageFormErrors } {
  const errors: PackageFormErrors = {}
  const edit: PackageEdit = { reason: form.reason.trim() }

  const price = parseWhole(form.price)
  if (price === null) errors.price = 'Giá là số nguyên VND, từ 0 trở lên.'
  else if (price !== original.price) edit.price = price

  const quota = parseWhole(form.workerQuota)
  if (quota === null || quota < 1) errors.workerQuota = 'Hạn mức là số nguyên từ 1 trở lên.'
  else if (quota !== original.workerQuota) edit.workerQuota = quota

  const percentText = form.commissionPercent.trim().replace(',', '.')
  const percent = percentText === '' ? NaN : Number(percentText)
  if (!Number.isFinite(percent) || percent < 0 || percent > 100) errors.commissionPercent = 'Hoa hồng từ 0 đến 100 (%).'
  else if (Math.round(percent * 10) / 1000 !== original.commissionRate) edit.commissionRate = Math.round(percent * 10) / 1000

  if (edit.reason.length === 0) errors.reason = 'Bắt buộc nhập lý do, được lưu vào lịch sử.'
  else if (edit.reason.length > REASON_MAX_LENGTH) errors.reason = `Lý do tối đa ${REASON_MAX_LENGTH} ký tự.`

  const changed = edit.price !== undefined || edit.workerQuota !== undefined || edit.commissionRate !== undefined
  if (!changed && Object.keys(errors).length === 0) errors.body = 'Chưa thay đổi giá trị nào.'

  return Object.keys(errors).length > 0 ? { edit: null, errors } : { edit, errors }
}

export function packageRow(p: SubscriptionPackage) {
  return {
    id: p.packageId,
    code: p.packageCode,
    name: p.packageName,
    tier: p.tier === 'PRO' ? 'Pro' : 'Free',
    price: p.price <= 0 ? 'Miễn phí' : formatVnd(p.price),
    quota: `${p.workerQuota} thợ`,
    commission: `${Math.round(p.commissionRate * 1000) / 10}%`,
    active: p.isActive,
  }
}

export function historyRow(e: AuditLogEntry) {
  const money = e.fieldName === 'price'
  const show = (v: string | null) => (v === null ? '' : money ? formatVnd(Number(v)) : e.fieldName === 'commission_rate' ? `${Math.round(Number(v) * 1000) / 10}%` : v)
  return {
    id: e.logId,
    when: formatDateTime(e.changedAt),
    who: e.actorType === 'SYSTEM' ? 'Hệ thống' : `Admin #${e.adminId ?? '?'}`,
    field: FIELD_TEXT[e.fieldName] ?? e.fieldName,
    oldValue: show(e.oldValue),
    newValue: show(e.newValue),
    reason: e.reason ?? '',
  }
}

export function appealStatusText(status: string): string {
  if (status === 'PENDING') return 'Chờ xử lý'
  if (status === 'ACCEPTED') return 'Đã chấp nhận (hoàn lại)'
  if (status === 'REJECTED') return 'Đã bác bỏ'
  return status
}

export function appealRow(a: EscrowAppeal) {
  return {
    id: a.appealId,
    txn: `#${a.escrowTxnId}`,
    agency: `#${a.agencyId}`,
    reason: a.reason,
    status: appealStatusText(a.status),
    pending: a.status === 'PENDING',
    created: formatDateTime(a.createdAt),
    decided: formatDateTime(a.decidedAt),
    note: a.decisionNote ?? '',
  }
}

/** Hours left of the 48 h window counted from the penalty is the server's rule; the console only shows the decision. */
export function noteProblem(note: string): string | null {
  return note.trim().length > NOTE_MAX_LENGTH ? `Ghi chú tối đa ${NOTE_MAX_LENGTH} ký tự.` : null
}
