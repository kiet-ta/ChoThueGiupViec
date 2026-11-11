import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { groupBySection } from '../src/app/nav-groups.ts'
import { activeSection, isItemActive, parseSaved, reveal, sectionId, serialize, toggle } from '../src/app/nav-tree.ts'

const item = (to: string, section?: string) => ({ label: to, to, section })
const groups = groupBySection([
  item('dashboard'),
  item('absence-reports', 'QUẢN LÝ NHÂN SỰ'),
  item('worker-controls', 'QUẢN LÝ NHÂN SỰ'),
  item('ledger', 'TÀI CHÍNH'),
  item('withdrawals', 'TÀI CHÍNH'),
])

describe('which page is active', () => {
  it('an item is active on its own page and on a page below it, not on a page that only starts with the same letters', () => {
    const ledger = item('ledger', 'TÀI CHÍNH')
    assert.equal(isItemActive('admin', ledger, '/admin/ledger'), true)
    assert.equal(isItemActive('admin', ledger, '/admin/ledger/42'), true)
    assert.equal(isItemActive('admin', ledger, '/admin/ledger-old'), false)
    assert.equal(isItemActive('partner', ledger, '/admin/ledger'), false)
  })

  it('finds the section of the current page; a page without a section has none', () => {
    assert.equal(activeSection('admin', groups, '/admin/withdrawals'), 'TÀI CHÍNH')
    assert.equal(activeSection('admin', groups, '/admin/worker-controls'), 'QUẢN LÝ NHÂN SỰ')
    assert.equal(activeSection('admin', groups, '/admin/dashboard'), undefined)
    assert.equal(activeSection('admin', groups, '/admin/nowhere'), undefined)
  })
})

describe('opening and folding', () => {
  it('every section starts folded and a click opens it, the next click folds it again', () => {
    const opened = toggle({}, 'TÀI CHÍNH')
    assert.deepEqual(opened, { 'TÀI CHÍNH': true })
    assert.deepEqual(toggle(opened, 'TÀI CHÍNH'), { 'TÀI CHÍNH': false })
  })

  it('opening one section keeps the others as the person left them', () => {
    assert.deepEqual(toggle({ A: true }, 'B'), { A: true, B: true })
  })

  it('does not change the state it was given', () => {
    const open = { A: true }
    toggle(open, 'A')
    reveal(open, 'B')
    assert.deepEqual(open, { A: true })
  })

  it('moving to a page opens its section and leaves the others alone; no section changes nothing', () => {
    assert.deepEqual(reveal({ A: true }, 'B'), { A: true, B: true })
    const open = { A: true }
    assert.equal(reveal(open, 'A'), open)
    assert.equal(reveal(open, undefined), open)
  })
})

describe('the saved state', () => {
  it('keeps only the open sections and reads them back', () => {
    const text = serialize({ A: true, B: false, C: true })
    assert.deepEqual(parseSaved(text), { A: true, C: true })
  })

  it('anything unreadable gives an empty state, so the sidebar still works', () => {
    for (const raw of [null, '', 'not json', '[]', '"x"', '42', 'null', '{"A":"yes","B":1}']) {
      assert.deepEqual(parseSaved(raw), {}, String(raw))
    }
  })
})

describe('ids for aria-controls', () => {
  it('are plain, unique per area and section', () => {
    assert.equal(sectionId('admin', 0), 'nav-admin-section-0')
    assert.notEqual(sectionId('admin', 1), sectionId('partner', 1))
    assert.match(sectionId('admin', 3), /^[a-z0-9-]+$/)
  })
})
