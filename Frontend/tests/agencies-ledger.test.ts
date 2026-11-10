import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  WALLET_OPTIONS,
  accountLabel,
  balanceBlocks,
  historyRow,
  signedAmountText,
  visibleEntries,
  walletNote,
  type AgencyLedgerEntry,
  type AgencyLedgerPage,
} from '../src/features/agencies/ledger-view.ts'

const entry = (over: Partial<AgencyLedgerEntry> = {}): AgencyLedgerEntry => ({
  entryId: 5,
  entryCode: 'LG00000005',
  createdAt: '2026-11-05T03:30:00Z',
  entryType: 'JOB_EARNING',
  fromPartyType: 'PLATFORM_CLEARING',
  fromPartyId: null,
  toPartyType: 'AGENCY',
  toPartyId: 3,
  amount: 221000,
  refType: 'ASSIGNMENT',
  refId: 10,
  orderId: 41,
  note: null,
  entryHash: 'abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789',
  account: 'AGENCY',
  signedAmount: 221000,
  ...over,
})

const escrowPenalty = entry({
  entryId: 6,
  entryCode: 'LG00000006',
  entryType: 'ESCROW_PENALTY',
  fromPartyType: 'AGENCY_ESCROW',
  fromPartyId: 3,
  toPartyType: 'PLATFORM_REVENUE',
  toPartyId: null,
  amount: 300000,
  refType: 'ESCROW_TXN',
  refId: 6,
  orderId: null,
  account: 'AGENCY_ESCROW',
  signedAmount: -300000,
})

const page = (over: Partial<AgencyLedgerPage> = {}): AgencyLedgerPage => ({
  items: [entry(), escrowPenalty],
  page: 1,
  pageSize: 20,
  total: 2,
  openingBalance: 221000,
  closingBalance: 321000,
  escrowOpeningBalance: 0,
  escrowClosingBalance: 1700000,
  ...over,
})

describe('wallet filter', () => {
  it('offers all, earnings and escrow', () => {
    assert.deepEqual(WALLET_OPTIONS.map((o) => o.label), ['Tất cả', 'Tiền công', 'Ký quỹ'])
    assert.equal(accountLabel('AGENCY'), 'Tiền công')
    assert.equal(accountLabel('AGENCY_ESCROW'), 'Ký quỹ')
    assert.equal(accountLabel(null), '—')
  })

  it('keeps the rows of the chosen account and everything for all', () => {
    const items = page().items
    assert.equal(visibleEntries(items, 'ALL').length, 2)
    assert.deepEqual(visibleEntries(items, 'WALLET').map((e) => e.entryId), [5])
    assert.deepEqual(visibleEntries(items, 'ESCROW').map((e) => e.entryId), [6])
  })

  it('says the filter works on the page being viewed, not for all', () => {
    assert.equal(walletNote('ALL'), null)
    assert.match(walletNote('ESCROW') ?? '', /trang đang xem/)
  })
})

describe('amounts', () => {
  it('signs money coming in and going out and shows a plain amount when the side is unknown', () => {
    assert.equal(signedAmountText(221000, 221000), '+221.000 đ')
    assert.equal(signedAmountText(-300000, 300000), '-300.000 đ')
    assert.equal(signedAmountText(null, 5000), '5.000 đ')
  })
})

describe('rows', () => {
  it('maps an incoming earning with the other side, the order and the local time', () => {
    const row = historyRow(entry())
    assert.deepEqual(
      [row.time, row.code, row.account, row.type, row.counterparty, row.amount, row.tone, row.reference, row.settlement, row.hash],
      ['05/11/2026 10:30', 'LG00000005', 'Tiền công', 'Tiền công ca', 'Tiền giữ hộ khách', '+221.000 đ', 'in', 'Ca #10 · đơn #41', false, 'abcdef01…'],
    )
  })

  it('maps an outgoing escrow movement towards the platform', () => {
    const row = historyRow(escrowPenalty)
    assert.deepEqual([row.account, row.type, row.counterparty, row.amount, row.tone, row.reference], ['Ký quỹ', 'Trừ ký quỹ', 'Doanh thu nền tảng', '-300.000 đ', 'out', 'Ký quỹ #6'])
  })

  it('highlights the monthly settlement', () => {
    const settlement = historyRow(
      entry({ entryType: 'SETTLEMENT', fromPartyType: 'AGENCY', fromPartyId: 3, toPartyType: 'EXTERNAL', toPartyId: null, refType: 'PAYOUT_ITEM', refId: 2, orderId: null, signedAmount: -221000 }),
    )
    assert.equal(settlement.settlement, true)
    assert.equal(settlement.type, 'Tất toán lương tháng')
    assert.equal(settlement.amount, '-221.000 đ')
    assert.equal(settlement.counterparty, 'Bên ngoài')
    assert.equal(settlement.reference, 'Kỳ payout #2')
  })

  it('shows both sides when the page carries no viewpoint', () => {
    const row = historyRow(entry({ signedAmount: null, account: null }))
    assert.deepEqual([row.account, row.tone, row.amount, row.counterparty], ['—', 'plain', '221.000 đ', 'Tiền giữ hộ khách → Agency #3'])
  })
})

describe('balances', () => {
  it('has a wallet block and an escrow block with the opening, the closing and the change', () => {
    const blocks = balanceBlocks(page())
    assert.deepEqual(blocks.map((b) => [b.title, b.opening, b.closing, b.change]), [
      ['Ví tiền công', '221.000 đ', '321.000 đ', '+100.000 đ'],
      ['Ký quỹ', '0 đ', '1.700.000 đ', '+1.700.000 đ'],
    ])
  })

  it('shows a negative change and leaves the escrow block out when the server sent no escrow balances', () => {
    const blocks = balanceBlocks(page({ openingBalance: 500, closingBalance: 200, escrowOpeningBalance: null, escrowClosingBalance: null }))
    assert.equal(blocks.length, 1)
    assert.equal(blocks[0].change, '-300 đ')
  })

  it('shows dashes while there is no page, never a made-up zero', () => {
    const blocks = balanceBlocks(null)
    assert.equal(blocks.length, 2)
    assert.ok(blocks.every((b) => b.opening === '–' && b.closing === '–' && b.change === '–'))
  })
})
