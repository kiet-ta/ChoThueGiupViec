import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { disputeCode, formatDateTime, formatSlaRemaining, formatVnd, workerTypeLabel } from '../src/lib/format.ts'

describe('formatVnd', () => {
  it('groups thousands with a dot and ends with the dong sign', () => {
    assert.equal(formatVnd(0), '0 đ')
    assert.equal(formatVnd(999), '999 đ')
    assert.equal(formatVnd(1000), '1.000 đ')
    assert.equal(formatVnd(260000), '260.000 đ')
    assert.equal(formatVnd(1234567), '1.234.567 đ')
    assert.equal(formatVnd(1000000000), '1.000.000.000 đ')
  })

  it('rounds to whole VND and keeps the sign of a deduction', () => {
    assert.equal(formatVnd(1999.6), '2.000 đ')
    assert.equal(formatVnd(0.4), '0 đ')
    assert.equal(formatVnd(-52000), '-52.000 đ')
    assert.equal(formatVnd(-0.2), '0 đ')
  })
})

describe('formatDateTime', () => {
  it('shows an instant in Asia/Ho_Chi_Minh (UTC+7)', () => {
    assert.equal(formatDateTime('2026-10-07T04:00:00Z'), '07/10/2026 11:00')
  })

  it('moves to the next local day when UTC is still the day before', () => {
    assert.equal(formatDateTime('2026-10-11T17:00:00Z'), '12/10/2026 00:00')
    assert.equal(formatDateTime('2026-10-11T16:59:59Z'), '11/10/2026 23:59')
  })

  it('moves to the next local year at the turn of the year', () => {
    assert.equal(formatDateTime('2026-12-31T17:00:00Z'), '01/01/2027 00:00')
  })

  it('reads an offset in the text and always shows 24-hour time', () => {
    assert.equal(formatDateTime('2026-10-07T12:05:00+07:00'), '07/10/2026 12:05')
    assert.equal(formatDateTime('2026-10-07T00:00:00Z'), '07/10/2026 07:00')
  })

  it('can show another zone when asked', () => {
    assert.equal(formatDateTime('2026-10-07T04:00:00Z', 'UTC'), '07/10/2026 04:00')
  })

  it('is empty for a missing or invalid value', () => {
    assert.equal(formatDateTime(null), '')
    assert.equal(formatDateTime(undefined), '')
    assert.equal(formatDateTime(''), '')
    assert.equal(formatDateTime('not a date'), '')
  })
})

describe('formatSlaRemaining', () => {
  it('shows hours and minutes with two digits, like the design', () => {
    assert.equal(formatSlaRemaining(6300), '01h 45p')
    assert.equal(formatSlaRemaining(11520), '03h 12p')
    assert.equal(formatSlaRemaining(3600), '01h 00p')
  })

  it('floors seconds to whole minutes', () => {
    assert.equal(formatSlaRemaining(59), '00h 00p')
    assert.equal(formatSlaRemaining(60), '00h 01p')
    assert.equal(formatSlaRemaining(119), '00h 01p')
  })

  it('is 00h 00p at exactly zero', () => {
    assert.equal(formatSlaRemaining(0), '00h 00p')
  })

  it('keeps counting hours past a day (the design shows 29h 05p)', () => {
    assert.equal(formatSlaRemaining(29 * 3600 + 5 * 60), '29h 05p')
    assert.equal(formatSlaRemaining(48 * 3600), '48h 00p')
  })

  it('says overdue for a negative time', () => {
    assert.equal(formatSlaRemaining(-1), 'Quá hạn 00h 00p')
    assert.equal(formatSlaRemaining(-(2 * 3600 + 10 * 60)), 'Quá hạn 02h 10p')
  })
})

describe('labels', () => {
  it('writes the dispute code as the design does', () => {
    assert.equal(disputeCode(1042), '#TC-1042')
  })

  it('names a worker type with the agency when there is one', () => {
    assert.equal(workerTypeLabel('FREELANCER', null), 'Freelancer')
    assert.equal(workerTypeLabel('FREELANCER', 'Ignored'), 'Freelancer')
    assert.equal(workerTypeLabel('AGENCY_STAFF', 'CleanPro Solutions'), 'Agency · CleanPro Solutions')
    assert.equal(workerTypeLabel('AGENCY_STAFF', null), 'Agency')
  })
})
