import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { NAV_LINKS, ROOMS, PLANS } from '../src/features/landing/data.ts'

describe('landing layout and navigation (ticket #633)', () => {
  it('NAV_LINKS contains the interactive shift schedule link #lich-ca', () => {
    const shiftLink = NAV_LINKS.find((l) => l.href === '#lich-ca')
    assert.ok(shiftLink, 'Expected #lich-ca link in NAV_LINKS')
    assert.equal(shiftLink?.label, 'Lịch ca')
  })

  it('NAV_LINKS contains essential navigation anchors', () => {
    const hrefs = NAV_LINKS.map((l) => l.href)
    assert.ok(hrefs.includes('#tinh-gia'))
    assert.ok(hrefs.includes('#lich-ca'))
    assert.ok(hrefs.includes('#cong-viec'))
    assert.ok(hrefs.includes('#an-tam'))
    assert.ok(hrefs.includes('#bang-gia'))
  })

  it('PLANS covers all three residential tiers', () => {
    const keys = PLANS.map((p) => p.key)
    assert.deepEqual(keys, ['studio', 'apartment', 'townhouse'])
  })

  it('ROOMS contains all 4 standard inspection rooms', () => {
    const names = ROOMS.map((r) => r.name)
    assert.deepEqual(names, ['Phòng khách', 'Nhà bếp', 'Phòng tắm', 'Phòng ngủ'])
  })
})
