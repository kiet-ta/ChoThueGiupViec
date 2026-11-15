import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { extractSandboxOtp } from '../src/auth/sandbox-otp.ts'

describe('extractSandboxOtp', () => {
  it('returns the trimmed 6-digit sandbox OTP when present', () => {
    assert.equal(
      extractSandboxOtp({
        sandboxOtp: ' 123456 ',
      }),
      '123456',
    )
  })

  it('returns null when sandboxOtp is absent, null, or blank', () => {
    assert.equal(extractSandboxOtp(undefined), null)
    assert.equal(extractSandboxOtp(null), null)
    assert.equal(extractSandboxOtp({}), null)
    assert.equal(extractSandboxOtp({ sandboxOtp: null }), null)
    assert.equal(extractSandboxOtp({ sandboxOtp: '' }), null)
    assert.equal(extractSandboxOtp({ sandboxOtp: '   ' }), null)
  })
})
