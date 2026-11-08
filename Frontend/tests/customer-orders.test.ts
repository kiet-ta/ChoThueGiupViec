import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  ARRIVED_HINT, arrivalCandidates, arrivalProblem, arrivalText, cancelReasonError, cancelReasonText, fallbackDeadlineText, fallbackOutcomeText, fallbackProblem, fallbackQuestion, hasFallback, extensionHourOptions, hasOrderRefund, hoursText, newEndText, orderActions, orderProblem, pageCount, STATUS_TABS, timeline, vnDateTime, vnTime,
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

describe('cancelReasonText', () => {
  it('explains both dispatch failures without internal codes', () => {
    const expected = 'Hiện chưa có thợ rảnh ở khu vực và ca bạn chọn nên hệ thống đã tự hủy đơn. Bạn không bị trừ tiền: khoản đã thanh toán được hoàn 100%. Hãy thử chọn ca hoặc ngày khác.'
    assert.equal(cancelReasonText({ cancelReason: 'Quá bán kính 10 km không tìm được thợ phù hợp (BR-03).' }), expected)
    assert.equal(cancelReasonText({ cancelReason: 'Không tìm thấy thợ phù hợp cho đơn hàng.' }), expected)
  })
  it('preserves customer text and falls back for empty reasons', () => {
    assert.equal(cancelReasonText({ cancelReason: 'Đổi lịch' }), 'Đổi lịch')
    assert.equal(cancelReasonText({ cancelReason: '' }), 'Đơn đã được hủy.')
    assert.equal(cancelReasonText({ cancelReason: null }), 'Đơn đã được hủy.')
  })
})

describe('hasOrderRefund', () => {
  it('only confirms a refund of the order payment', () => {
    assert.equal(hasOrderRefund([{ purpose: 'ORDER', txnStatus: 'REFUNDED' }]), true)
    assert.equal(hasOrderRefund([{ purpose: 'EXTENSION', txnStatus: 'REFUNDED' }]), false)
    assert.equal(hasOrderRefund([{ purpose: 'ORDER', txnStatus: 'SUCCESS' }]), false)
    assert.equal(hasOrderRefund([]), false)
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
  it('offers the acceptance review once no worker awaits acceptance and one is completed', () => {
    assert.equal(orderActions('COMPLETED', [worker('COMPLETED')], null, now).showAcceptanceReview, true)
    assert.equal(orderActions('ASSIGNED', [worker('COMPLETED'), { ...worker('AWAITING_ACCEPTANCE'), assignmentId: 2 }], null, now).showAcceptanceReview, false)
    assert.equal(orderActions('ASSIGNED', [worker('IN_PROGRESS')], null, now).showAcceptanceReview, false)
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
  const states = (status: string, after = false, workers: ProgressAssignment[] = []) => timeline(status, after, workers).map((s) => s.state)
  it('has the 7 steps with arrived and working', () => {
    assert.deepEqual(timeline('ASSIGNED').map((s) => s.label), ['Thanh toán', 'Tìm thợ', 'Đã có thợ', 'Thợ đã đến', 'Thợ đang làm', 'Chờ nghiệm thu', 'Hoàn tất'])
  })
  it('marks the step an order is at', () => {
    assert.deepEqual(states('PENDING_PAYMENT'), ['current', 'todo', 'todo', 'todo', 'todo', 'todo', 'todo'])
    assert.deepEqual(states('DISPATCHING'), ['done', 'current', 'todo', 'todo', 'todo', 'todo', 'todo'])
    assert.deepEqual(states('ASSIGNED'), ['done', 'done', 'current', 'todo', 'todo', 'todo', 'todo'])
    assert.deepEqual(states('COMPLETED'), ['done', 'done', 'done', 'done', 'done', 'done', 'done'])
  })
  it('follows the most advanced visible assignment of an assigned order', () => {
    const current = (workers: ProgressAssignment[]) => timeline('ASSIGNED', false, workers).find((s) => s.state === 'current')?.key
    assert.equal(current([worker('ASSIGNED')]), 'assigned')
    assert.equal(current([worker('ASSIGNED'), worker('CHECKED_IN')]), 'arrived')
    assert.equal(current([worker('CHECKED_IN'), worker('IN_PROGRESS')]), 'working')
    assert.equal(current([worker('IN_PROGRESS'), worker('AWAITING_ACCEPTANCE')]), 'acceptance')
    assert.equal(current([worker('UNKNOWN')]), 'assigned')
    assert.equal(current([]), 'assigned')
  })
  it('explains on the arrived step that the work starts with the before photos', () => {
    const arrived = (workers: ProgressAssignment[]) => timeline('ASSIGNED', false, workers).find((s) => s.key === 'arrived')
    assert.equal(arrived([worker('CHECKED_IN')])?.hint, ARRIVED_HINT)
    assert.equal(ARRIVED_HINT, 'Ca bắt đầu khi thợ chụp ảnh trước khi dọn.')
    assert.equal(arrived([worker('IN_PROGRESS')])?.hint, undefined)
    assert.equal(arrived([worker('ASSIGNED')])?.hint, undefined)
  })
  it('marks where a cancelled order stopped', () => {
    assert.deepEqual(states('CANCELLED'), ['cancelled', 'todo', 'todo', 'todo', 'todo', 'todo', 'todo'])
    assert.deepEqual(states('CANCELLED', true), ['done', 'cancelled', 'todo', 'todo', 'todo', 'todo', 'todo'])
  })
})

describe('arrival confirmation', () => {
  it('asks only for assigned workers of an assigned order', () => {
    assert.deepEqual(arrivalCandidates('ASSIGNED', [{ ...worker('ASSIGNED'), assignmentId: 7 }, { ...worker('CHECKED_IN'), assignmentId: 8 }]), [7])
    assert.deepEqual(arrivalCandidates('COMPLETED', [{ ...worker('ASSIGNED'), assignmentId: 7 }]), [])
  })
  it('names the worker and rounds the distance', () => {
    assert.equal(arrivalText('Phạm Thị Nhận', 6675.33), 'Thợ Phạm Thị Nhận báo đã đến nhưng vị trí GPS chưa khớp địa chỉ (cách khoảng 6.675 m).')
    assert.equal(arrivalText(null, null), 'Thợ báo đã đến nhưng vị trí GPS chưa khớp địa chỉ.')
  })
  it('explains a refused confirmation', () => {
    assert.equal(arrivalProblem({ status: 409 }), 'Thợ chưa báo đến nơi hoặc đã được xác nhận.')
    assert.equal(arrivalProblem({ status: 404 }), 'Không tìm thấy ca làm này.')
    assert.equal(arrivalProblem({ status: 500 }), 'Không xác nhận được. Vui lòng thử lại.')
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

describe('BR-02 fallback', () => {
  it('asks only for an order awaiting the fallback', () => {
    assert.equal(hasFallback('AWAITING_FALLBACK'), true)
    assert.equal(hasFallback('ASSIGNED'), false)
  })
  it('writes the question and the deadlines in Vietnam time', () => {
    assert.equal(vnDateTime('2026-10-15T01:00:00Z'), '08:00, 15/10')
    assert.equal(vnDateTime(null), '')
    assert.match(fallbackQuestion('ca chiều', '2026-10-15'), /làm luôn ca chiều ngày 15\/10 ngay sau ca đã nhận/)
    assert.equal(fallbackDeadlineText('AWAITING_CUSTOMER', '2026-10-15T01:00:00Z'), 'Hãy trả lời trước 08:00, 15/10. Nếu không trả lời, đơn sẽ tự hủy và hoàn tiền 100 %.')
    assert.match(fallbackDeadlineText('AWAITING_WORKER', null), /Thợ có 2 phút/)
  })
  it('explains the outcome and the 409 codes', () => {
    assert.match(fallbackOutcomeText('WORKER_ASKED'), /2 phút/)
    assert.match(fallbackOutcomeText('CANCELLED'), /hoàn tiền 100 %/)
    assert.match(fallbackProblem({ status: 409, data: { code: 'FALLBACK_NOT_WAITING' } }), /không còn hiệu lực/)
    assert.match(fallbackProblem({ status: 409, data: { code: 'FALLBACK_ALREADY_ANSWERED' } }), /đã đồng ý/)
    assert.match(fallbackProblem({ status: 404 }), /Không tìm thấy/)
  })
})
