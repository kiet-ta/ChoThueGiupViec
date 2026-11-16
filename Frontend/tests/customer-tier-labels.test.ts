import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { tierLabel } from '../src/features/customer/booking/view.ts'

describe('customer package names (WEB-ORDERS-02)', () => {
  it('uses the names of the Mobile app', () => {
    assert.equal(tierLabel('ECONOMY'), 'Cơ bản')
    assert.equal(tierLabel('PREMIUM'), 'Cao cấp')
  })
})
