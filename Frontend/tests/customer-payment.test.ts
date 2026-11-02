import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { formatCountdown, paymentState, qrImageUrl, qrProblem, secondsLeft, shouldPoll, stateView } from '../src/features/customer/payment/view.ts'

describe('paymentState', () => {
  it('maps the wire statuses', () => {
    assert.equal(paymentState('PENDING'), 'PENDING')
    assert.equal(paymentState('SUCCESS'), 'PAID')
    assert.equal(paymentState('PAID'), 'PAID')
    assert.equal(paymentState('EXPIRED'), 'EXPIRED')
    assert.equal(paymentState('FAILED'), 'FAILED')
  })
  it('never reads an unknown status as paid', () => assert.equal(paymentState('WHATEVER'), 'FAILED'))
  it('polls only while pending', () => {
    assert.equal(shouldPoll('PENDING'), true)
    for (const s of ['SUCCESS', 'PAID', 'EXPIRED', 'FAILED']) assert.equal(shouldPoll(s), false)
  })
})

describe('countdown', () => {
  const now = new Date('2026-11-01T10:00:00Z')
  it('counts whole seconds', () => assert.equal(secondsLeft('2026-11-01T10:14:21Z', now), 14 * 60 + 21))
  it('never goes below zero', () => assert.equal(secondsLeft('2026-11-01T09:00:00Z', now), 0))
  it('treats an invalid time as expired', () => assert.equal(secondsLeft('x', now), 0))
  it('formats mm:ss', () => {
    assert.equal(formatCountdown(861), '14:21')
    assert.equal(formatCountdown(5), '00:05')
    assert.equal(formatCountdown(-3), '00:00')
  })
})

describe('state words and QR problems', () => {
  it('gives every state a label and an icon', () => {
    for (const s of ['PENDING', 'PAID', 'EXPIRED', 'FAILED'] as const) {
      const v = stateView(s)
      assert.ok(v.label.length > 0 && v.icon)
    }
    assert.match(stateView('EXPIRED').detail, /đặt lại/)
  })
  it('flags an expired order on 409 PAYMENT_EXPIRED', () => assert.equal(qrProblem({ status: 409, data: { code: 'PAYMENT_EXPIRED' } }).expired, true))
  it('lets the user retry on 502', () => {
    const p = qrProblem({ status: 502 })
    assert.equal(p.expired, false)
    assert.match(p.text, /thử lại/)
  })
})

describe('qrImageUrl', () => {
  it('shows the SePay VietQR image', () => {
    const url = 'https://qr.sepay.vn/img?acc=1&bank=MB&amount=260000&des=TA1&template=compact'
    assert.equal(qrImageUrl(url), url)
  })
  it('accepts an https image file', () => assert.equal(qrImageUrl('https://cdn.test/qr.png'), 'https://cdn.test/qr.png'))
  it('refuses plain text, http and deeplinks', () => {
    assert.equal(qrImageUrl('FAKE-QR-123'), null)
    assert.equal(qrImageUrl('http://qr.sepay.vn/img?x=1'), null)
    assert.equal(qrImageUrl('momo://pay?x=1'), null)
    assert.equal(qrImageUrl('https://test-payment.momo.vn/pay/abc'), null)
    assert.equal(qrImageUrl(null), null)
  })
})
