import { apiClient } from '@/services/api'
import type { AdminDispute, CaseFile, DisputeSummary, Page, ResolveRequest } from './types'

export const PAGE_SIZE = 20

export interface QueueQuery {
  /** Comma list of statuses, for example `OPEN,IN_REVIEW`. */
  status: string
  /** HIGH, MEDIUM, LOW or empty for all. */
  priority: string
  page: number
}

export const disputesApi = {
  queue: (q: QueueQuery) =>
    apiClient.get<Page<DisputeSummary>>('/admin/disputes', {
      query: { status: q.status, priority: q.priority || undefined, page: q.page, pageSize: PAGE_SIZE },
    }),
  get: (disputeId: number) => apiClient.get<CaseFile>(`/admin/disputes/${disputeId}`),
  take: (disputeId: number) => apiClient.post<AdminDispute>(`/admin/disputes/${disputeId}/take`),
  resolve: (disputeId: number, body: ResolveRequest) =>
    apiClient.post<AdminDispute>(`/admin/disputes/${disputeId}/resolve`, body),
}
