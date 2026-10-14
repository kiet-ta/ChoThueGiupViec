// Shapes of the M5 API (contract .spec/contracts/agencies.md, decisions D9-D14). camelCase, UTC instants, dates as `yyyy-MM-dd`.

export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

export interface SubscriptionPackage {
  packageId: number
  packageCode: string
  packageName: string
  tier: string
  billingCycle: string
  price: number
  workerQuota: number
  commissionRate: number
  hasRosterDashboard: boolean
  hasAnalytics: boolean
  priorityDispatch: boolean
  isActive: boolean
}

export interface Subscription {
  subscriptionId: number
  agencyId: number
  packageId: number
  startDate: string
  endDate: string
  subStatus: string
  autoRenew: boolean
}

export interface SubscriptionState {
  tier: string
  package: SubscriptionPackage
  subscription: Subscription | null
  inGrace: boolean
  workerQuota: number
  workerCount: number
  hasRosterDashboard: boolean
  hasAnalytics: boolean
  priorityDispatch: boolean
}

export interface SubscriptionPurchase {
  subscription: Subscription
  paymentId: number
  gatewayTxnRef: string
  amount: number
  qrPayload: string | null
  payUrl: string | null
  expiresAt: string
}

export interface ImportRowError {
  /** Sheet row, the header is row 1; 0 for an error of the whole file (quota). */
  row: number
  column: string
  code: string
  message: string
}

export interface ImportResult {
  isValid: boolean
  committed: boolean
  totalRows: number
  validRows: number
  createdWorkers: number
  errors: ImportRowError[]
}

export interface RosterEntry {
  slotId: number
  workerId: number
  workerName: string
  slotDate: string
  shiftCode: string
  startTime: string
  endTime: string
  slotStatus: string
}

export interface RosterResult {
  created: number
  unchanged: number
  entries: RosterEntry[]
}

export interface RosterEntryError {
  index: number
  code: string
  message: string
}

export interface EscrowState {
  escrowDepositBalance: number
  agencyStatus: string
  minEscrowBalance: number
  hasEscrowWarning: boolean
  slaScore: number
  hasSlaWarning: boolean
  excludedFromPremium: boolean
}

export interface EscrowTransaction {
  escrowTxnId: number
  txnType: string
  amount: number
  balanceAfter: number
  slaPointsDelta: number | null
  orderId: number | null
  disputeId: number | null
  paymentId: number | null
  reason: string
  createdAt: string
  canAppeal: boolean
}

export interface DepositPurchase {
  paymentId: number
  gatewayTxnRef: string
  amount: number
  qrPayload: string | null
  payUrl: string | null
  expiresAt: string
}

export interface EscrowAppeal {
  appealId: number
  escrowTxnId: number
  agencyId: number
  reason: string
  status: string
  createdAt: string
  decidedAt: string | null
  decisionNote: string | null
}

export interface AgencyJob {
  assignmentId: number
  orderId: number
  workerId: number
  workerName: string
  serviceTier: string
  assignmentSeq: number
  assignmentStatus: string
  grossAmount: number
  commissionRate: number
  payoutAmount: number
  createdAt: string
  completedAt: string | null
}

export interface WorkerPerformance {
  workerId: number
  workerName: string
  assigned: number
  completed: number
  cancelled: number
  absent: number
  incident: number
  payoutTotal: number
  completionRate: number
}

export interface PerformanceReport {
  from: string
  to: string
  totalJobs: number
  payoutTotal: number
  workers: WorkerPerformance[]
}

export interface SwapWindow {
  assignmentId: number
  orderId: number
  /** OPEN, SWAPPED or RESCUE_STARTED */
  state: string
  openedAt: string
  deadlineAt: string
  remainingSeconds: number
  shiftStartUtc: string
  newWorkerId: number | null
}
