// Hand-written from the approved contract until gate G4: .spec/contracts/disputes.md sections 1, 2.2 and 2.3.

export type DisputeStatus = 'OPEN' | 'IN_REVIEW' | 'RESOLVED' | 'DISMISSED'

export interface DisputeWorker {
  workerId: number
  fullName: string
  /** FREELANCER or AGENCY_STAFF. */
  workerType: string
  agencyName: string | null
}

/** A row of the Admin queue (`DisputeSummary`). */
export interface DisputeSummary {
  disputeId: number
  orderId: number
  orderCode: string
  customerName: string
  workers: DisputeWorker[]
  raisedBy: string
  category: string
  /** HIGH, MEDIUM or LOW, derived from the time left. */
  priority: string
  slaDueAt: string
  /** Negative when overdue; 0 once decided. */
  slaSecondsRemaining: number
  disputeStatus: DisputeStatus
  /** True for the customer dispute of an absence fee (ABSENT_FEE). */
  autoCancelled: boolean
}

export interface AdminDispute {
  disputeId: number
  orderId: number
  raisedBy: string
  category: string
  description: string
  evidenceUrls: string[]
  disputeStatus: DisputeStatus
  /** FREELANCER, AGENCY or CUSTOMER; null while open and after a dismissal. */
  faultParty: string | null
  compensationAmount: number | null
  slaDueAt: string
  resolvedAt: string | null
  createdAt: string
  resolvedBy: number | null
}

export interface TimelineEntry {
  at: string
  /** CHECK_IN, PHOTO_AFTER, CUSTOMER_DISPUTED or CHECK_OUT. */
  type: string
  detail: string
  volScore?: number | null
  gpsVerified?: boolean | null
  distanceM?: number | null
}

export interface CasePhoto {
  assignmentId: number
  /** BEFORE or AFTER. */
  phase: string
  angleNo: number
  url: string
  volScore: number
  isAccepted: boolean
}

export interface CaseFile {
  dispute: AdminDispute
  summary: DisputeSummary
  shiftTimeline: TimelineEntry[]
  /** No data source exists yet (contract D6), so this is always null. */
  checklist: unknown | null
  photos: CasePhoto[]
}

export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

/** Body of `POST /api/admin/disputes/{id}/resolve`. */
export interface ResolveRequest {
  faultParty: string | null
  compensationAmount: number
  lockWorker: boolean
  note: string
}
