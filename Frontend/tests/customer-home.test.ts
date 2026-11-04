import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { monthStats, topFavorites } from '../src/features/customer/home/view.ts'
import type { OrderSummary, ShiftOption } from '../src/features/customer/booking/types.ts'
import type { FavoriteWorker } from '../src/features/customer/account/types.ts'

const shift: ShiftOption = { shiftCode: 'MORNING', startLocal: '08:00', endLocal: '11:30' }
const order = (over: Partial<OrderSummary> = {}): OrderSummary => ({ orderId: 1, orderCode: 'A', serviceTier: 'ECONOMY', scheduledDate: '2026-11-01', shiftCode: 'MORNING', requiredWorkers: 1, totalAmount: 1, orderStatus: 'COMPLETED', createdAt: '', ...over })
const favorite = (workerId: number, ratingAvg: number, completedJobs: number): FavoriteWorker => ({ workerId, ratingAvg, completedJobs, fullName: 'Worker', workStatus: 'IDLE', addedAt: '' })

describe('monthStats', () => {
  it('returns zero for no orders', () => assert.deepEqual(monthStats([], [shift], new Date('2026-11-01T00:00:00Z')), { count: 0, hours: 0 }))
  it('uses Vietnam month at 00:30 on the first', () => assert.deepEqual(monthStats([order(), order({ scheduledDate: '2026-10-31' })], [shift], new Date('2026-10-31T17:30:00Z')), { count: 1, hours: 3.5 }))
  it('counts two workers twice, ignoring unknown shifts for hours', () => assert.deepEqual(monthStats([order({ requiredWorkers: 2 }), order({ shiftCode: 'UNKNOWN' })], [shift], new Date('2026-11-02T00:00:00Z')), { count: 2, hours: 7 }))
})

describe('topFavorites', () => {
  it('returns at most three by rating then completed jobs without mutating input', () => {
    const list = [favorite(1, 4.5, 10), favorite(2, 5, 3), favorite(3, 5, 9), favorite(4, 4, 99)]
    assert.deepEqual(topFavorites(list, 3).map((x) => x.workerId), [3, 2, 1])
    assert.deepEqual(list.map((x) => x.workerId), [1, 2, 3, 4])
  })
})
