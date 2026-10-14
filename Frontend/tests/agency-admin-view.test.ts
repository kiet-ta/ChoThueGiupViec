import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { appealRow, appealStatusText, buildEdit, historyRow, noteProblem, packageFormOf, packageRow } from '../src/features/agency-admin/view.ts'
import type { AuditLogEntry, SubscriptionPackage } from '../src/features/agency-admin/types.ts'

const pkg: SubscriptionPackage = {
  packageId: 2, packageCode: 'PRO_MONTHLY', packageName: 'Pro monthly', tier: 'PRO', billingCycle: 'MONTHLY', price: 2_000_000, workerQuota: 50,
  commissionRate: 0, hasRosterDashboard: true, hasAnalytics: true, priorityDispatch: true, isActive: true,
}

describe('package form', () => {
  it('starts from the current values and shows the commission in percent', () => {
    assert.deepEqual(packageFormOf({ ...pkg, commissionRate: 0.2 }), { price: '2000000', workerQuota: '50', commissionPercent: '20', reason: '' })
  })

  it('sends only the changed fields and trims the reason', () => {
    const { edit, errors } = buildEdit(pkg, { price: '2.500.000', workerQuota: '50', commissionPercent: '0', reason: '  Q4 pricing ' })
    assert.deepEqual(errors, {})
    assert.deepEqual(edit, { reason: 'Q4 pricing', price: 2_500_000 })
  })

  it('turns percent into a rate with at most one decimal', () => {
    const { edit } = buildEdit({ ...pkg, commissionRate: 0.2 }, { price: '2000000', workerQuota: '50', commissionPercent: '12,5', reason: 'x' })
    assert.equal(edit?.commissionRate, 0.125)
  })

  it('requires a reason, valid numbers and at least one change', () => {
    assert.equal(buildEdit(pkg, { price: '1', workerQuota: '50', commissionPercent: '0', reason: ' ' }).errors.reason, 'Bắt buộc nhập lý do, được lưu vào lịch sử.')
    assert.ok(buildEdit(pkg, { price: 'abc', workerQuota: '50', commissionPercent: '0', reason: 'x' }).errors.price)
    assert.ok(buildEdit(pkg, { price: '1', workerQuota: '0', commissionPercent: '0', reason: 'x' }).errors.workerQuota)
    assert.ok(buildEdit(pkg, { price: '1', workerQuota: '50', commissionPercent: '120', reason: 'x' }).errors.commissionPercent)
    assert.ok(buildEdit(pkg, { price: '1', workerQuota: '50', commissionPercent: '', reason: 'x' }).errors.commissionPercent)
    assert.equal(buildEdit(pkg, { price: '2000000', workerQuota: '50', commissionPercent: '0', reason: 'same' }).errors.body, 'Chưa thay đổi giá trị nào.')
    assert.equal(buildEdit(pkg, { price: '1', workerQuota: '50', commissionPercent: '0', reason: 'x'.repeat(256) }).edit, null)
  })
})

describe('rows', () => {
  it('shows a package row', () => {
    assert.deepEqual(packageRow({ ...pkg, price: 0, tier: 'FREE', commissionRate: 0.2, workerQuota: 3 }), {
      id: 2, code: 'PRO_MONTHLY', name: 'Pro monthly', tier: 'Free', price: 'Miễn phí', quota: '3 thợ', commission: '20%', active: true,
    })
  })

  it('formats each audited field of the history', () => {
    const e = (over: Partial<AuditLogEntry>): AuditLogEntry => ({
      logId: 1, actorType: 'ADMIN', adminId: 4, entityType: 'SUBSCRIPTION_PACKAGE', entityId: '2', fieldName: 'price', oldValue: '2000000',
      newValue: '2500000', reason: 'Q4', changedAt: '2026-10-14T03:00:00Z', ...over,
    })
    const price = historyRow(e({}))
    assert.equal(price.field, 'Giá')
    assert.equal(price.oldValue, '2.000.000 đ')
    assert.equal(price.who, 'Admin #4')
    assert.equal(price.when, '14/10/2026 10:00')
    assert.equal(historyRow(e({ fieldName: 'commission_rate', oldValue: '0.200', newValue: '0' })).oldValue, '20%')
    assert.equal(historyRow(e({ fieldName: 'worker_quota', oldValue: '50', newValue: '60' })).newValue, '60')
    assert.equal(historyRow(e({ actorType: 'SYSTEM', adminId: null })).who, 'Hệ thống')
  })

  it('shows an appeal and whether it can still be decided', () => {
    const row = appealRow({ appealId: 3, escrowTxnId: 9, agencyId: 7, reason: 'khách sai', status: 'PENDING', createdAt: '2026-10-14T03:00:00Z', decidedAt: null, decisionNote: null })
    assert.equal(row.pending, true)
    assert.equal(row.txn, '#9')
    assert.equal(row.status, 'Chờ xử lý')
    assert.equal(appealStatusText('ACCEPTED'), 'Đã chấp nhận (hoàn lại)')
    assert.equal(noteProblem('x'.repeat(256)) !== null, true)
    assert.equal(noteProblem('ok'), null)
  })
})
