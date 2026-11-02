import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  cancelReasonError, extensionHourOptions, hoursText, newEndText, orderActions, orderProblem, pageCount, STATUS_TABS, timeline, vnTime,
} from '../src/features/customer/orders/view.ts'
import type { Extension, ProgressAssignment } from '../src/features/customer/orders/types.ts'

const now = new Date('2026-11-02T12:00:00Z')
const worker = (status: string, completedAt: string | null = null): ProgressAssignment => ({
  assignmentId: 1, assignmentSeq: 1, workerId: 7, workerName: 'Hà Trần', workerRatingAvg: 4.9, assignmentStatus: status, acceptedAt: null, completedAt,
})
const extension = (over: Partial<Extension> = {}): Extension => ({
  extensionId: 3, orderId: 1, workerId: 7, extraHours: 1.5, extraAmount: 97500, extStatus: 'PENDING_PAYMENT', workerDecision: 'PENDING', requestedAt: '2026-11-02T04:00:00Z', decidedAt: null, ...over,
})

describe('orderActions: cancel', () => {
  it('lets a customer cancel before and after payment while no worker works', () => {
    for (const s of ['PENDING_PAYMENT', 'PAID', 'DISPATCHING']) assert.equal(orderActions(s, [], null, now).canCancel, true, s)
    assert.equal(orderActions('ASSIGNED', [worker('ASSIGNED')], null, now).canCancel, true)
    assert.equal(orderActions('ASSIGNED', [worker('CHECKED_IN')], null, now).canCancel, true)
  })
  it('blocks cancel once the work started or the order ended', () => {
    for (const s of ['IN_PROGRESS', 'AWAITING_ACCEPTANCE', 'COMPLETED']) assert.equal(orderActions('ASSIGNED', [worker(s)], null, now).canCancel, false, s)
    for (const s of ['COMPLETED', 'CANCELLED']) assert.equal(orderActions(s, [], null, now).canCancel, false, s)
  })
  it('offers payment only while waiting for it', () => {
    assert.equal(orderActions('PENDING_PAYMENT', [], null, now).canPay, true)
    assert.equal(orderActions('PAID', [], null, now).canPay, false)
  })
})

describe('orderActions: extension', () => {
  it('needs an ASSIGNED order, a worker on site and no extension yet', () => {
    assert.equal(orderActions('ASSIGNED', [worker('IN_PROGRESS')], null, now).canExtend, true)
    assert.equal(orderActions('ASSIGNED', [worker('AWAITING_ACCEPTANCE')], null, now).canExtend, true)
    assert.equal(orderActions('ASSIGNED', [worker('ASSIGNED')], null, now).canExtend, false)
    assert.equal(orderActions('ASSIGNED', [worker('IN_PROGRESS')], extension(), now).canExtend, false)
    assert.equal(orderActions('COMPLETED', [worker('IN_PROGRESS')], null, now).canExtend, false)
  })
})

describe('orderActions: after the shift', () => {
  it('shows acceptance while a worker waits for it', () => {
    assert.equal(orderActions('ASSIGNED', [worker('AWAITING_ACCEPTANCE')], null, now).showAcceptance, true)
    assert.equal(orderActions('ASSIGNED', [worker('IN_PROGRESS')], null, now).showAcceptance, false)
  })
  it('opens the dispute window for 24 h and the rating window for 48 h', () => {
    const at = (hoursAgo: number) => new Date(now.getTime() - hoursAgo * 3600_000).toISOString()
    const a = orderActions('COMPLETED', [worker('COMPLETED', at(10))], null, now)
    assert.deepEqual([a.showRating, a.showDispute], [true, true])
    const b = orderActions('COMPLETED', [worker('COMPLETED', at(30))], null, now)
    assert.deepEqual([b.showRating, b.showDispute], [true, false])
    const c = orderActions('COMPLETED', [worker('COMPLETED', at(60))], null, now)
    assert.deepEqual([c.showRating, c.showDispute], [false, false])
    assert.equal(orderActions('ASSIGNED', [worker('COMPLETED')], null, now).showRating, false) // no completion time
  })
})

describe('timeline', () => {
  const states = (status: string, after = false) => timeline(status, after).map((s) => s.state)
  it('marks the step an order is at', () => {
    assert.deepEqual(states('PENDING_PAYMENT'), ['current', 'todo', 'todo', 'todo'])
    assert.deepEqual(states('DISPATCHING'), ['done', 'current', 'todo', 'todo'])
    assert.deepEqual(states('ASSIGNED'), ['done', 'done', 'current', 'todo'])
    assert.deepEqual(states('COMPLETED'), ['done', 'done', 'done', 'done'])
  })
  it('marks where a cancelled order stopped', () => {
    assert.deepEqual(states('CANCELLED'), ['cancelled', 'todo', 'todo', 'todo'])
    assert.deepEqual(states('CANCELLED', true), ['done', 'cancelled', 'todo', 'todo'])
  })
})

describe('extension helpers', () => {
  it('offers 0.5 to 4 hours in 0.5 steps', () => {
    const o = extensionHourOptions()
    assert.equal(o.length, 8)
    assert.deepEqual([o[0], o[1], o[7]], [0.5, 1, 4])
  })
  it('writes hours with a decimal comma', () => {
    assert.equal(hoursText(1.5), '1,5 giờ')
    assert.equal(hoursText(2), '2 giờ')
  })
  it('computes the new end in Vietnam time', () => {
    assert.equal(vnTime('2026-11-02T05:00:00Z'), '12:00')
    assert.equal(newEndText('2026-11-02T05:00:00Z', 1.5), '12:00 → 13:30')
    assert.equal(newEndText('bad', 1), '')
  })
})

describe('forms, errors and paging', () => {
  it('requires a cancel reason of 1 to 255 characters', () => {
    assert.ok(cancelReasonError('   '))
    assert.equal(cancelReasonError('Đổi lịch'), null)
    assert.ok(cancelReasonError('x'.repeat(256)))
  })
  it('maps the contract errors', () => {
    assert.match(orderProblem({ status: 409, data: { code: 'EXTENSION_EXISTS' } }), /đã có một đề nghị/)
    assert.match(orderProblem({ status: 409, data: { code: 'INVALID_STATE' } }), /không còn ở trạng thái/)
    assert.equal(orderProblem({ status: 400, fieldErrors: { reason: ['Thiếu lý do.'] } }), 'Thiếu lý do.')
    assert.equal(orderProblem({ status: 404 }), 'Không tìm thấy đơn.')
  })
  it('counts pages of 20 and never goes below 1', () => {
    assert.equal(pageCount(0, 20), 1)
    assert.equal(pageCount(20, 20), 1)
    assert.equal(pageCount(21, 20), 2)
  })
  it('has one tab per list filter, "all" first', () => {
    assert.equal(STATUS_TABS[0].status, null)
    assert.equal(new Set(STATUS_TABS.map((t) => t.status)).size, STATUS_TABS.length)
  })
})
