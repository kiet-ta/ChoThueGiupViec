import { apiClient } from '@/services/api'
import type { IncidentLogDto } from './types'

export const mockIncidents: IncidentLogDto[] = [
  {
    incidentId: 3001,
    assignmentId: 1001,
    workerId: 42,
    incidentType: 'ACCIDENT',
    description: 'Thủng lốp xe trên đường đi làm tại khu vực Cầu Giấy',
    photoEvidenceUrl: 'https://images.unsplash.com/photo-1544620347-c4fd4a3d5957?auto=format&fit=crop&w=400&q=80',
    latitude: 21.0285,
    longitude: 105.8542,
    reportedAt: new Date(Date.now() - 3 * 60 * 1000).toISOString(),
    isPenaltyExempt: true,
    reDispatchStatus: 'SEARCHING',
    reDispatchDeadline: new Date(Date.now() + 2 * 60 * 1000).toISOString(),
    substituteWorkerId: null,
  },
  {
    incidentId: 3002,
    assignmentId: 1005,
    workerId: 58,
    incidentType: 'SEVERE_WEATHER',
    description: 'Ngập nước đường Nguyễn Xiển không thể di chuyển tiếp',
    photoEvidenceUrl: 'https://images.unsplash.com/photo-1515694346937-94d85e41e6f0?auto=format&fit=crop&w=400&q=80',
    latitude: 20.9902,
    longitude: 105.8015,
    reportedAt: new Date(Date.now() - 45 * 60 * 1000).toISOString(),
    isPenaltyExempt: true,
    reDispatchStatus: 'REASSIGNED',
    reDispatchDeadline: new Date(Date.now() - 40 * 60 * 1000).toISOString(),
    substituteWorkerId: 77,
  },
]

export async function fetchAdminIncidents(): Promise<IncidentLogDto[]> {
  try {
    const data = await apiClient.get<IncidentLogDto[]>('/dispatch/admin/incidents')
    if (Array.isArray(data)) {
      return data
    }
    return mockIncidents
  } catch {
    // If backend endpoint is not yet connected in dev, fallback to mock data per contract
    return mockIncidents
  }
}
