import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { ariaSort, nextSort, sortHint, sortParams, type SortState } from '../src/components/data-table/sort.ts'
import { LEDGER_DEFAULT_SORT, LEDGER_SORT, WITHDRAWAL_SORT, toQuery, withdrawalQuery, EMPTY_FILTERS } from '../src/features/ledger/view.ts'
import { BATCH_DEFAULT_SORT, BATCH_SORT, batchQuery } from '../src/features/payouts/view.ts'

describe('sorting a table by its header', () => {
  it('a new column starts ascending and a second click on it goes descending, then ascending again', () => {
    const first = nextSort(null, 'amount')
    assert.deepEqual(first, { key: 'amount', dir: 'asc' })
    const second = nextSort(first, 'amount')
    assert.deepEqual(second, { key: 'amount', dir: 'desc' })
    assert.deepEqual(nextSort(second, 'amount'), { key: 'amount', dir: 'asc' })
  })

  it('clicking another column starts that one ascending, whatever the direction was', () => {
    const desc: SortState = { key: 'amount', dir: 'desc' }
    assert.deepEqual(nextSort(desc, 'createdAt'), { key: 'createdAt', dir: 'asc' })
  })

  it('does not change the state it was given', () => {
    const current: SortState = { key: 'amount', dir: 'asc' }
    nextSort(current, 'amount')
    assert.deepEqual(current, { key: 'amount', dir: 'asc' })
  })

  it('sends no parameter at all when there is no sort, so the server keeps its own order', () => {
    assert.deepEqual(sortParams(null), {})
    assert.deepEqual(sortParams({ key: 'createdAt', dir: 'desc' }), { sort: 'createdAt', dir: 'desc' })
  })

  it('tells the screen reader which column is sorted and how', () => {
    const sort: SortState = { key: 'amount', dir: 'desc' }
    assert.equal(ariaSort(sort, 'amount'), 'descending')
    assert.equal(ariaSort({ key: 'amount', dir: 'asc' }, 'amount'), 'ascending')
    assert.equal(ariaSort(sort, 'createdAt'), 'none')
    assert.equal(ariaSort(null, 'amount'), 'none')
  })

  it('the hint names the direction the next click asks for', () => {
    assert.equal(sortHint(null, 'amount', 'số tiền'), 'Sắp xếp theo số tiền tăng dần')
    assert.equal(sortHint({ key: 'amount', dir: 'asc' }, 'amount', 'số tiền'), 'Sắp xếp theo số tiền giảm dần')
    assert.equal(sortHint({ key: 'amount', dir: 'desc' }, 'amount', 'số tiền'), 'Sắp xếp theo số tiền tăng dần')
    assert.equal(sortHint({ key: 'amount', dir: 'desc' }, 'createdAt', 'thời gian'), 'Sắp xếp theo thời gian tăng dần')
  })
})

describe('the sort names of the three Admin tables are the ones of the contract', () => {
  it('ledger: entryCode, createdAt, amount; the table opens newest first', () => {
    assert.deepEqual({ ...LEDGER_SORT }, { code: 'entryCode', time: 'createdAt', amount: 'amount' })
    assert.deepEqual(LEDGER_DEFAULT_SORT, { key: 'createdAt', dir: 'desc' })
    // one click on the time flips the default to oldest first
    assert.deepEqual(nextSort(LEDGER_DEFAULT_SORT, LEDGER_SORT.time), { key: 'createdAt', dir: 'asc' })
  })

  it('withdrawals: worker, amount, bank, requestedAt, status', () => {
    assert.deepEqual({ ...WITHDRAWAL_SORT }, { worker: 'worker', amount: 'amount', bank: 'bank', sent: 'requestedAt', status: 'status' })
  })

  it('payout batches: period, status, items, total, closedAt; the table opens newest month first', () => {
    assert.deepEqual({ ...BATCH_SORT }, { period: 'period', status: 'status', items: 'items', total: 'total', closed: 'closedAt' })
    assert.deepEqual(BATCH_DEFAULT_SORT, { key: 'period', dir: 'desc' })
  })
})

describe('the query of each table carries the sort', () => {
  it('ledger: the filters, the page and the order; no order parameters without a sort (the exports and the agency page do not send one)', () => {
    assert.deepEqual(toQuery(EMPTY_FILTERS, 2, 20, { key: 'amount', dir: 'asc' }), {
      from: undefined,
      to: undefined,
      type: undefined,
      partyType: undefined,
      partyId: undefined,
      page: 2,
      pageSize: 20,
      sort: 'amount',
      dir: 'asc',
    })
    const without = toQuery(EMPTY_FILTERS, 1, 20)
    assert.equal('sort' in without, false)
    assert.equal('dir' in without, false)
  })

  it('withdrawals: the tab and the page, and the order once a header was clicked', () => {
    assert.deepEqual(withdrawalQuery('PENDING', 1, 20, null), { status: 'PENDING', page: 1, pageSize: 20 })
    assert.deepEqual(withdrawalQuery('PAID', 3, 20, { key: 'worker', dir: 'desc' }), { status: 'PAID', page: 3, pageSize: 20, sort: 'worker', dir: 'desc' })
  })

  it('payout batches: the page and the order', () => {
    assert.deepEqual(batchQuery(1, 20, null), { page: 1, pageSize: 20 })
    assert.deepEqual(batchQuery(1, 20, BATCH_DEFAULT_SORT), { page: 1, pageSize: 20, sort: 'period', dir: 'desc' })
  })
})
