import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  addDays,
  agencyStatusText,
  codeMessage,
  commissionText,
  dayLabel,
  errorCode,
  escrowAlerts,
  featureList,
  formatCountdown,
  groupImportErrors,
  isPdf,
  isXlsx,
  jobRow,
  mondayOf,
  parseAmount,
  percent,
  priceText,
  quotaText,
  rosterGrid,
  secondsLeft,
  subscriptionStatusText,
  expiryWarning,
  isVerifiedPartner,
  txnRow,
  weekDays,
} from '../src/features/agencies/view.ts'
import type { EscrowState, RosterEntry, SubscriptionState } from '../src/features/agencies/types.ts'
import { createApiClient } from '../src/services/client.ts'

const pkg = { packageId: 2, packageCode: 'PRO_MONTHLY', packageName: 'Pro', tier: 'PRO', billingCycle: 'MONTHLY', price: 2_000_000, workerQuota: 50, commissionRate: 0, hasRosterDashboard: true, hasAnalytics: true, priorityDispatch: true, isActive: true }

describe('subscription texts', () => {
  it('prices a package and shows commission and features', () => {
    assert.equal(priceText(pkg), '2.000.000 đ / tháng')
    assert.equal(priceText({ price: 0, billingCycle: '' }), 'Miễn phí')
    assert.equal(priceText({ price: 5_400_000, billingCycle: 'QUARTERLY' }), '5.400.000 đ / quý')
    assert.equal(commissionText(0.2), '20%')
    assert.equal(commissionText(0), '0%')
    assert.deepEqual(featureList(pkg), ['Bảng phân ca theo tuần', 'Báo cáo hiệu suất nhân viên', 'Ưu tiên điều phối'])
    assert.deepEqual(featureList({ hasRosterDashboard: false, hasAnalytics: false, priorityDispatch: false }), [])
  })

  it('says whether the quota is used up', () => {
    assert.deepEqual(quotaText({ workerCount: 2, workerQuota: 3 }), { text: '2 / 3 thợ', full: false })
    assert.equal(quotaText({ workerCount: 3, workerQuota: 3 }).full, true)
  })

  it('describes the running subscription and its grace days', () => {
    const base = { tier: 'PRO', package: pkg, workerQuota: 50, workerCount: 1, hasRosterDashboard: true, hasAnalytics: true, priorityDispatch: true } as SubscriptionState
    assert.equal(subscriptionStatusText({ ...base, subscription: null, inGrace: false }), 'Đang dùng gói Free')
    const sub = { subscriptionId: 1, agencyId: 7, packageId: 2, startDate: '2026-10-01', endDate: '2026-10-31', subStatus: 'ACTIVE' }
    assert.equal(subscriptionStatusText({ ...base, subscription: sub, inGrace: false }), 'Gói Pro đến hết ngày 31/10/2026')
    assert.match(subscriptionStatusText({ ...base, subscription: sub, inGrace: true }), /gia hạn 7 ngày/)
  })
})

describe('business codes', () => {
  it('maps a known code to words and ignores an unknown one', () => {
    assert.equal(codeMessage('WORKER_QUOTA_EXCEEDED'), 'Vượt số thợ tối đa của gói hiện tại.')
    assert.equal(codeMessage('SOMETHING_NEW'), null)
    assert.equal(codeMessage(null), null)
    assert.equal(errorCode({ code: 'GUARANTEE_REQUIRED' }), 'GUARANTEE_REQUIRED')
    assert.equal(errorCode(null), null)
    assert.equal(errorCode({ code: 5 }), null)
  })
})

describe('import errors', () => {
  it('groups the errors by sheet row in row order with the whole-file error first', () => {
    const groups = groupImportErrors([
      { row: 5, column: 'phone_number', code: 'INVALID_FORMAT', message: 'phone_number is not a Vietnamese mobile number.' },
      { row: 0, column: '', code: 'QUOTA_EXCEEDED', message: 'Over the quota.' },
      { row: 3, column: 'full_name', code: 'REQUIRED', message: '' },
      { row: 5, column: 'national_id', code: 'DUPLICATE_IN_FILE', message: 'The national id repeats row 3.' },
    ])
    assert.deepEqual(groups.map((g) => g.title), ['Cả file', 'Dòng 3', 'Dòng 5'])
    assert.equal(groups[1].items[0].column, 'Họ tên')
    assert.equal(groups[1].items[0].text, 'Bắt buộc')
    assert.equal(groups[2].items.length, 2)
    assert.match(groups[2].items[1].text, /Trùng trong file/)
  })

  it('accepts only a non-empty .xlsx or .pdf by name', () => {
    assert.equal(isXlsx({ name: 'Workers.XLSX', size: 10 }), true)
    assert.equal(isXlsx({ name: 'workers.csv', size: 10 }), false)
    assert.equal(isXlsx({ name: 'workers.xlsx', size: 0 }), false)
    assert.equal(isPdf({ name: 'g.pdf', size: 1 }), true)
    assert.equal(isPdf({ name: 'g.png', size: 1 }), false)
  })
})

describe('roster week', () => {
  it('finds the Monday of any day and lists seven days', () => {
    assert.equal(mondayOf('2026-10-14'), '2026-10-12') // Wednesday
    assert.equal(mondayOf('2026-10-12'), '2026-10-12')
    assert.equal(mondayOf('2026-10-18'), '2026-10-12') // Sunday
    assert.equal(addDays('2026-10-31', 1), '2026-11-01')
    assert.deepEqual(weekDays('2026-10-12').slice(0, 2), ['2026-10-12', '2026-10-13'])
    assert.equal(weekDays('2026-10-12').length, 7)
    assert.equal(dayLabel('2026-10-14', '2026-10-12'), 'T4 14/10')
  })

  it('puts workers in their cell and only an AVAILABLE slot is removable', () => {
    const entry = (over: Partial<RosterEntry>): RosterEntry => ({
      slotId: 1, workerId: 1, workerName: 'An', slotDate: '2026-10-14', shiftCode: 'SHIFT_MORNING', startTime: '08:00', endTime: '12:00', slotStatus: 'AVAILABLE', ...over,
    })
    const grid = rosterGrid([entry({}), entry({ slotId: 2, workerId: 2, workerName: 'Binh', slotStatus: 'BOOKED' }), entry({ slotId: 3, shiftCode: 'SHIFT_EVENING' })])
    const cell = grid.get('2026-10-14|SHIFT_MORNING')!
    assert.equal(cell.length, 2)
    assert.equal(cell[0].removable, true)
    assert.equal(cell[1].removable, false)
    assert.equal(grid.get('2026-10-14|SHIFT_EVENING')!.length, 1)
  })
})

describe('escrow', () => {
  const state = (over: Partial<EscrowState>): EscrowState => ({
    escrowDepositBalance: 6_000_000, agencyStatus: 'ACTIVE', minEscrowBalance: 5_000_000, hasEscrowWarning: false, slaScore: 100, hasSlaWarning: false, excludedFromPremium: false, ...over,
  })

  it('raises the right alerts', () => {
    assert.deepEqual(escrowAlerts(state({})), [])
    assert.match(escrowAlerts(state({ hasEscrowWarning: true, escrowDepositBalance: 3_000_000 }))[0], /5\.000\.000 đ/)
    assert.match(escrowAlerts(state({ agencyStatus: 'SUSPENDED' }))[0], /tạm ngưng/)
    assert.match(escrowAlerts(state({ slaScore: 40, excludedFromPremium: true, hasSlaWarning: true }))[0], /dưới 50/)
    assert.match(escrowAlerts(state({ slaScore: 70, hasSlaWarning: true }))[0], /70 trở xuống/)
    assert.equal(agencyStatusText('PENDING'), 'Chờ đủ ký quỹ tối thiểu')
  })

  it('shows a penalty as minus with the points and an appeal flag', () => {
    const row = txnRow({ escrowTxnId: 9, txnType: 'PENALTY', amount: 360_000, balanceAfter: 9_640_000, slaPointsDelta: -20, orderId: 1, disputeId: null, paymentId: null, reason: 'NOSHOW: x', createdAt: '2026-10-14T03:00:00Z', canAppeal: true })
    assert.equal(row.amountText, '-360.000 đ')
    assert.equal(row.pointsText, '-20')
    assert.equal(row.when, '14/10/2026 10:00')
    assert.equal(row.canAppeal, true)
    assert.equal(txnRow({ escrowTxnId: 1, txnType: 'DEPOSIT', amount: 1_000_000, balanceAfter: 1_000_000, slaPointsDelta: null, orderId: null, disputeId: null, paymentId: 5, reason: 'd', createdAt: '2026-10-14T03:00:00Z', canAppeal: false }).amountText, '+1.000.000 đ')
  })

  it('parses a typed amount', () => {
    assert.equal(parseAmount('5.000.000'), 5_000_000)
    assert.equal(parseAmount(' 1 000 000 '), 1_000_000)
    assert.equal(parseAmount('0'), null)
    assert.equal(parseAmount('12,5'), 125)
    assert.equal(parseAmount('abc'), null)
    assert.equal(parseAmount('-5'), null)
    assert.equal(parseAmount(''), null)
  })
})

describe('jobs and swap window', () => {
  it('maps a job row', () => {
    const row = jobRow({ assignmentId: 1, orderId: 100, workerId: 7, workerName: '', serviceTier: 'PREMIUM', assignmentSeq: 1, assignmentStatus: 'COMPLETED', grossAmount: 400000, commissionRate: 0.2, payoutAmount: 320000, createdAt: '2026-10-14T03:00:00Z', completedAt: null })
    assert.equal(row.worker, 'Thợ #7')
    assert.equal(row.statusText, 'Hoàn thành')
    assert.equal(row.gross, '400.000 đ')
    assert.equal(percent(0.8), '80%')
  })

  it('counts down to the deadline and never goes below zero', () => {
    const w = { deadlineAt: '2026-10-14T03:30:00Z' }
    const t0 = Date.parse('2026-10-14T03:00:00Z')
    assert.equal(secondsLeft(w, t0), 1800)
    assert.equal(formatCountdown(1800), '30:00')
    assert.equal(formatCountdown(59), '00:59')
    assert.equal(formatCountdown(-5), '00:00')
    assert.equal(secondsLeft(w, Date.parse('2026-10-14T03:31:00Z')), 0)
  })
})

describe('api client file upload and download', () => {
  it('sends a FormData as it is without a JSON content type and returns a blob', async () => {
    let seen: { init?: RequestInit } = {}
    const client = createApiClient({
      baseUrl: '/api',
      fetch: async (_url, init) => {
        seen = { init }
        return new Response(JSON.stringify({ success: true, message: '', data: { ok: 1 } }), { status: 200 })
      },
      config: () => ({ getAccessToken: () => 'tok' }),
    })
    const form = new FormData()
    form.append('file', new Blob(['x']), 'a.xlsx')

    const data = await client.post<{ ok: number }>('/agencies/workers/import/dry-run', form)

    assert.deepEqual(data, { ok: 1 })
    assert.equal(seen.init?.body, form)
    const headers = seen.init?.headers as Record<string, string>
    assert.equal(headers['Content-Type'], undefined)
    assert.equal(headers.Authorization, 'Bearer tok')

    const file = createApiClient({ baseUrl: '/api', fetch: async () => new Response('BIN', { status: 200 }) })
    const blob = await file.download('/agencies/workers/import/template')
    assert.equal(await blob.text(), 'BIN')
  })
})

describe('expiry warning', () => {
  const base = { tier: 'PRO', package: pkg, workerQuota: 50, workerCount: 1, hasRosterDashboard: true, hasAnalytics: true, priorityDispatch: true } as SubscriptionState
  const pro = (endDate: string, extra: Partial<SubscriptionState> = {}): SubscriptionState => ({
    ...base,
    tier: 'PRO',
    subscription: { subscriptionId: 1, agencyId: 1, packageId: 2, startDate: '2026-09-15', endDate, subStatus: 'ACTIVE' },
    ...extra,
  })

  it('warns from 7 days before the end date, with the days left', () => {
    assert.equal(expiryWarning(pro('2026-10-31'), '2026-10-14'), null)
    assert.match(expiryWarning(pro('2026-10-21'), '2026-10-14') ?? '', /còn 7 ngày/)
    assert.match(expiryWarning(pro('2026-10-15'), '2026-10-14') ?? '', /còn 1 ngày/)
    assert.match(expiryWarning(pro('2026-10-14'), '2026-10-14') ?? '', /hết hạn hôm nay/)
  })

  it('says nothing for Free, in the grace days or after the end', () => {
    assert.equal(expiryWarning({ ...base, subscription: null, inGrace: false }, '2026-10-14'), null)
    assert.equal(expiryWarning(pro('2026-10-10', { inGrace: true }), '2026-10-14'), null)
    assert.equal(expiryWarning(pro('2026-10-10'), '2026-10-14'), null)
  })

  it('tells the agency the package does not renew by itself', () => {
    assert.match(expiryWarning(pro('2026-10-16'), '2026-10-14') ?? '', /không tự gia hạn/)
  })
})

describe('verified partner label', () => {
  it('follows the Pro package in force', () => {
    assert.equal(isVerifiedPartner({ tier: 'PRO' }), true)
    assert.equal(isVerifiedPartner({ tier: 'FREE' }), false)
  })
})
