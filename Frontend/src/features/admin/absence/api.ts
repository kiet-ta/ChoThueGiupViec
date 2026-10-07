import { apiClient } from '@/services/api'
import type { AbsenceReport, AbsenceStatus, Page } from './types'

export const PAGE_SIZE = 20

export const absenceApi = {
  list: (status: AbsenceStatus, page: number) =>
    apiClient.get<Page<AbsenceReport>>('/admin/absence-reports', { query: { status, page, pageSize: PAGE_SIZE } }),
  get: (assignmentId: number) => apiClient.get<AbsenceReport>(`/admin/absence-reports/${assignmentId}`),
  approve: (assignmentId: number) => apiClient.post<AbsenceReport>(`/admin/absence-reports/${assignmentId}/approve`),
  reject: (assignmentId: number, reason: string) =>
    apiClient.post<AbsenceReport>(`/admin/absence-reports/${assignmentId}/reject`, { reason }),
}
