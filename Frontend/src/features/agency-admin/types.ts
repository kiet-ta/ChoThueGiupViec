// Shapes of the Admin side of M5 (contract .spec/contracts/agencies.md, decisions D9, D12).

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

export interface AuditLogEntry {
  logId: number
  actorType: string
  adminId: number | null
  entityType: string
  entityId: string
  fieldName: string
  oldValue: string | null
  newValue: string | null
  reason: string | null
  changedAt: string
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

export interface PackageEdit {
  price?: number
  workerQuota?: number
  commissionRate?: number
  reason: string
}
