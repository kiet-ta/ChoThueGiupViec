import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { bookingProblem, CONTINUOUS_WORK_AGREEMENT, CONTINUOUS_WORK_NOTICE, dayText, durationChoices, endTimeOf, firstName, hoursLabel, jobRange, nextOrder, quoteWorkersLabel, shiftBlockedReason, shiftStart, startsWorkerCanTake, startTimeChoices, upcomingDays, vnDate, workersPrice } from '../src/features/customer/booking/view.ts'
import type { FreeShift, OrderSummary } from '../src/features/customer/booking/types.ts'

const morning = { shiftCode: 'SHIFT_MORNING', startLocal: '08:00', endLocal: '12:00' }
const opts = { premiumMinLeadHours: 4 }
const order = (over: Partial<OrderSummary>): OrderSummary => ({
  orderId: 1, orderCode: 'GV1', serviceTier: 'ECONOMY', scheduledDate: '2026-11-03', shiftCode: 'SHIFT_MORNING',
  requiredWorkers: 1, totalAmount: 260000, orderStatus: 'PAID', createdAt: '2026-11-01T00:00:00Z', ...over,
})

describe('firstName', () => {
  it('uses the last word of a trimmed name', () => {
    assert.equal(firstName(''), '')
    assert.equal(firstName('  '), '')
    assert.equal(firstName('Lan'), 'Lan')
    assert.equal(firstName('Nguyễn Thị Lan'), 'Lan')
    assert.equal(firstName('  Nguyễn   Thị  Lan  '), 'Lan')
  })
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
  const free = [{ date: '2026-11-02', shiftCode: 'SHIFT_MORNING' }]
  it('allows a free worker shift', () => assert.equal(shiftBlockedReason('2026-11-02', morning, 'ECONOMY', opts, now, free), null))
  it('blocks a shift absent from a nonempty availability list', () => assert.equal(shiftBlockedReason('2026-11-03', morning, 'ECONOMY', opts, now, free), 'Thợ không rảnh ca này'))
  it('allows shifts while availability is loading or failed', () => assert.equal(shiftBlockedReason('2026-11-03', morning, 'ECONOMY', opts, now, null), null))
  it('allows shifts when no free shifts exist', () => assert.equal(shiftBlockedReason('2026-11-03', morning, 'ECONOMY', opts, now, []), null))
  it('ignores worker availability for Premium', () => assert.equal(shiftBlockedReason('2026-11-03', morning, 'PREMIUM', opts, now, free), null))
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
    assert.equal(bookingProblem({ status: 409, message: 'No capacity', data: { code: 'FULLY_BOOKED' } }), 'Ca này hiện không còn thợ rảnh ở khu vực của bạn. Hãy chọn ca hoặc ngày khác.')
    assert.equal(bookingProblem({ status: 409, message: 'Other conflict', data: { code: 'UNKNOWN' } }), 'Other conflict')
  })
  it('falls back to the server message', () => assert.equal(bookingProblem({ status: 500, message: 'Lỗi máy chủ' }), 'Lỗi máy chủ'))
})

describe('price text and next order', () => {
  it('formats workers x unit price', () => assert.equal(workersPrice(2, 260000), '2 × 260.000 đ'))
  const shifts = [morning, { shiftCode: 'SHIFT_AFTERNOON', startLocal: '13:00', endLocal: '17:00' }]
  const evening = new Date('2026-11-01T14:00:00Z') // 21:00 in Vietnam
  it('picks the soonest open order from today on', () => {
    const list = [order({ orderId: 1, scheduledDate: '2026-11-05' }), order({ orderId: 2, scheduledDate: '2026-11-03' }), order({ orderId: 3, scheduledDate: '2026-10-30' }), order({ orderId: 4, scheduledDate: '2026-11-02', orderStatus: 'CANCELLED' })]
    assert.equal(nextOrder(list, evening, shifts)?.orderId, 2)
  })
  it('skips a shift of today that already ended', () => {
    const list = [order({ orderId: 1, scheduledDate: '2026-11-01', shiftCode: 'SHIFT_AFTERNOON' }), order({ orderId: 2, scheduledDate: '2026-11-04' })]
    assert.equal(nextOrder(list, evening, shifts)?.orderId, 2)
    assert.equal(nextOrder(list, new Date('2026-11-01T08:00:00Z'), shifts)?.orderId, 1) // 15:00, shift still running
  })
  it('sorts by shift start time within a day', () => {
    const list = [order({ orderId: 1, scheduledDate: '2026-11-03', shiftCode: 'SHIFT_AFTERNOON' }), order({ orderId: 2, scheduledDate: '2026-11-03', shiftCode: 'SHIFT_MORNING' })]
    assert.equal(nextOrder(list, evening, shifts)?.orderId, 2)
  })
  it('returns null when nothing is open', () => assert.equal(nextOrder([order({ orderStatus: 'COMPLETED' })], evening, shifts), null))
})

describe('timed Economy job (decisions D34)', () => {
  const evening = { shiftCode: 'SHIFT_EVENING', startLocal: '17:30', endLocal: '20:30' }
  const rules = { shiftMaxHours: 4 }
  const otherDay = new Date('2026-11-15T03:00:00Z')

  it('offers lengths from 2 h to the shift maximum in half hours', () =>
    assert.deepEqual(durationChoices(rules), [2, 2.5, 3, 3.5, 4]))
  it('reads hours and the job time in Vietnamese', () => {
    assert.equal(hoursLabel(2.5), '2,5 giờ')
    assert.equal(endTimeOf('08:30', 2.5), '11:00')
    assert.equal(jobRange('08:30', 3), '08:30 → 11:30 (3 giờ)')
  })
  it('keeps the job inside the shift', () => {
    assert.deepEqual(startTimeChoices('2026-11-20', morning, 3, rules, otherDay), ['08:00', '08:30', '09:00'])
    assert.deepEqual(startTimeChoices('2026-11-20', evening, 4, rules, otherDay), [])
  })
  it('starts at least the lead time from now', () => {
    // 08:10 in Vietnam: the earliest start is 09:10, so 09:30.
    const now = new Date('2026-11-15T01:10:00Z')
    assert.deepEqual(startTimeChoices('2026-11-15', morning, 2, rules, now), ['09:30', '10:00'])
  })
  it('explains LEAD_TIME_TOO_SHORT from its code', () =>
    assert.match(bookingProblem({ status: 409, data: { code: 'LEAD_TIME_TOO_SHORT' } }), /muộn hơn/))
})

describe('large house: one worker, a possible 2nd job (decision D36)', () => {
  const small = { requiredWorkers: 1, unitPrice: 260000, totalAmount: 260000 }
  const large = { requiredWorkers: 1, unitPrice: 260000, totalAmount: 520000, isContinuousWork: true }
  const premiumLarge = { requiredWorkers: 2, unitPrice: 390000, totalAmount: 780000, isContinuousWork: false }
  it('says the single large-house price instead of "x N workers"', () => {
    assert.equal(quoteWorkersLabel(large), '1 thợ · giá nhà lớn 520.000 đ')
    assert.equal(quoteWorkersLabel(small), '1 × 260.000 đ')
    assert.equal(quoteWorkersLabel(premiumLarge), '2 × 390.000 đ')
  })
  it('words the notice and the agreement the customer must give', () => {
    assert.match(CONTINUOUS_WORK_NOTICE, /2 đơn liên tiếp/)
    assert.match(CONTINUOUS_WORK_NOTICE, /thêm giờ ngay sau ca/)
    assert.match(CONTINUOUS_WORK_NOTICE, /chỉ trả một lần/)
    assert.match(CONTINUOUS_WORK_AGREEMENT, /đồng ý/)
  })
})

describe('a favorite worker who is busy (BOOK-BUSY-01, decisions D41)', () => {
  const afternoon = ['13:00', '13:30', '14:00', '14:30', '15:00']
  const free = (over: Partial<FreeShift>): FreeShift => ({ date: '2026-11-17', shiftCode: 'SHIFT_AFTERNOON', ...over })

  it('keeps every start when the worker has no busy time or nothing is known', () => {
    assert.deepEqual(startsWorkerCanTake(afternoon, 2, undefined), afternoon)
    assert.deepEqual(startsWorkerCanTake(afternoon, 2, free({})), afternoon)
    assert.deepEqual(startsWorkerCanTake(afternoon, 2, free({ busyWindows: [], remainingHours: 8 })), afternoon)
  })

  it('drops the starts that overlap a live job (already widened by the buffer)', () => {
    // A job until 13:00 and the 30 minutes after it: nothing may start before 13:30.
    const busy = free({ busyWindows: [{ startTime: '11:30', endTime: '13:30' }], remainingHours: 6 })
    assert.deepEqual(startsWorkerCanTake(afternoon, 2, busy), ['13:30', '14:00', '14:30', '15:00'])
  })

  it('drops a start whose job would run into the next live job', () => {
    const busy = free({ busyWindows: [{ startTime: '15:30', endTime: '17:00' }] })
    // A 2 h job from 13:30 ends 15:30, exactly where the busy time begins; from 14:00 it overlaps.
    assert.deepEqual(startsWorkerCanTake(afternoon, 2, busy), ['13:00', '13:30'])
  })

  it('offers nothing when the whole shift is taken, or when the day has fewer hours left than the job', () => {
    assert.deepEqual(startsWorkerCanTake(afternoon, 2, free({ busyWindows: [{ startTime: '07:30', endTime: '17:30' }] })), [])
    assert.deepEqual(startsWorkerCanTake(afternoon, 2, free({ remainingHours: 1.5 })), [])
    assert.deepEqual(startsWorkerCanTake(afternoon, 2, free({ remainingHours: 2 })), afternoon)
  })

  it('says it in words when the server refuses a busy preferred worker', () => {
    assert.equal(
      bookingProblem({ status: 409, message: 'x', data: { code: 'PREFERRED_WORKER_BUSY' } } as never),
      'Thợ bạn chọn đã bận giờ này. Chọn giờ khác hoặc để hệ thống chọn thợ.',
    )
  })
})
