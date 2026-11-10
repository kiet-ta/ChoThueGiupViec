import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  EMPTY_FILTERS,
  ENTRY_TYPE_LABELS,
  ENTRY_TYPE_OPTIONS,
  entryRow,
  entryTypeLabel,
  exportErrorMessage,
  exportFallbackName,
  exportHint,
  filterErrors,
  loadErrorText,
  partyLabel,
  reconciliationRows,
  referenceLabel,
  sameFilters,
  shortHash,
  summaryCards,
  toQuery,
  verifyMessage,
} from '../src/features/ledger/view.ts'
import type { LedgerEntry, LedgerFilters } from '../src/features/ledger/types.ts'

const entry = (over: Partial<LedgerEntry> = {}): LedgerEntry => ({
  entryId: 12,
  entryCode: 'LG00000012',
  createdAt: '2026-11-05T03:30:00Z',
  entryType: 'JOB_EARNING',
  fromPartyType: 'PLATFORM_CLEARING',
  fromPartyId: null,
  toPartyType: 'WORKER',
  toPartyId: 7,
  amount: 208000,
  refType: 'ASSIGNMENT',
  refId: 10,
  orderId: 41,
  note: null,
  entryHash: 'abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789',
  ...over,
})

const filters = (over: Partial<LedgerFilters> = {}): LedgerFilters => ({ ...EMPTY_FILTERS, ...over })

describe('labels', () => {
  it('names every entry type of the contract in Vietnamese and keeps an unknown one as it is', () => {
    assert.equal(Object.keys(ENTRY_TYPE_LABELS).length, 15)
    assert.equal(entryTypeLabel('WITHDRAWAL'), 'Rút tiền (ứng lương)')
    assert.equal(entryTypeLabel('SETTLEMENT'), 'Tất toán lương tháng')
    assert.equal(entryTypeLabel('SOMETHING_NEW'), 'SOMETHING_NEW')
    assert.equal(ENTRY_TYPE_OPTIONS[0].value, '')
    assert.equal(ENTRY_TYPE_OPTIONS.length, 16)
  })

  it('shows a party with its id and the platform accounts without one', () => {
    assert.equal(partyLabel('WORKER', 7), 'Thợ #7')
    assert.equal(partyLabel('AGENCY_ESCROW', 3), 'Ký quỹ Agency #3')
    assert.equal(partyLabel('PLATFORM_REVENUE', null), 'Doanh thu nền tảng')
    assert.equal(partyLabel('EXTERNAL', null), 'Bên ngoài')
    assert.equal(partyLabel('EXTERNAL', 12), 'Bên ngoài #12')
  })

  it('adds the order to a reference unless the reference is that order', () => {
    assert.equal(referenceLabel('ASSIGNMENT', 10, 41), 'Ca #10 · đơn #41')
    assert.equal(referenceLabel('ORDER', 41, 41), 'Đơn #41')
    assert.equal(referenceLabel('WITHDRAWAL', 1, null), 'Lệnh rút #1')
    assert.equal(referenceLabel('EXTENSION', 5, 41), 'Làm thêm #5 · đơn #41')
  })

  it('shortens a hash to eight characters', () => {
    assert.equal(shortHash('abcdef0123456789'), 'abcdef01…')
    assert.equal(shortHash('abc'), 'abc')
  })
})

describe('rows', () => {
  it('turns an entry into the cells of the table in Vietnam time', () => {
    const row = entryRow(entry())
    assert.deepEqual(
      [row.code, row.time, row.from, row.to, row.type, row.amount, row.reference, row.hash],
      ['LG00000012', '05/11/2026 10:30', 'Tiền giữ hộ khách', 'Thợ #7', 'Tiền công ca', '208.000 đ', 'Ca #10 · đơn #41', 'abcdef01…'],
    )
    assert.equal(row.fullHash.length, 64)
  })
})

describe('summary', () => {
  it('has the five cards with money formatted and a dash when there is no summary', () => {
    const cards = summaryCards({ revenue: 72000, clearing: 0, workerDebt: 138000, agencyDebt: 221000, held: 100000 })
    assert.deepEqual(cards.map((c) => c.title), ['Doanh thu nền tảng', 'Tiền đang giữ hộ khách', 'Nợ thợ tự do', 'Nợ Agency', 'Đang chờ rút'])
    assert.deepEqual(cards.map((c) => c.value), ['72.000 đ', '0 đ', '138.000 đ', '221.000 đ', '100.000 đ'])
    assert.ok(summaryCards(null).every((c) => c.value === '–'))
  })
})

describe('chain check', () => {
  it('says the ledger is intact with the number of entries checked', () => {
    assert.deepEqual(verifyMessage({ ok: true, checkedCount: 9, firstBrokenEntryCode: null }), { tone: 'success', text: 'Sổ nguyên vẹn: đã kiểm 9 bút toán.' })
  })

  it('names the first broken entry', () => {
    assert.deepEqual(verifyMessage({ ok: false, checkedCount: 2, firstBrokenEntryCode: 'LG00000002' }), {
      tone: 'danger',
      text: 'Phát hiện sai lệch tại bút toán LG00000002.',
    })
    assert.equal(verifyMessage({ ok: false, checkedCount: 2, firstBrokenEntryCode: null }).tone, 'danger')
  })
})

describe('reconciliation', () => {
  it('has no rows when ledger and sources agree', () => {
    assert.deepEqual(reconciliationRows({ items: [] }), [])
    assert.deepEqual(reconciliationRows(null), [])
  })

  it('shows the type, the missing rows and amount and the excess', () => {
    const rows = reconciliationRows({
      items: [
        { entryType: 'COMMISSION', missingCount: 2, missingAmount: 72000, excessAmount: 0 },
        { entryType: 'REFUND', missingCount: 0, missingAmount: 0, excessAmount: 50000 },
      ],
    })
    assert.deepEqual(rows[0], { type: 'Phí nền tảng', missingCount: 2, missing: '72.000 đ', excess: '—' })
    assert.deepEqual(rows[1], { type: 'Hoàn tiền khách', missingCount: 0, missing: '—', excess: '50.000 đ' })
  })
})

describe('filters', () => {
  it('accepts empty filters, good days and a numeric id with its type', () => {
    assert.deepEqual(filterErrors(EMPTY_FILTERS), {})
    assert.deepEqual(filterErrors(filters({ from: '2026-11-01', to: '2026-11-30', partyType: 'WORKER', partyId: '7' })), {})
    assert.deepEqual(filterErrors(filters({ from: '2026-11-30', to: '2026-11-30' })), {})
  })

  it('rejects an invalid day, a reversed range and an id without a type', () => {
    assert.ok(filterErrors(filters({ from: '2026-13-01' })).from)
    assert.ok(filterErrors(filters({ from: '2026-02-30' })).from)
    assert.ok(filterErrors(filters({ to: '01/11/2026' })).to)
    assert.ok(filterErrors(filters({ from: '2026-11-30', to: '2026-11-01' })).from)
    assert.ok(filterErrors(filters({ partyId: '7' })).partyType)
    assert.ok(filterErrors(filters({ partyType: 'WORKER', partyId: 'abc' })).partyId)
  })

  it('leaves empty values out of the query and sends the id only with its type', () => {
    assert.deepEqual(toQuery(EMPTY_FILTERS, 1, 20), {
      from: undefined, to: undefined, type: undefined, partyType: undefined, partyId: undefined, page: 1, pageSize: 20,
    })
    assert.deepEqual(toQuery(filters({ from: '2026-11-01', type: 'COMMISSION', partyType: 'WORKER', partyId: '7' }), 2, 20), {
      from: '2026-11-01', to: undefined, type: 'COMMISSION', partyType: 'WORKER', partyId: 7, page: 2, pageSize: 20,
    })
    assert.equal(toQuery(filters({ partyId: '7' })).partyId, undefined)
  })

  it('compares filters and names the statement', () => {
    assert.equal(sameFilters(EMPTY_FILTERS, filters()), true)
    assert.equal(sameFilters(EMPTY_FILTERS, filters({ type: 'REFUND' })), false)
    assert.equal(exportFallbackName(EMPTY_FILTERS), 'ledger-start_now.xlsx')
    assert.equal(exportFallbackName(filters({ from: '2026-11-01', to: '2026-11-30' })), 'ledger-2026-11-01_2026-11-30.xlsx')
  })

  it('tells when the statement will have balance rows', () => {
    assert.match(exportHint(filters({ partyType: 'WORKER', partyId: '7' })), /số dư đầu kỳ và cuối kỳ của bên/)
    assert.match(exportHint(filters({ partyType: 'PLATFORM_REVENUE' })), /số dư đầu kỳ và cuối kỳ của bên/)
    assert.match(exportHint(filters({ partyType: 'WORKER' })), /Chọn một bên cụ thể/)
    assert.match(exportHint(EMPTY_FILTERS), /Chọn một bên cụ thể/)
  })
})

describe('errors', () => {
  it('gives a line for the usual statuses', () => {
    assert.equal(loadErrorText(0), 'Không kết nối được máy chủ.')
    assert.match(loadErrorText(403), /không có quyền/)
    assert.match(loadErrorText(500), /lỗi 500/)
  })

  it('reads the first field message of a failed statement and falls back to a line for the status', () => {
    const body = JSON.stringify({ success: false, message: 'Validation failed', data: { errors: { from: ['The statement has more than 50000 entries; choose a shorter period.'] } } })
    assert.match(exportErrorMessage(400, body), /more than 50000/)
    assert.match(exportErrorMessage(400, 'not json'), /khoảng thời gian ngắn hơn/)
    assert.equal(exportErrorMessage(500, JSON.stringify({ message: 'Boom' })), 'Boom')
    assert.match(exportErrorMessage(403, ''), /không có quyền/)
    assert.match(exportErrorMessage(502, ''), /lỗi 502/)
  })
})
