import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  accountProblem, areaBracket, areaText, bandText, canBookWorker, formFromAddress, initials, parseNumber, phoneText, requiredWorkers,
  toAddressBody, totalArea, validateAddress, validateProfile, wardsOf, workStatusLabel, type AddressForm,
} from '../src/features/customer/account/view.ts'
import { vnAdminUnits } from '../src/features/customer/account/vn-admin-units.ts'
import type { Address } from '../src/features/customer/account/types.ts'

const form = (over: Partial<AddressForm> = {}): AddressForm => ({
  label: 'Nhà', city: 'Thành phố Hồ Chí Minh', district: 'Phường Sài Gòn', addressLine: '12 Lê Lợi', housingType: 'HOUSE', floorAreaM2: '29', numFloors: '2',
  isDefault: false, latitude: 10.7769, longitude: 106.7009, ...over,
})

describe('totalArea (floorAreaM2 x numFloors, rounded like the server)', () => {
  it('multiplies area by floors', () => {
    assert.equal(totalArea(29, 2), 58)
    assert.equal(totalArea(120, 1), 120)
    assert.equal(totalArea(12.5, 3), 37.5)
  })
  it('rounds to 2 decimals, half away from zero', () => {
    assert.equal(totalArea(33.335, 1), 33.34)
    assert.equal(totalArea(0.1, 3), 0.3)
  })
  it('is null for missing or non-positive input', () => {
    assert.equal(totalArea(null, 2), null)
    assert.equal(totalArea(29, null), null)
    assert.equal(totalArea(0, 2), null)
    assert.equal(totalArea(29, 0), null)
    assert.equal(totalArea(-5, 2), null)
  })
})

describe('price band and workers (booking.md 1.3)', () => {
  it('uses the contract boundaries 30 / 80', () => {
    assert.equal(areaBracket(30), 'UP_TO_30')
    assert.equal(areaBracket(30.01), 'FROM_31_TO_80')
    assert.equal(areaBracket(80), 'FROM_31_TO_80')
    assert.equal(areaBracket(80.01), 'OVER_80')
  })
  it('needs 1 worker up to 80 m2 and exactly 2 above', () => {
    assert.equal(requiredWorkers(80), 1)
    assert.equal(requiredWorkers(80.01), 2)
  })
  it('writes the band under the area', () => {
    assert.equal(bandText(58), 'Khung giá 31–80 m² · 1 thợ')
    assert.equal(bandText(120), 'Khung giá Trên 80 m² · 2 thợ')
    assert.equal(areaText({ floorAreaM2: 29, numFloors: 2, totalAreaM2: 58 }), '29 m² × 2 tầng = 58 m²')
  })
})

describe('validateAddress (customers.md 2.2)', () => {
  it('accepts a complete form', () => assert.deepEqual(validateAddress(form()), {}))
  it('requires label, province, ward and street', () => {
    const e = validateAddress(form({ label: ' ', city: '', district: '', addressLine: '' }))
    assert.ok(e.label && e.city && e.district && e.addressLine)
    assert.ok(validateAddress(form({ label: 'x'.repeat(51) })).label)
    assert.ok(validateAddress(form({ addressLine: 'x'.repeat(256) })).addressLine)
  })
  it('checks the area: > 0, <= 9999.99, a room <= 30', () => {
    assert.ok(validateAddress(form({ floorAreaM2: '' })).floorAreaM2)
    assert.ok(validateAddress(form({ floorAreaM2: '0' })).floorAreaM2)
    assert.ok(validateAddress(form({ floorAreaM2: '10000' })).floorAreaM2)
    assert.equal(validateAddress(form({ floorAreaM2: '9999,99' })).floorAreaM2, undefined)
    assert.equal(validateAddress(form({ housingType: 'ROOM', floorAreaM2: '30', numFloors: '1' })).floorAreaM2, undefined)
    assert.match(validateAddress(form({ housingType: 'ROOM', floorAreaM2: '30.5', numFloors: '1' })).floorAreaM2!, /tối đa 30/)
  })
  it('checks floors: apartments and rooms 1, houses 1 to 10', () => {
    assert.ok(validateAddress(form({ housingType: 'APARTMENT', numFloors: '2' })).numFloors)
    assert.ok(validateAddress(form({ housingType: 'ROOM', floorAreaM2: '20', numFloors: '3' })).numFloors)
    assert.equal(validateAddress(form({ numFloors: '10' })).numFloors, undefined)
    assert.match(validateAddress(form({ numFloors: '11' })).numFloors!, /tối đa 10/)
    assert.ok(validateAddress(form({ numFloors: '0' })).numFloors)
    assert.ok(validateAddress(form({ numFloors: '1.5' })).numFloors)
    assert.ok(validateAddress(form({ numFloors: '' })).numFloors)
  })
  it('checks the coordinates', () => {
    assert.ok(validateAddress(form({ latitude: 91 })).location)
    assert.ok(validateAddress(form({ longitude: -181 })).location)
  })
})

describe('address body and form', () => {
  it('builds the request without totalAreaM2', () => {
    const body = toAddressBody(form({ label: ' Nhà ', floorAreaM2: '29,5', isDefault: true }))
    assert.equal(body.label, 'Nhà')
    assert.equal(body.floorAreaM2, 29.5)
    assert.equal(body.numFloors, 2)
    assert.equal(body.isDefault, true)
    assert.ok(!('totalAreaM2' in body))
  })
  it('fills the form from an address, with an unknown housing type falling back to an apartment', () => {
    const a = { addressId: 1, label: 'Nhà', addressLine: '12 Lê Lợi', district: 'Phường Sài Gòn', city: 'Thành phố Hồ Chí Minh', housingType: 'VILLA', floorAreaM2: 29, numFloors: 2,
      totalAreaM2: 58, bedrooms: null, bathrooms: null, latitude: 10.7, longitude: 106.7, isDefault: true, createdAt: '2026-11-01T00:00:00Z' } as Address
    const f = formFromAddress(a)
    assert.deepEqual([f.housingType, f.floorAreaM2, f.numFloors, f.isDefault], ['APARTMENT', '29', '2', true])
  })
  it('reads numbers with a decimal comma', () => {
    assert.equal(parseNumber('29,5'), 29.5)
    assert.equal(parseNumber(' '), null)
    assert.equal(parseNumber('abc'), null)
  })
})

describe('ward catalogue (in force since 2025-07-01)', () => {
  it('has 34 provinces and 3,321 wards, keyed by the name the API stores in `city`', () => {
    assert.equal(Object.keys(vnAdminUnits).length, 34)
    assert.equal(Object.values(vnAdminUnits).reduce((n, w) => n + w.length, 0), 3321)
    assert.ok(wardsOf(vnAdminUnits, 'Thành phố Hồ Chí Minh').includes('Phường Sài Gòn'))
    assert.deepEqual(wardsOf(vnAdminUnits, 'Không có'), [])
  })
})

describe('profile and favorites helpers', () => {
  it('validates the profile (name 1 to 100, email valid or empty)', () => {
    assert.deepEqual(validateProfile('Nguyễn Lan', ''), {})
    assert.deepEqual(validateProfile('Nguyễn Lan', 'lan@example.com'), {})
    assert.ok(validateProfile(' ', '').fullName)
    assert.ok(validateProfile('x'.repeat(101), '').fullName)
    assert.ok(validateProfile('Lan', 'not-an-email').email)
  })
  it('formats a phone number and initials', () => {
    assert.equal(phoneText('0912345678'), '0912 345 678')
    assert.equal(phoneText('+84912345678'), '+84912345678')
    assert.equal(initials('Nguyễn Thị Mai'), 'TM')
    assert.equal(initials('  '), '?')
  })
  it('labels the worker status and only lets bookable workers be booked', () => {
    assert.equal(workStatusLabel('IDLE'), 'Đang hoạt động')
    assert.equal(workStatusLabel('NEW_VALUE'), 'NEW_VALUE')
    assert.equal(canBookWorker('IDLE'), true)
    assert.equal(canBookWorker('BUSY'), true)
    for (const s of ['LOCKED', 'RETIRING', 'RETIRED', 'PENDING']) assert.equal(canBookWorker(s), false, s)
  })
  it('maps failures', () => {
    assert.equal(accountProblem({ status: 400, fieldErrors: { label: ['Quá dài.'] } }), 'Quá dài.')
    assert.match(accountProblem({ status: 409 }, true), /đã có đơn/)
    assert.match(accountProblem({ status: 404 }), /Không tìm thấy/)
    assert.equal(accountProblem({ status: 500, message: 'Lỗi' }), 'Lỗi')
  })
})
