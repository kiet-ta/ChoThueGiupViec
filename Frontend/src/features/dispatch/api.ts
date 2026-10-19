import { apiClient } from '@/services/api'
import type { IncidentLogDto } from './types'

/**
 * The incidents of the field, newest first (contract dispatch.md 3.3, `GET /api/dispatch/admin/incidents`). A failure is thrown to the
 * page, which shows it with a retry: the page never invents incidents.
 */
export async function fetchAdminIncidents(): Promise<IncidentLogDto[]> {
  return apiClient.get<IncidentLogDto[]>('/dispatch/admin/incidents')
}
