export type IncidentType = 'ACCIDENT' | 'HEALTH' | 'SEVERE_WEATHER' | 'OTHER'

export type ReDispatchStatus = 'SEARCHING' | 'REASSIGNED' | 'FAILED'

export interface IncidentLogDto {
  incidentId: number
  assignmentId: number
  workerId: number
  incidentType: IncidentType | string
  description: string
  photoEvidenceUrl?: string | null
  latitude: number
  longitude: number
  reportedAt: string
  isPenaltyExempt: boolean
  reDispatchStatus: ReDispatchStatus | string
  reDispatchDeadline: string
  substituteWorkerId?: number | null
}

export interface DispatchFilterParams {
  incidentType?: string
  status?: string
  search?: string
}

export function formatIncidentType(type: string): string {
  switch (type) {
    case 'ACCIDENT':
      return 'Tai nạn / Hỏng xe'
    case 'HEALTH':
      return 'Sức khỏe đột xuất'
    case 'SEVERE_WEATHER':
      return 'Thời tiết ngập lụt'
    case 'OTHER':
      return 'Sự cố khác'
    default:
      return type
  }
}

export function formatReDispatchStatus(status: string): { label: string; variant: 'warning' | 'success' | 'destructive' | 'secondary' } {
  switch (status) {
    case 'SEARCHING':
      return { label: 'Đang tìm thợ (5m)', variant: 'warning' }
    case 'REASSIGNED':
      return { label: 'Đã đổi thợ cứu hộ', variant: 'success' }
    case 'FAILED':
      return { label: 'Huỷ ca (Hoàn 100%)', variant: 'destructive' }
    default:
      return { label: status, variant: 'secondary' }
  }
}
