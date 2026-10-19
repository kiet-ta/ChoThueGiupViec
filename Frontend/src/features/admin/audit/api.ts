import { apiClient } from '@/services/api'
import type { AuditFilter, AuditLogEntry, Page } from './types'
import { auditQuery } from './view'

export const auditApi = {
  /** `GET /api/admin/audit-logs` (contract admin.md 2.4), newest first. Read only. */
  list: (filter: AuditFilter, page: number) =>
    apiClient.get<Page<AuditLogEntry>>('/admin/audit-logs', { query: auditQuery(filter, page) }),
}
