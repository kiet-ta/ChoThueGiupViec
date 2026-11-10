// Hand-written from the approved contract until gate G4: .spec/contracts/ledger.md sections 3 and 9.

/** One ledger entry (`LedgerEntryDto`): append-only, chained to the previous one by `entryHash`. */
export interface LedgerEntry {
  entryId: number
  /** `LG00000012`. */
  entryCode: string
  /** ISO-8601 UTC. */
  createdAt: string
  /** ORDER_PAID, JOB_EARNING, COMMISSION, ... */
  entryType: string
  /** EXTERNAL, PLATFORM_CLEARING, PLATFORM_REVENUE, WORKER, AGENCY or AGENCY_ESCROW. */
  fromPartyType: string
  fromPartyId: number | null
  toPartyType: string
  toPartyId: number | null
  amount: number
  /** ORDER, EXTENSION, ASSIGNMENT, PAYMENT, DISPUTE, ESCROW_TXN, WITHDRAWAL, PAYOUT_ITEM or ENTRY. */
  refType: string
  refId: number
  orderId: number | null
  note: string | null
  entryHash: string
}

export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

/** What the platform holds and owes (net balances, VND). */
export interface LedgerSummary {
  revenue: number
  clearing: number
  workerDebt: number
  agencyDebt: number
  held: number
}

export interface ChainResult {
  ok: boolean
  checkedCount: number
  /** Set when `ok` is false: the first entry whose hash or link is wrong. */
  firstBrokenEntryCode: string | null
}

export interface ReconciliationItem {
  entryType: string
  missingCount: number
  missingAmount: number
  /** What the ledger holds above the source (refunds only); never written automatically. */
  excessAmount: number
}

export interface ReconciliationReport {
  items: ReconciliationItem[]
}

/** The filters of the list and of the statement; every value is the text of its input. */
export interface LedgerFilters {
  /** `yyyy-MM-dd`, a Vietnam day, included. */
  from: string
  to: string
  type: string
  partyType: string
  /** Digits only; needs a party type. */
  partyId: string
}
