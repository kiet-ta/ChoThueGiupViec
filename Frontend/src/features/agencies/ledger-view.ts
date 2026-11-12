// Pure logic of the agency's transaction history (no React, so `node --test` covers it). Rules: .spec/contracts/ledger.md sections 5 and 9.
import { formatDateTime, formatVnd } from '../../lib/format.ts'
import { entryPartyLabel, entryTypeLabel, referenceLabel, shortHash } from '../ledger/view.ts'
import type { LedgerEntry } from '../ledger/types.ts'

/** A row of the agency's history: the entry plus the account of the agency it touched and the signed amount (`GET /agencies/me/ledger`). */
export interface AgencyLedgerEntry extends LedgerEntry {
  /** AGENCY (the wallet of the agency's earnings) or AGENCY_ESCROW (the escrow account). */
  account: string | null
  /** Positive when the money came in to `account`, negative when it went out. */
  signedAmount: number | null
}

/** The history with the balances of both accounts around the period (the filters limit the rows, never the balances). */
export interface AgencyLedgerPage {
  items: AgencyLedgerEntry[]
  page: number
  pageSize: number
  total: number
  openingBalance: number
  closingBalance: number
  escrowOpeningBalance: number | null
  escrowClosingBalance: number | null
}

export type Wallet = 'ALL' | 'WALLET' | 'ESCROW'

export const WALLET_OPTIONS: { value: Wallet; label: string }[] = [
  { value: 'ALL', label: 'Tất cả' },
  { value: 'WALLET', label: 'Tiền công' },
  { value: 'ESCROW', label: 'Ký quỹ' },
]

const ACCOUNT_LABELS: Record<string, string> = { AGENCY: 'Tiền công', AGENCY_ESCROW: 'Ký quỹ' }

export function accountLabel(account: string | null): string {
  return account ? (ACCOUNT_LABELS[account] ?? account) : '—'
}

/** The rows of the page that belong to the chosen account; the wallet filter works on the rows already loaded. */
export function visibleEntries(items: AgencyLedgerEntry[], wallet: Wallet): AgencyLedgerEntry[] {
  if (wallet === 'ALL') return items
  const account = wallet === 'WALLET' ? 'AGENCY' : 'AGENCY_ESCROW'
  return items.filter((e) => e.account === account)
}

/** `+221.000 đ` for money coming in, `-300.000 đ` for money going out; a plain amount when the side is unknown. */
export function signedAmountText(signed: number | null, amount: number): string {
  if (signed === null) return formatVnd(amount)
  return `${signed > 0 ? '+' : ''}${formatVnd(signed)}`
}

export interface HistoryRow {
  entryId: number
  code: string
  time: string
  account: string
  type: string
  /** The other side of the movement, from the agency's point of view. */
  counterparty: string
  amount: string
  tone: 'in' | 'out' | 'plain'
  reference: string
  /** The monthly settlement is highlighted: it is the line that closes a month. */
  settlement: boolean
  hash: string
  fullHash: string
}

export function historyRow(e: AgencyLedgerEntry): HistoryRow {
  const received = e.signedAmount !== null && e.signedAmount > 0
  const other = received ? entryPartyLabel(e, 'from', 'agency') : entryPartyLabel(e, 'to', 'agency')
  return {
    entryId: e.entryId,
    code: e.entryCode,
    time: formatDateTime(e.createdAt),
    account: accountLabel(e.account),
    type: entryTypeLabel(e.entryType),
    counterparty: e.signedAmount === null ? `${entryPartyLabel(e, 'from', 'agency')} → ${entryPartyLabel(e, 'to', 'agency')}` : other,
    amount: signedAmountText(e.signedAmount, e.amount),
    tone: e.signedAmount === null ? 'plain' : e.signedAmount > 0 ? 'in' : 'out',
    reference: referenceLabel(e.refType, e.refId, e.orderId),
    settlement: e.entryType === 'SETTLEMENT',
    hash: shortHash(e.entryHash),
    fullHash: e.entryHash,
  }
}

export interface BalanceBlock {
  key: string
  title: string
  opening: string
  closing: string
  change: string
}

/** The two balance blocks of the page; the escrow block only exists when the server sent its balances. A missing page shows dashes. */
export function balanceBlocks(page: AgencyLedgerPage | null): BalanceBlock[] {
  const block = (key: string, title: string, opening: number | null | undefined, closing: number | null | undefined): BalanceBlock => ({
    key,
    title,
    opening: opening === null || opening === undefined ? '–' : formatVnd(opening),
    closing: closing === null || closing === undefined ? '–' : formatVnd(closing),
    change: opening === null || opening === undefined || closing === null || closing === undefined ? '–' : signedAmountText(closing - opening, closing - opening),
  })

  const blocks = [block('wallet', 'Ví tiền công', page?.openingBalance, page?.closingBalance)]
  if (!page || (page.escrowOpeningBalance !== null && page.escrowClosingBalance !== null)) {
    blocks.push(block('escrow', 'Ký quỹ', page?.escrowOpeningBalance, page?.escrowClosingBalance))
  }

  return blocks
}

/** What the page says under the wallet filter: it narrows the rows of the page being viewed, not the whole history. */
export function walletNote(wallet: Wallet): string | null {
  return wallet === 'ALL' ? null : 'Bộ lọc ví áp dụng cho các dòng của trang đang xem; số dư đầu kỳ và cuối kỳ luôn tính trên toàn bộ sổ.'
}
