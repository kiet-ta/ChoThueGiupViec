// Hand-written from the approved contract: .spec/contracts/admin.md section 1 (`AuditLogEntry`) and 2.4.

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
  /** UTC. */
  changedAt: string
}

export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

/** What the filter bar holds; every field is optional text or empty. */
export interface AuditFilter {
  entityType: string
  entityId: string
  actorType: string
  /** `YYYY-MM-DD` of the Vietnam calendar, or empty. */
  from: string
  to: string
}
