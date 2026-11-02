import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  ROUND_STEP_VND,
  TWO_WORKER_FLOOR_VND,
  TWO_WORKER_THRESHOLD_M2,
  estimatePrice,
} from '../src/features/landing/pricing.ts'

describe('landing price estimate (ticket #569)', () => {
  it('apartment at the calculator default (65 m2) is 290.000 with one worker', () => {
    // 200.000 + 65 * 1.400 = 291.000, rounded to the nearest 10.000
    assert.deepEqual(estimatePrice('apartment', 65), { priceVnd: 290_000, workers: 1 })
  })

  it('studio is flat 180.000 up to 40 m2, then adds 1.500 per extra m2', () => {
    assert.equal(estimatePrice('studio', 30).priceVnd, 180_000)
    assert.equal(estimatePrice('studio', 40).priceVnd, 180_000)
    assert.equal(estimatePrice('studio', 60).priceVnd, 210_000) // 180.000 + 20 * 1.500
  })

  it('BR-21 threshold: 80 m2 is still one worker, 81 m2 is two', () => {
    assert.equal(estimatePrice('apartment', TWO_WORKER_THRESHOLD_M2).workers, 1)
    assert.equal(estimatePrice('apartment', TWO_WORKER_THRESHOLD_M2 + 1).workers, 2)
  })

  it('a townhouse always gets two workers, even below the threshold', () => {
    const e = estimatePrice('townhouse', 50) // 360.000 + 50 * 1.800 = 450.000
    assert.deepEqual(e, { priceVnd: 450_000, workers: 2 })
  })

  it('two-worker estimates are never below the 420.000 floor', () => {
    // apartment 81 m2 would be 313.400 without the floor
    assert.equal(estimatePrice('apartment', 81).priceVnd, TWO_WORKER_FLOOR_VND)
    // the floor does not apply to one worker
    assert.equal(estimatePrice('apartment', 80).priceVnd, 310_000)
  })

  it('always rounds to a multiple of 10.000', () => {
    for (const type of ['apartment', 'studio', 'townhouse'] as const) {
      for (let area = 30; area <= 250; area += 7) {
        assert.equal(estimatePrice(type, area).priceVnd % ROUND_STEP_VND, 0, `${type} ${area}`)
      }
    }
  })

  it('largest apartment (250 m2) is 550.000 with two workers', () => {
    assert.deepEqual(estimatePrice('apartment', 250), { priceVnd: 550_000, workers: 2 })
  })
})
