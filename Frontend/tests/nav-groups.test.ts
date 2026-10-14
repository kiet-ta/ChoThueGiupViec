import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { groupBySection } from '../src/app/nav-groups.ts'

const item = (to: string, section?: string) => ({ label: to, to, section })

describe('groupBySection', () => {
  it('merges the items of one section even when they are not next to each other', () => {
    const groups = groupBySection([item('a', 'CẤU HÌNH'), item('b', 'XỬ LÝ'), item('c', 'CẤU HÌNH'), item('d', 'XỬ LÝ')])

    assert.deepEqual(groups.map((g) => g.section), ['CẤU HÌNH', 'XỬ LÝ'])
    assert.deepEqual(groups[0].items.map((i) => i.to), ['a', 'c'])
    assert.deepEqual(groups[1].items.map((i) => i.to), ['b', 'd'])
  })

  it('keeps the items without a section in one group and gives every group a unique key', () => {
    const groups = groupBySection([item('home'), item('a', 'S'), item('other')])

    assert.deepEqual(groups.map((g) => g.section), [undefined, 'S'])
    assert.deepEqual(groups[0].items.map((i) => i.to), ['home', 'other'])
    assert.equal(new Set(groups.map((g) => g.section ?? 'root')).size, groups.length)
  })
})
