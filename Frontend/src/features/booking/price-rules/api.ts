import { apiClient } from '@/services/api'
import type { AuditLogEntry, Page, PriceRule } from './types'

export const HISTORY_PAGE_SIZE = 10

export const priceRulesApi = {
  list: () => apiClient.get<PriceRule[]>('/admin/price-rules'),
  update: (ruleId: number, unitPrice: number, reason: string) =>
    apiClient.put<PriceRule>(`/admin/price-rules/${ruleId}`, { unitPrice, reason }),
  /** The change history is not a Booking endpoint (contract booking.md 4.3): it is M6's audit log filtered on the rule. */
  history: (ruleId: number, page: number) =>
    apiClient.get<Page<AuditLogEntry>>('/admin/audit-logs', {
      query: { entityType: 'PRICE_RULE', entityId: String(ruleId), page, pageSize: HISTORY_PAGE_SIZE },
    }),
}
