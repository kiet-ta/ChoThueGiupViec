// Hand-written from the approved contract: .spec/contracts/admin.md 2.6 (LEDGER-28).

export type BankChangeStatus = 'PENDING' | 'APPROVED' | 'REJECTED'

/** A worker's request to change the payout account as the Admin sees it (`AdminBankChangeDto`). */
export interface BankChangeRequest {
  requestId: number
  workerId: number
  workerName: string
  workerPhone: string
  /** The account the worker had when the request was sent. */
  previousBankName: string | null
  previousBankAccountNo: string | null
  bankName: string
  bankAccountNo: string
  status: BankChangeStatus
  /** ISO-8601 UTC. */
  requestedAt: string
  reviewedAt: string | null
  rejectReason: string | null
}

export interface BankChangePage {
  items: BankChangeRequest[]
  page: number
  pageSize: number
  total: number
}
