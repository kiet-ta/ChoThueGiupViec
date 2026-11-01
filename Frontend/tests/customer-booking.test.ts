import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { bookingProblem, dayText, nextOrder, shiftBlockedReason, shiftStart, upcomingDays, vnDate, workersPrice } from '../src/features/customer/booking/view.ts'
import type { OrderSummary } from '../src/features/customer/booking/types.ts'

const morning = { shiftCode: 'SHIFT_MORNING', startLocal: '08:00', endLocal: '12:00' }
const opts = { premiumMinLeadHours: 4 }
const order = (over: Partial<OrderSummary>): OrderSummary => ({
  orderId: 1, orderCode: 'GV1', serviceTier: 'ECONOMY', scheduledDate: '2026-11-03', shiftCode: 'SHIFT_MORNING',
  requiredWorkers: 1, totalAmount: 260000, orderStatus: 'PAID', createdAt: '2026-11-01T00:00:00Z', ...over,
})

describe('vietnam dates', () => {
  it('uses the Vietnam calendar day, not the UTC one', () => {
    assert.equal(vnDate(new Date('2026-11-01T18:00:00Z')), '2026-11-02')
    assert.equal(vnDate(new Date('2026-11-01T16:59:00Z')), '2026-11-01')
  })
  it('lists 7 days from today with weekday labels', () => {
    const days = upcomingDays(new Date('2026-11-01T03:00:00Z'))
    assert.equal(days.length, 7)
    assert.deepEqual(days[0], { date: '2026-11-01', weekday: 'CN', day: '01' })
    assert.equal(days[1].weekday, 'T2')
  })
  it('formats a day', () => assert.equal(dayText('2026-10-26'), 'T2 26/10'))
  it('converts a shift start to UTC', () => assert.equal(shiftStart('2026-11-02', '08:00').toISOString(), '2026-11-02T01:00:00.000Z'))
})

describe('shiftBlockedReason', () => {
  const now = new Date('2026-11-02T00:00:00Z') // 07:00 in Vietnam
  it('blocks a shift in the past', () => assert.equal(shiftBlockedReason('2026-11-01', morning, 'ECONOMY', opts, now), 'Đã qua'))
  it('allows Economy one hour ahead', () => assert.equal(shiftBlockedReason('2026-11-02', morning, 'ECONOMY', opts, now), null))
  it('blocks Premium inside the 4 hour lead time', () => assert.match(shiftBlockedReason('2026-11-02', morning, 'PREMIUM', opts, now) ?? '', /4 giờ/))
  it('allows Premium exactly 4 hours ahead', () => assert.equal(shiftBlockedReason('2026-11-02', morning, 'PREMIUM', opts, new Date('2026-11-01T21:00:00Z')), null))
})

describe('bookingProblem', () => {
  it('shows the first field message for 400', () => assert.equal(bookingProblem({ status: 400, fieldErrors: { shiftCode: ['Ca không hợp lệ.'] } }), 'Ca không hợp lệ.'))
  it('maps the contract 409 codes', () => {
    assert.match(bookingProblem({ status: 409, data: { code: 'SHIFT_IN_PAST' } }), /đã qua/)
    assert.match(bookingProblem({ status: 409, data: { code: 'PREMIUM_LEAD_TIME' } }), /4 giờ/)
  })
  it('falls back to the server message', () => assert.equal(bookingProblem({ status: 500, message: 'Lỗi máy chủ' }), 'Lỗi máy chủ'))
})

describe('price text and next order', () => {
  it('formats workers x unit price', () => assert.equal(workersPrice(2, 260000), '2 × 260.000 đ'))
  it('picks the soonest open order from today on', () => {
    const list = [order({ orderId: 1, scheduledDate: '2026-11-05' }), order({ orderId: 2, scheduledDate: '2026-11-03' }), order({ orderId: 3, scheduledDate: '2026-10-30' }), order({ orderId: 4, scheduledDate: '2026-11-02', orderStatus: 'CANCELLED' })]
    assert.equal(nextOrder(list, '2026-11-01')?.orderId, 2)
  })
  it('returns null when nothing is open', () => assert.equal(nextOrder([order({ orderStatus: 'COMPLETED' })], '2026-11-01'), null))
})
