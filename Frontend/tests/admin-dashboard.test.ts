import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { dashboardCards, nearSlaRows } from '../src/features/admin/dashboard/view.ts'
import type { Dashboard, NearSlaDispute } from '../src/features/admin/dashboard/types.ts'

const dashboard = (nearSla: number): Dashboard => ({
  generatedAt: '2026-10-07T04:00:00Z',
  orders: { today: 48, thisWeek: 312 },
  shifts: { inProgress: 17, completedToday: 29 },
  disputes: { open: 46, nearSla },
})

describe('dashboardCards', () => {
  it('has exactly the three groups of the contract in the order of the design, with the right numbers', () => {
    const cards = dashboardCards(dashboard(3))

    assert.deepEqual(
      cards.map((c) => c.title),
      ['Đơn', 'Ca làm', 'Tranh chấp tồn'],
    )
    assert.deepEqual(cards[0].figures, [
      { value: 48, label: 'Hôm nay' },
      { value: 312, label: 'Tuần này' },
    ])
    assert.deepEqual(cards[1].figures, [
      { value: 17, label: 'Đang làm' },
      { value: 29, label: 'Hoàn tất hôm nay' },
    ])
    assert.equal(cards[2].figures[0].value, 46)
    assert.equal(cards[2].figures[1].value, 3)
    assert.equal(cards.flatMap((c) => c.figures).length, 6) // six numbers and no other metric (G-7)
  })

  it('shows near-SLA disputes in red only while there are some', () => {
    assert.equal(dashboardCards(dashboard(3))[2].figures[1].tone, 'danger')
    assert.equal(dashboardCards(dashboard(0))[2].figures[1].tone, 'default')
    assert.equal(dashboardCards(dashboard(3))[2].figures[0].tone, undefined) // the open count is never red
  })
})

const dispute = (over: Partial<NearSlaDispute>): NearSlaDispute => ({
  disputeId: 1042,
  orderCode: 'ORD1042',
  customerName: 'Chị Ngọc Anh',
  workers: [{ workerId: 7, fullName: 'Lý Văn Phúc', workerType: 'AGENCY_STAFF', agencyName: 'CleanPro Solutions' }],
  slaSecondsRemaining: 11520,
  ...over,
})

describe('nearSlaRows', () => {
  it('maps a row the way the design shows it', () => {
    const [row] = nearSlaRows([dispute({})])

    assert.equal(row.code, '#TC-1042')
    assert.equal(row.customerName, 'Chị Ngọc Anh')
    assert.deepEqual(row.workers, [{ name: 'Lý Văn Phúc', kind: 'Agency · CleanPro Solutions' }])
    assert.equal(row.slaText, '03h 12p')
    assert.equal(row.overdue, false)
    assert.equal(row.detailPath, '/admin/disputes/1042')
  })

  it('marks an overdue ticket and keeps the order of the list', () => {
    const rows = nearSlaRows([dispute({ disputeId: 1, slaSecondsRemaining: -600 }), dispute({ disputeId: 2, slaSecondsRemaining: 60 })])

    assert.deepEqual(rows.map((r) => r.disputeId), [1, 2])
    assert.equal(rows[0].overdue, true)
    assert.equal(rows[0].slaText, 'Quá hạn 00h 10p')
    assert.equal(rows[1].overdue, false)
  })

  it('lists every worker of an order, freelancer or agency', () => {
    const [row] = nearSlaRows([
      dispute({
        workers: [
          { workerId: 1, fullName: 'Phạm Hoàng Nam', workerType: 'FREELANCER', agencyName: null },
          { workerId: 2, fullName: 'Lý Văn Phúc', workerType: 'AGENCY_STAFF', agencyName: 'CleanPro Solutions' },
        ],
      }),
    ])

    assert.deepEqual(row.workers.map((w) => w.kind), ['Freelancer', 'Agency · CleanPro Solutions'])
  })

  it('is empty for an empty list', () => {
    assert.deepEqual(nearSlaRows([]), [])
  })
})
