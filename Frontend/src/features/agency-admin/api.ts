import { apiClient } from '@/services/api'
import type { AuditLogEntry, EscrowAppeal, Page, PackageEdit, SubscriptionPackage } from './types'

export const HISTORY_PAGE_SIZE = 10

/** Admin calls of M5 (policy AdminOnly). Paths are relative to /api. */
export const agencyAdminApi = {
  packages: () => apiClient.get<SubscriptionPackage[]>('/admin/subscription-packages'),
  updatePackage: (packageId: number, edit: PackageEdit) =>
    apiClient.put<SubscriptionPackage>(`/admin/subscription-packages/${packageId}`, edit),
  /** The history is M6's audit log filtered on the package (decisions G-5). */
  packageHistory: (packageId: number, page: number) =>
    apiClient.get<Page<AuditLogEntry>>('/admin/audit-logs', {
      query: { entityType: 'SUBSCRIPTION_PACKAGE', entityId: String(packageId), page, pageSize: HISTORY_PAGE_SIZE },
    }),
  appeals: (status: string) => apiClient.get<EscrowAppeal[]>('/admin/escrow/appeals', { query: { status } }),
  decideAppeal: (appealId: number, accept: boolean, note: string) =>
    apiClient.post<EscrowAppeal>(`/admin/escrow/appeals/${appealId}/decision`, { accept, note: note || null }),
}
