import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  ACTIONS,
  REASON_MAX_LENGTH,
  actionError,
  actionInfo,
  actionRequest,
  parseWorkerId,
  reasonText,
  successText,
  validateReason,
} from '../src/features/admin/worker-controls/view.ts'

describe('the three actions', () => {
  it('are approve, revoke and unlock, each with a hint and an effect an Admin reads before confirming', () => {
    assert.deepEqual(ACTIONS.map((a) => a.value), ['APPROVE_SUPER', 'REVOKE_SUPER', 'UNLOCK'])
    for (const a of ACTIONS) {
      assert.ok(a.label.length > 0 && a.hint.length > 0 && a.effect.length > 0, a.value)
    }
    assert.equal(actionInfo('UNLOCK').label, 'Mở khoá thợ')
  })
})

describe('parseWorkerId', () => {
  it('accepts a whole number of at least 1, trimmed', () => {
    assert.deepEqual(parseWorkerId(' 12 '), { id: 12 })
    assert.deepEqual(parseWorkerId('1'), { id: 1 })
    assert.deepEqual(parseWorkerId('2147483647'), { id: 2147483647 })
  })

  it('refuses empty, zero, negative, decimal, text and numbers beyond an int', () => {
    for (const bad of ['', '   ', '0', '-3', '1.5', '12a', 'abc', '2147483648', '99999999999999999999']) {
      assert.ok('error' in parseWorkerId(bad), `"${bad}" must be refused`)
    }
  })
})

describe('validateReason', () => {
  it('needs 1 to 255 characters after trimming, with the boundaries', () => {
    assert.match(validateReason('') ?? '', /nhập lý do/)
    assert.match(validateReason('   ') ?? '', /nhập lý do/)
    assert.equal(validateReason('a'), null)
    assert.equal(validateReason('a'.repeat(REASON_MAX_LENGTH)), null)
    assert.match(validateReason('a'.repeat(REASON_MAX_LENGTH + 1)) ?? '', /255/)
    assert.equal(validateReason('  ' + 'a'.repeat(REASON_MAX_LENGTH) + '  '), null) // trimmed first
  })
})

describe('actionRequest (contract admin.md 2.5 and 2.6)', () => {
  it('approve is a POST and revoke is a DELETE on the super-freelancer path, unlock a POST on the unlock path', () => {
    assert.deepEqual(actionRequest('APPROVE_SUPER', 5, ' top rated '), {
      method: 'POST',
      path: '/admin/workers/5/super-freelancer',
      body: { reason: 'top rated' },
    })
    assert.deepEqual(actionRequest('REVOKE_SUPER', 5, 'r'), { method: 'DELETE', path: '/admin/workers/5/super-freelancer', body: { reason: 'r' } })
    assert.deepEqual(actionRequest('UNLOCK', 12, 'warning served'), {
      method: 'POST',
      path: '/admin/workers/12/unlock',
      body: { reason: 'warning served' },
    })
  })

  it('always sends the reason as the body, also on a DELETE (the server refuses a revoke without it)', () => {
    assert.equal(JSON.stringify(actionRequest('REVOKE_SUPER', 1, ' x ').body), '{"reason":"x"}')
  })
})

describe('actionError', () => {
  const call = (status: number, data: unknown = null, fieldErrors: Record<string, string[]> = {}) => ({ status, message: 'm', data, fieldErrors })

  it('turns every failed criterion of an approval into words', () => {
    const e = actionError(
      'APPROVE_SUPER',
      call(409, { failedCriteria: ['RATING_BELOW_MINIMUM', 'COMPLETED_JOBS_BELOW_MINIMUM', 'KYC_NOT_APPROVED', 'UPHELD_DISPUTE_RECENT'] }),
    )
    assert.match(e.message, /Chưa đủ điều kiện/)
    assert.equal(e.reasons.length, 4)
    assert.match(e.reasons[0], /4,80/)
    assert.match(e.reasons[1], /50 ca/)
    assert.match(e.reasons[3], /180 ngày/)
  })

  it('turns the block reasons of an unlock into words and says the unlock is what was refused', () => {
    const e = actionError('UNLOCK', call(409, { blockReasons: ['NOT_LOCKED', 'KYC_NOT_APPROVED', 'NOT_FREELANCER'] }))
    assert.match(e.message, /Chưa thể mở khoá/)
    assert.equal(e.reasons.length, 3)
    assert.match(e.reasons[0], /không bị khoá/)
    assert.match(e.reasons[1], /eKYC/)
  })

  it('shows an unknown code as it is, and a 409 without codes in one plain line', () => {
    assert.equal(reasonText('SOMETHING_NEW'), 'SOMETHING_NEW')
    assert.match(actionError('UNLOCK', call(409)).message, /trạng thái của thợ/)
    assert.deepEqual(actionError('UNLOCK', call(409)).reasons, [])
  })

  it('maps 400 to the reason field message, 404, a network failure and anything else', () => {
    assert.equal(actionError('UNLOCK', call(400, null, { reason: ['A reason is required.'] })).message, 'A reason is required.')
    assert.match(actionError('UNLOCK', call(404)).message, /Không tìm thấy thợ/)
    assert.match(actionError('UNLOCK', call(0)).message, /Không kết nối/)
    assert.equal(actionError('UNLOCK', { status: 500, message: '', data: null }).message, 'Lỗi 500')
  })
})

describe('successText', () => {
  it('names the worker and the result', () => {
    assert.match(successText('APPROVE_SUPER', 7), /thợ #7.*Super-Freelancer/)
    assert.match(successText('REVOKE_SUPER', 7), /thu hồi.*#7/)
    assert.match(successText('UNLOCK', 7), /mở khoá thợ #7/)
  })
})
