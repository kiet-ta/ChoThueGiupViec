import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { parsePositiveInt } from '../src/features/customer/route-id.ts'

describe('customer route ID parser', () => {
  it('parses valid positive integer params including surrounding whitespace', () => {
    assert.equal(parsePositiveInt('1'), 1)
    assert.equal(parsePositiveInt('42'), 42)
    assert.equal(parsePositiveInt(' 999 '), 999)
  })

  it('rejects non-numeric, zero, negative, fractional, leading-zero, or unsafe integers', () => {
    for (const raw of [undefined, '', '   ', 'abc', '0', '-5', '1.5', '12px', '01', '1e3', '9007199254740992']) {
      assert.equal(parsePositiveInt(raw), null, `expected null for ${String(raw)}`)
    }
  })
})
