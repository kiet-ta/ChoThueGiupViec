// Pure mapping from the API response to what the dashboard shows (no React, so `node --test` covers it).
import { disputeCode, formatSlaRemaining, workerTypeLabel } from '../../../lib/format.ts'
import type { Dashboard, NearSlaDispute } from './types'

export interface CardModel {
  title: string
  figures: { value: number; label: string; tone?: 'default' | 'danger' }[]
}

/** The three cards of the design: orders, shifts, open disputes. Near-SLA disputes are red while there is at least one. */
export function dashboardCards(d: Dashboard): CardModel[] {
  return [
    {
      title: 'Đơn',
      figures: [
        { value: d.orders.today, label: 'Hôm nay' },
        { value: d.orders.thisWeek, label: 'Tuần này' },
      ],
    },
    {
      title: 'Ca làm',
      figures: [
        { value: d.shifts.inProgress, label: 'Đang làm' },
        { value: d.shifts.completedToday, label: 'Hoàn tất hôm nay' },
      ],
    },
    {
      title: 'Tranh chấp tồn',
      figures: [
        { value: d.disputes.open, label: 'Chưa xử lý' },
        { value: d.disputes.nearSla, label: 'Sắp quá SLA', tone: d.disputes.nearSla > 0 ? 'danger' : 'default' },
      ],
    },
  ]
}

export interface NearSlaRow {
  disputeId: number
  code: string
  customerName: string
  /** One line per worker of the order, for example `Freelancer` or `Agency · CleanPro Solutions`. */
  workers: { name: string; kind: string }[]
  slaText: string
  overdue: boolean
  detailPath: string
}

export function nearSlaRows(items: NearSlaDispute[]): NearSlaRow[] {
  return items.map((d) => ({
    disputeId: d.disputeId,
    code: disputeCode(d.disputeId),
    customerName: d.customerName,
    workers: d.workers.map((w) => ({ name: w.fullName, kind: workerTypeLabel(w.workerType, w.agencyName) })),
    slaText: formatSlaRemaining(d.slaSecondsRemaining),
    overdue: d.slaSecondsRemaining < 0,
    detailPath: `/admin/disputes/${d.disputeId}`,
  }))
}
