import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { cleanDigits, distributePastedDigits } from '../src/components/ui/otp-logic.ts'

describe('cleanDigits', () => {
  it('strips non-digits and truncates to specified length', () => {
    assert.equal(cleanDigits('123456'), '123456')
    assert.equal(cleanDigits('12-34-56'), '123456')
    assert.equal(cleanDigits(' 1 2 3 a b c 4 5 6 '), '123456')
    assert.equal(cleanDigits('1234567890', 6), '123456')
    assert.equal(cleanDigits('abc', 6), '')
  })
})

describe('distributePastedDigits', () => {
  it('distributes 6-digit text starting at index 0 and sets focus to end', () => {
    const current = ['', '', '', '', '', '']
    const result = distributePastedDigits(current, 0, '654321', 6)
    assert.equal(result.code, '654321')
    assert.equal(result.nextFocus, 5)
  })

  it('distributes partial digits starting at mid-index', () => {
    const current = ['1', '2', '', '', '', '']
    const result = distributePastedDigits(current, 2, '34', 6)
    assert.equal(result.code, '1234')
    assert.equal(result.nextFocus, 4)
  })

  it('ignores non-numeric characters in pasted clipboard text', () => {
    const current = ['', '', '', '', '', '']
    const result = distributePastedDigits(current, 0, 'Code: 987-654', 6)
    assert.equal(result.code, '987654')
    assert.equal(result.nextFocus, 5)
  })

  it('truncates overflow pasted text at max length', () => {
    const current = ['', '', '', '', '', '']
    const result = distributePastedDigits(current, 0, '1234567890', 6)
    assert.equal(result.code, '123456')
    assert.equal(result.nextFocus, 5)
  })
})
