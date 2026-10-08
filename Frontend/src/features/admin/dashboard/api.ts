import { apiClient } from '@/services/api'
import type { Dashboard, NearSlaDispute, Page } from './types'

/** How many near-SLA disputes the dashboard lists (contract admin.md 2.3). */
export const NEAR_SLA_PAGE_SIZE = 5

export const dashboardApi = {
  get: () => apiClient.get<Dashboard>('/admin/dashboard'),
  nearSla: () =>
    apiClient.get<Page<NearSlaDispute>>('/admin/disputes', { query: { nearSla: true, pageSize: NEAR_SLA_PAGE_SIZE } }),
}
