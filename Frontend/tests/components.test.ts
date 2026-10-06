import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { pageItems, pageRange, totalPages } from '../src/components/data-table/paging.ts'
import { toCsv } from '../src/components/data-table/csv.ts'
import { pairPhotos, type ComparePhoto } from '../src/components/photo-compare/pair-photos.ts'

describe('pagination', () => {
  it('totalPages is at least 1', () => {
    assert.equal(totalPages(0, 20), 1)
    assert.equal(totalPages(46, 20), 3)
    assert.equal(totalPages(40, 20), 2)
  })

  it('pageRange gives the 1-based inclusive range, "1-3 of 46" style', () => {
    assert.deepEqual(pageRange(1, 3, 46), { from: 1, to: 3 })
    assert.deepEqual(pageRange(16, 3, 46), { from: 46, to: 46 })
    assert.deepEqual(pageRange(1, 20, 0), { from: 0, to: 0 })
    assert.deepEqual(pageRange(9, 20, 46), { from: 0, to: 0 })
  })

  it('pageItems lists every page when few, ellipses when many', () => {
    assert.deepEqual(pageItems(1, 3), [1, 2, 3])
    assert.deepEqual(pageItems(1, 20), [1, 2, 3, 4, 5, 'ellipsis', 20])
    assert.deepEqual(pageItems(10, 20), [1, 'ellipsis', 9, 10, 11, 'ellipsis', 20])
    assert.deepEqual(pageItems(20, 20), [1, 'ellipsis', 16, 17, 18, 19, 20])
    for (const p of [1, 5, 10, 19, 20]) {
      const items = pageItems(p, 20)
      assert.equal(items.length, 7, `page ${p}`)
      assert.ok(items.includes(p), `page ${p} is shown`)
    }
  })
})

describe('toCsv', () => {
  const cols = [
    { header: 'Mã', value: (r: { id: string; note: string | null; n: number }) => r.id },
    { header: 'Ghi chú', value: (r: { id: string; note: string | null; n: number }) => r.note },
    { header: 'Số', value: (r: { id: string; note: string | null; n: number }) => r.n },
  ]

  it('starts with a BOM, uses CRLF and escapes commas, quotes and newlines', () => {
    const csv = toCsv(cols, [
      { id: '#TC-1', note: 'a,b', n: 1 },
      { id: '#TC-2', note: 'he said "hi"\nbye', n: 2.5 },
      { id: '#TC-3', note: null, n: 0 },
    ])
    assert.equal(csv.charCodeAt(0), 0xfeff)
    assert.equal(
      csv.slice(1),
      'Mã,Ghi chú,Số\r\n#TC-1,"a,b",1\r\n#TC-2,"he said ""hi""\nbye",2.5\r\n#TC-3,,0\r\n',
    )
  })

  it('neutralises spreadsheet formulas in text but keeps negative numbers', () => {
    const csv = toCsv(cols, [{ id: '=HYPERLINK("http://x")', note: '@cmd', n: -5 }])
    assert.ok(csv.includes(`"'=HYPERLINK(""http://x"")"`))
    assert.ok(csv.includes(`'@cmd`))
    assert.ok(csv.endsWith(',-5\r\n'))
  })
})

describe('pairPhotos', () => {
  const p = (angleNo: number, url: string, isAccepted?: boolean): ComparePhoto => ({ angleNo, url, isAccepted })

  it('pairs by angle and sorts unsorted input', () => {
    const pairs = pairPhotos([p(3, 'b3'), p(1, 'b1'), p(2, 'b2')], [p(2, 'a2'), p(3, 'a3'), p(1, 'a1')])
    assert.deepEqual(
      pairs.map((x) => [x.angleNo, x.before?.url, x.after?.url]),
      [[1, 'b1', 'a1'], [2, 'b2', 'a2'], [3, 'b3', 'a3']],
    )
  })

  it('keeps null for a missing side and for an extra angle on one side', () => {
    const pairs = pairPhotos([p(1, 'b1'), p(2, 'b2')], [p(1, 'a1'), p(4, 'a4')])
    assert.deepEqual(
      pairs.map((x) => [x.angleNo, x.before?.url ?? null, x.after?.url ?? null]),
      [[1, 'b1', 'a1'], [2, 'b2', null], [4, null, 'a4']],
    )
  })

  it('prefers the accepted photo over a rejected retake and the latest among equals', () => {
    const pairs = pairPhotos(
      [p(1, 'blur', false), p(1, 'sharp', true), p(1, 'blur2', false)],
      [p(1, 'old', true), p(1, 'new', true)],
    )
    assert.equal(pairs[0].before?.url, 'sharp')
    assert.equal(pairs[0].after?.url, 'new')
  })

  it('returns an empty list when there are no photos', () => {
    assert.deepEqual(pairPhotos([], []), [])
  })
})
