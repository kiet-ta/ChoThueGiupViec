import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { bannerFor, groupOf, NAV_GROUPS, statusFromSearch } from '../src/features/customer/shell/nav.ts'

describe('customer navbar dropdowns (WEB-CUST-UI-01)', () => {
  it('has the four menus with direct links', () => {
    assert.deepEqual(NAV_GROUPS.map((g) => g.label), ['Trang chủ', 'Đơn của tôi', 'Thợ yêu thích', 'Tài khoản'])
    const orders = NAV_GROUPS.find((g) => g.key === 'orders')!
    assert.deepEqual(orders.items.map((i) => i.to), [
      '/app/orders', '/app/orders?status=PENDING_PAYMENT', '/app/orders?status=DISPATCHING', '/app/orders?status=ASSIGNED',
      '/app/orders?status=COMPLETED', '/app/disputes', '/app/disputes/new',
    ])
    for (const g of NAV_GROUPS) for (const i of g.items) {
      assert.ok(i.to.startsWith('/app'), i.label)
      assert.ok(i.hint.length > 0, i.label)
    }
  })
  it('marks the menu of each page', () => {
    assert.equal(groupOf('/app'), 'home')
    assert.equal(groupOf('/app/booking'), 'home')
    assert.equal(groupOf('/app/orders/12'), 'orders')
    assert.equal(groupOf('/app/disputes/3'), 'orders')
    assert.equal(groupOf('/app/payments/9'), 'orders')
    assert.equal(groupOf('/app/favorites'), 'favorites')
    assert.equal(groupOf('/app/addresses/new'), 'account')
    assert.equal(groupOf('/app/profile'), 'account')
  })
  it('gives each part of the site its photo', () => {
    assert.equal(bannerFor('/app/orders'), '/customer/broom-kitchen.jpg')
    assert.equal(bannerFor('/app'), '/customer/mop-living-room.jpg')
  })
})

describe('orders filter in the URL', () => {
  it('reads a known status and ignores anything else', () => {
    assert.equal(statusFromSearch('?status=ASSIGNED'), 'ASSIGNED')
    assert.equal(statusFromSearch(new URLSearchParams('status=CANCELLED')), 'CANCELLED')
    assert.equal(statusFromSearch(''), null)
    assert.equal(statusFromSearch('?status=DROP_TABLE'), null)
  })
})
