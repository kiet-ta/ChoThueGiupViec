// Hand-written from the approved contract until gate G4: .spec/contracts/payouts.md sections 1 and 2.1-2.4.

export type BatchStatus = 'DRAFT' | 'CLOSED'

export interface PayoutBatch {
  batchId: number
  /** `YYYY-MM`. */
  periodMonth: string
  batchStatus: BatchStatus
  totalAmount: number
  itemCount: number
  confirmedBy: number | null
  confirmedAt: string | null
  createdAt: string
}

export interface PayoutItem {
  itemId: number
  /** FREELANCER or AGENCY. */
  payeeType: string
  workerId: number | null
  agencyId: number | null
  payeeName: string
  jobCount: number
  grossAmount: number
  commissionAmount: number
  penaltyAmount: number
  netAmount: number
  bankName: string | null
  /** Empty when the payee has no bank account (question P4). */
  bankAccountNo: string
  /** PENDING or TRANSFERRED. */
  itemStatus: string
}

export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

export interface BatchDetail {
  batch: PayoutBatch
  items: PayoutItem[]
  page: number
  pageSize: number
  total: number
  /** One line per payee without a bank account. */
  warnings: string[]
}

export type ExportType = 'freelancer' | 'agency-summary' | 'agency-detail'
