import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { fillFromGeocode, latestRequest, matchCity, matchWard, placeText, roundCoordinate } from '../src/features/customer/account/view.ts'
import { vnAdminUnits } from '../src/features/customer/account/vn-admin-units.ts'
import type { ReverseGeocodeResult } from '../src/features/customer/account/types.ts'

const HCM = 'Thành phố Hồ Chí Minh'
const read = (over: Partial<ReverseGeocodeResult> = {}): ReverseGeocodeResult => ({
  city: HCM, district: '', addressLine: '', formattedAddress: '', latitude: 10.7724, longitude: 106.698, ...over,
})

// ADDR-MAP-02: the address read at the point clicked on the map fills the Web address form.
describe('matchCity', () => {
  it('keeps a catalogue name and finds one written another way', () => {
    assert.equal(matchCity(vnAdminUnits, HCM), HCM)
    assert.equal(matchCity(vnAdminUnits, 'Hồ Chí Minh'), HCM)
    assert.equal(matchCity(vnAdminUnits, 'TP. Hồ Chí Minh'), HCM)
    assert.equal(matchCity(vnAdminUnits, 'thanh pho ha noi'), 'Thành phố Hà Nội')
  })
  it('returns null for an unknown or empty name', () => {
    assert.equal(matchCity(vnAdminUnits, 'Nowhere'), null)
    assert.equal(matchCity(vnAdminUnits, '  '), null)
    assert.equal(matchCity(vnAdminUnits, undefined), null)
  })
})

describe('matchWard', () => {
  it('adds the prefix the server leaves out ("Bến Thành" -> "Phường Bến Thành"), ignoring case and accents', () => {
    assert.equal(matchWard(vnAdminUnits, HCM, 'Bến Thành'), 'Phường Bến Thành')
    assert.equal(matchWard(vnAdminUnits, HCM, 'phuong ben thanh'), 'Phường Bến Thành')
    assert.equal(matchWard(vnAdminUnits, HCM, 'Phường Bến Thành'), 'Phường Bến Thành')
    assert.equal(matchWard(vnAdminUnits, 'Thành phố Hà Nội', 'Hoàn Kiếm'), 'Phường Hoàn Kiếm')
  })
  it('reads the ward from a part of the full address when the ward name is missing', () => {
    assert.equal(matchWard(vnAdminUnits, HCM, '', 'Điện Biên Phủ, Thạnh Mỹ Tây, Thành phố Hồ Chí Minh, Việt Nam'), 'Phường Thạnh Mỹ Tây')
  })
  it('never guesses: an unknown ward, a ward of another province or no province gives null', () => {
    assert.equal(matchWard(vnAdminUnits, HCM, 'Khu phố Không Có'), null)
    assert.equal(matchWard(vnAdminUnits, HCM, 'Hoàn Kiếm'), null)
    assert.equal(matchWard(vnAdminUnits, null, 'Bến Thành'), null)
  })
})

describe('fillFromGeocode', () => {
  it('fills the street, the province and the matched ward (the real server answer for 10.7724, 106.698)', () => {
    const fill = fillFromGeocode(vnAdminUnits, read({
      district: 'Bến Thành', addressLine: 'Công trường Quách Thị Trang',
      formattedAddress: 'Công trường Quách Thị Trang, Bến Thành, Thành phố Hồ Chí Minh, Việt Nam',
    }), HCM)
    assert.deepEqual(fill, { addressLine: 'Công trường Quách Thị Trang', city: HCM, district: 'Phường Bến Thành' })
  })
  it('moves to another province with its ward', () => {
    const fill = fillFromGeocode(vnAdminUnits, read({ city: 'Thành phố Hà Nội', district: 'Hoàn Kiếm', addressLine: '17 Phố Trần Nguyên Hãn' }), HCM)
    assert.equal(fill.city, 'Thành phố Hà Nội')
    assert.equal(fill.district, 'Phường Hoàn Kiếm')
  })
  it('leaves an unmatched ward empty, keeps the province when unknown and the line when nothing was read', () => {
    const fill = fillFromGeocode(vnAdminUnits, read({ city: 'Nowhere', district: 'Không Có' }), HCM)
    assert.deepEqual(fill, { addressLine: null, city: HCM, district: '' })
    assert.equal(fillFromGeocode(vnAdminUnits, read({ formattedAddress: 'Sài Gòn, Việt Nam' }), HCM).addressLine, 'Sài Gòn, Việt Nam')
  })
})

describe('map helpers', () => {
  it('placeText shows the full address, else its parts', () => {
    assert.equal(placeText(read({ formattedAddress: 'A, B, C' })), 'A, B, C')
    assert.equal(placeText(read({ addressLine: 'Điện Biên Phủ', district: 'Thạnh Mỹ Tây' })), `Điện Biên Phủ, Thạnh Mỹ Tây, ${HCM}`)
  })
  it('latestRequest keeps only the last click current', () => {
    const latest = latestRequest()
    const first = latest.next()
    const second = latest.next()
    assert.equal(latest.isLatest(first), false)
    assert.equal(latest.isLatest(second), true)
  })
  it('roundCoordinate keeps six decimals', () => {
    assert.equal(roundCoordinate(10.77241234567), 10.772412)
  })
})
