// Hand-written from the approved contracts until gate G4 generates the client: .spec/contracts/admin.md 2.3 and disputes.md 2.2.

/** `GET /api/admin/dashboard`: exactly these three groups (decision G-7). */
export interface Dashboard {
  generatedAt: string
  orders: { today: number; thisWeek: number }
  shifts: { inProgress: number; completedToday: number }
  disputes: { open: number; nearSla: number }
}

export interface NearSlaWorker {
  workerId: number
  fullName: string
  /** FREELANCER or AGENCY_STAFF. */
  workerType: string
  agencyName: string | null
}

/** The fields of a dispute queue row (`DisputeSummary`) that the dashboard list shows. */
export interface NearSlaDispute {
  disputeId: number
  orderCode: string
  customerName: string
  workers: NearSlaWorker[]
  /** Negative when overdue. */
  slaSecondsRemaining: number
}

export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}
