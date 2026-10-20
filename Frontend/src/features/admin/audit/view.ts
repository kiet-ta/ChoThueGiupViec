// Pure logic of the audit log page (no React, so `node --test` covers it).
import { formatDateTime } from '../../../lib/format.ts'
import type { AuditFilter, AuditLogEntry } from './types'

export const PAGE_SIZE = 20

export const EMPTY_FILTER: AuditFilter = { entityType: '', entityId: '', actorType: '', from: '', to: '' }

/** The entity types the backend writes to the audit log, with the name an Admin reads. */
export const ENTITY_TYPES: { value: string; label: string }[] = [
  { value: 'DISPUTE_TICKET', label: 'Khiếu nại tranh chấp' },
  { value: 'JOB_ASSIGNMENT', label: 'Ca làm (vắng mặt)' },
  { value: 'WORKER', label: 'Thợ' },
  { value: 'PAYOUT_BATCH', label: 'Kỳ payout' },
  { value: 'PRICE_RULE', label: 'Bảng giá' },
  { value: 'SUBSCRIPTION_PACKAGE', label: 'Gói thuê bao' },
  { value: 'ESCROW_APPEAL', label: 'Khiếu nại ký quỹ' },
]

export function entityLabel(entityType: string): string {
  return ENTITY_TYPES.find((e) => e.value === entityType)?.label ?? entityType
}

/** Who did it: an Admin by number, or the system. */
export function actorText(entry: Pick<AuditLogEntry, 'actorType' | 'adminId'>): string {
  if (entry.actorType === 'SYSTEM') return 'Hệ thống'
  return entry.adminId === null ? 'Admin' : `Admin #${entry.adminId}`
}

/** "cũ → mới", with "—" for a value that was not there. */
export function changeText(oldValue: string | null, newValue: string | null): string {
  return `${oldValue ?? '—'} → ${newValue ?? '—'}`
}

/** A row of the table; the dispute rows link to their case. */
export interface AuditRow {
  id: number
  time: string
  actor: string
  entity: string
  entityId: string
  field: string
  change: string
  reason: string
  link: string | null
}

export function auditRow(e: AuditLogEntry): AuditRow {
  return {
    id: e.logId,
    time: formatDateTime(e.changedAt),
    actor: actorText(e),
    entity: entityLabel(e.entityType),
    entityId: e.entityId,
    field: e.fieldName,
    change: changeText(e.oldValue, e.newValue),
    reason: e.reason && e.reason.trim() !== '' ? e.reason : '—',
    link: e.entityType === 'DISPUTE_TICKET' && /^\d+$/.test(e.entityId) ? `/admin/disputes/${e.entityId}` : null,
  }
}

/** The instant (UTC, ISO) at which a Vietnam calendar day starts: 00:00 at UTC+7 is 17:00 of the day before in UTC. */
export function vietnamDayStartUtc(day: string): string {
  const [y, m, d] = day.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d) - 7 * 3600 * 1000).toISOString()
}

/**
 * The query of the list call. Empty fields are dropped; `from` is the start of its Vietnam day and `to` the START OF THE NEXT day, so the
 * whole last day is included (the server's `to` is exclusive of nothing the Admin typed). A malformed date is dropped, not sent.
 */
export function auditQuery(filter: AuditFilter, page: number): Record<string, string | number> {
  const isDay = (v: string) => /^\d{4}-\d{2}-\d{2}$/.test(v)
  const query: Record<string, string | number> = { page, pageSize: PAGE_SIZE }
  if (filter.entityType) query.entityType = filter.entityType
  if (filter.entityId.trim()) query.entityId = filter.entityId.trim()
  if (filter.actorType) query.actorType = filter.actorType
  if (isDay(filter.from)) query.from = vietnamDayStartUtc(filter.from)
  if (isDay(filter.to)) {
    const next = new Date(new Date(vietnamDayStartUtc(filter.to)).getTime() + 24 * 3600 * 1000)
    query.to = next.toISOString()
  }
  return query
}

/** True when from is after to: the page says so instead of asking the server for nothing. */
export function rangeError(filter: AuditFilter): string | null {
  const isDay = (v: string) => /^\d{4}-\d{2}-\d{2}$/.test(v)
  if (isDay(filter.from) && isDay(filter.to) && filter.from > filter.to) return 'Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.'
  return null
}
