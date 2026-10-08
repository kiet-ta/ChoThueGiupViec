// Pure display helpers shared by the Admin pages (no React, no globals beyond Intl): they run under `node --test`.
// Time zone rule (decision G-3): the API sends UTC, the Admin console shows Asia/Ho_Chi_Minh.

export const DISPLAY_TIME_ZONE = 'Asia/Ho_Chi_Minh'

/** Whole VND with a dot as the thousands separator: `1234567` -> `1.234.567 đ`. Not locale dependent. */
export function formatVnd(amount: number): string {
  const rounded = Math.round(amount)
  const digits = String(Math.abs(rounded)).replace(/\B(?=(\d{3})+(?!\d))/g, '.')
  return `${rounded < 0 ? '-' : ''}${digits} đ`
}

/** `07/10/2026 11:00` in Asia/Ho_Chi_Minh for an ISO-8601 instant; empty text for a missing or invalid value. */
export function formatDateTime(iso: string | null | undefined, timeZone: string = DISPLAY_TIME_ZONE): string {
  if (!iso) return ''
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(date)
  const get = (type: string) => parts.find((p) => p.type === type)?.value ?? ''
  return `${get('day')}/${get('month')}/${get('year')} ${get('hour')}:${get('minute')}`
}

const two = (n: number) => String(n).padStart(2, '0')

/**
 * Time left to an SLA: `01h 45p` (hours may pass 24, as in the design: `29h 05p`); an overdue ticket reads `Quá hạn 02h 10p`.
 * Seconds are floored to whole minutes; exactly 0 reads `00h 00p`.
 */
export function formatSlaRemaining(seconds: number): string {
  const overdue = seconds < 0
  const minutes = Math.floor(Math.abs(seconds) / 60)
  const text = `${two(Math.floor(minutes / 60))}h ${two(minutes % 60)}p`
  return overdue ? `Quá hạn ${text}` : text
}

/** `#TC-1042` for the dispute id shown to the Admin (the design's "Mã KN"). */
export function disputeCode(disputeId: number): string {
  return `#TC-${disputeId}`
}

/** `Freelancer` or `Agency · CleanPro Solutions` for a worker of a dispute or an absence report. */
export function workerTypeLabel(workerType: string, agencyName: string | null | undefined): string {
  if (workerType === 'FREELANCER') return 'Freelancer'
  return agencyName ? `Agency · ${agencyName}` : 'Agency'
}
