import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { paymentRow, problemText, subjectOf } from '../src/features/dev-payments/view.ts'
import type { DevPayment } from '../src/features/dev-payments/types.ts'

const base: DevPayment = {
  paymentId: 3, purpose: 'ORDER', orderId: 12, subscriptionId: null, agencyId: null, amount: 390000,
  gatewayTxnRef: 'FAKE-abc', createdAt: '2026-10-14T09:11:44Z',
}

describe('dev payment rows', () => {
  it('names what the payment is for with the id a tester recognises', () => {
    assert.equal(subjectOf(base), 'Đơn hàng · đơn #12')
    assert.equal(subjectOf({ ...base, purpose: 'ESCROWDEPOSIT', orderId: null, agencyId: 4 }), 'Nạp ký quỹ · doanh nghiệp #4')
    assert.equal(subjectOf({ ...base, purpose: 'SUBSCRIPTION', orderId: null, subscriptionId: 8 }), 'Mua gói thuê bao · gói #8')
  })

  it('marks only the Fake gateway references as confirmable', () => {
    assert.equal(paymentRow(base).isFake, true)
    assert.equal(paymentRow({ ...base, gatewayTxnRef: 'MOMO-1' }).isFake, false)
    assert.equal(paymentRow(base).reference, 'FAKE-abc')
  })
})

describe('problem text', () => {
  it('explains a 404 and a 409', () => {
    assert.match(problemText(404, undefined), /Development/)
    assert.match(problemText(409, undefined), /FAKE-/)
    assert.equal(problemText(500, 'boom'), 'boom')
  })
})
