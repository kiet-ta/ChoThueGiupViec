import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  bracketLabel,
  historyRow,
  MAX_UNIT_PRICE,
  parseUnitPrice,
  REASON_MAX_LENGTH,
  ruleRow,
  saveError,
  sortRules,
  tierLabel,
  validateReason,
} from '../src/features/booking/price-rules/view.ts'
import type { AuditLogEntry, PriceRule } from '../src/features/booking/price-rules/types.ts'

const rule = (over: Partial<PriceRule> = {}): PriceRule => ({
  ruleId: 3,
  serviceTier: 'ECONOMY',
  areaBracket: 'UP_TO_30',
  unitPrice: 160000,
  isActive: true,
  updatedAt: '2026-10-07T04:00:00Z',
  updatedBy: null,
  ...over,
})

const entry = (over: Partial<AuditLogEntry> = {}): AuditLogEntry => ({
  logId: 9,
  actorType: 'ADMIN',
  adminId: 5,
  entityType: 'PRICE_RULE',
  entityId: '3',
  fieldName: 'unit_price',
  oldValue: '160000',
  newValue: '175000',
  reason: 'Seasonal demand',
  changedAt: '2026-10-07T04:00:00Z',
  ...over,
})

describe('price rule rows', () => {
  it('shows the tier, the bracket in words and the price in VND', () => {
    const row = ruleRow(rule())
    assert.equal(row.tier, 'Economy')
    assert.equal(row.bracket, 'Đến 30 m²')
    assert.equal(row.priceText, '160.000 đ')
    assert.equal(row.updatedText, '07/10/2026 11:00')
    assert.equal(row.active, true)
  })

  it('labels every bracket and tier of the contract and leaves an unknown code as it is', () => {
    assert.equal(bracketLabel('FROM_31_TO_80'), 'Trên 30 đến 80 m²')
    assert.equal(bracketLabel('OVER_80'), 'Trên 80 m² (2 thợ)')
    assert.equal(bracketLabel('SOMETHING_NEW'), 'SOMETHING_NEW')
    assert.equal(tierLabel('PREMIUM'), 'Premium')
    assert.equal(tierLabel('GOLD'), 'GOLD')
  })

  it('orders Economy before Premium and the brackets from small to large', () => {
    const sorted = sortRules([
      rule({ ruleId: 6, serviceTier: 'PREMIUM', areaBracket: 'OVER_80' }),
      rule({ ruleId: 2, serviceTier: 'ECONOMY', areaBracket: 'FROM_31_TO_80' }),
      rule({ ruleId: 4, serviceTier: 'PREMIUM', areaBracket: 'UP_TO_30' }),
      rule({ ruleId: 3, serviceTier: 'ECONOMY', areaBracket: 'OVER_80' }),
      rule({ ruleId: 1, serviceTier: 'ECONOMY', areaBracket: 'UP_TO_30' }),
    ])
    assert.deepEqual(sorted.map((r) => r.ruleId), [1, 2, 3, 4, 6])
  })

  it('does not change the list it was given', () => {
    const input = [rule({ ruleId: 2, serviceTier: 'PREMIUM' }), rule({ ruleId: 1 })]
    sortRules(input)
    assert.deepEqual(input.map((r) => r.ruleId), [2, 1])
  })
})

describe('new unit price', () => {
  it('accepts whole VND, also when grouped with dots, commas or spaces', () => {
    assert.deepEqual(parseUnitPrice('175000', 160000), { value: 175000, error: null })
    assert.deepEqual(parseUnitPrice(' 175.000 ', 160000), { value: 175000, error: null })
    assert.deepEqual(parseUnitPrice('1,750,000', 160000), { value: 1750000, error: null })
    assert.deepEqual(parseUnitPrice('1 750 000', 160000), { value: 1750000, error: null })
  })

  it('accepts 1 and the largest exact integer', () => {
    assert.equal(parseUnitPrice('1', 160000).value, 1)
    assert.equal(parseUnitPrice(String(MAX_UNIT_PRICE), 160000).value, MAX_UNIT_PRICE)
  })

  it('refuses an empty text, zero, a negative number, letters and a fraction of a dong', () => {
    for (const text of ['', '   ', '0', '000', '-5', '12a', 'abc', '1e5', '160000.5đ']) {
      const result = parseUnitPrice(text, 160000)
      assert.equal(result.value, null, text)
      assert.notEqual(result.error, null, text)
    }
  })

  it('never reads a decimal point as a thousands separator', () => {
    // 160000.5 must not become 1,600,005, and 1.5 must not become 15.
    for (const text of ['160000.5', '1.5', '12.34', '1.2345', '160,5', '1..000', '.500', '500.']) {
      const result = parseUnitPrice(text, 160000)
      assert.equal(result.value, null, text)
      assert.notEqual(result.error, null, text)
    }
  })

  it('refuses a price no number can hold exactly', () => {
    assert.equal(parseUnitPrice('9007199254740993', 160000).value, null)
    assert.equal(parseUnitPrice('99999999999999999999', 160000).value, null)
  })

  it('refuses the price the rule already has, because the server would write nothing', () => {
    const result = parseUnitPrice('160.000', 160000)
    assert.equal(result.value, null)
    assert.match(result.error ?? '', /trùng/)
  })
})

describe('reason', () => {
  it('is required and limited to 255 characters after trimming', () => {
    assert.notEqual(validateReason(''), null)
    assert.notEqual(validateReason('   '), null)
    assert.equal(validateReason('ok'), null)
    assert.equal(validateReason('r'.repeat(REASON_MAX_LENGTH)), null)
    assert.equal(validateReason(`  ${'r'.repeat(REASON_MAX_LENGTH)}  `), null)
    assert.notEqual(validateReason('r'.repeat(REASON_MAX_LENGTH + 1)), null)
  })
})

describe('a failed save', () => {
  it('puts the 400 messages under their fields', () => {
    const err = saveError({ status: 400, message: 'Validation failed', fieldErrors: { unitPrice: ['too big'], reason: ['required'] } })
    assert.deepEqual(err, { message: null, unitPrice: 'too big', reason: 'required' })
  })

  it('shows the server message for a 400 without field errors', () => {
    assert.deepEqual(saveError({ status: 400, message: 'Bad body' }), { message: 'Bad body', unitPrice: null, reason: null })
  })

  it('explains 404, 401, 403 and a network failure in words', () => {
    assert.match(saveError({ status: 404, message: '' }).message ?? '', /Không tìm thấy/)
    assert.match(saveError({ status: 401, message: '' }).message ?? '', /không có quyền/)
    assert.match(saveError({ status: 403, message: '' }).message ?? '', /không có quyền/)
    assert.match(saveError({ status: 0, message: '' }).message ?? '', /Không kết nối/)
  })

  it('falls back to the server message or the status for anything else', () => {
    assert.equal(saveError({ status: 500, message: 'Boom' }).message, 'Boom')
    assert.equal(saveError({ status: 503, message: '' }).message, 'Lỗi 503')
  })
})

describe('history rows', () => {
  it('shows when, who, the old and new price and the reason', () => {
    assert.deepEqual(historyRow(entry()), {
      logId: 9,
      when: '07/10/2026 11:00',
      who: 'Admin #5',
      oldPrice: '160.000 đ',
      newPrice: '175.000 đ',
      reason: 'Seasonal demand',
    })
  })

  it('names the system for a SYSTEM entry and an Admin without an id', () => {
    assert.equal(historyRow(entry({ actorType: 'SYSTEM', adminId: null })).who, 'Hệ thống')
    assert.equal(historyRow(entry({ adminId: null })).who, 'Admin')
  })

  it('shows a dash for a missing value or reason and keeps a value that is not a number', () => {
    const row = historyRow(entry({ oldValue: null, newValue: 'n/a', reason: '  ' }))
    assert.equal(row.oldPrice, '–')
    assert.equal(row.newPrice, 'n/a')
    assert.equal(row.reason, '–')
  })
})
