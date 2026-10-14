// Hand-written from the contracts until gate G4: .spec/contracts/booking.md section 2 (`PriceRule`), 4.1, 4.2 and
// .spec/contracts/admin.md section 1 (`AuditLogEntry`), 2.4.

export type ServiceTier = 'ECONOMY' | 'PREMIUM'

export interface PriceRule {
  ruleId: number
  serviceTier: ServiceTier
  /** UP_TO_30, FROM_31_TO_80 or OVER_80. */
  areaBracket: string
  /** Whole VND per worker per shift. */
  unitPrice: number
  isActive: boolean
  updatedAt: string
  updatedBy: number | null
}

export interface AuditLogEntry {
  logId: number
  /** ADMIN or SYSTEM. */
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

export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}
