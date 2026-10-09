import { formatDateTime } from '../../lib/format.ts'
import type { EkycFilterStatus, EkycQueueItemResponse } from './types'

export type StatusTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger' | 'accent'

export const PAGE_SIZE = 20
export const REASON_MAX_LENGTH = 255
export const AUTO_APPROVE_THRESHOLD = 85.0

export interface EkycRow {
  workerId: number
  fullName: string
  phoneNumber: string
  nationalId: string
  confidenceScore: number
  confidenceText: string
  confidenceTone: StatusTone
  submittedAtFormatted: string
  raw: EkycQueueItemResponse
}

export interface FailedCall {
  status: number
  message: string
  data: unknown
  fieldErrors?: Record<string, string[]>
}

export interface ActionError {
  message: string
  reasons: string[]
}

export const TABS: { status: EkycFilterStatus; label: string }[] = [
  { status: 'PENDING', label: 'Chờ hậu kiểm' },
  { status: 'APPROVED', label: 'Đã duyệt' },
  { status: 'REJECTED', label: 'Từ chối' },
]

export function confidenceInfo(score: number): { label: string; tone: StatusTone } {
  if (score >= AUTO_APPROVE_THRESHOLD) {
    return { label: `${score.toFixed(1)}% (Tự động duyệt)`, tone: 'success' }
  }
  if (score >= 70.0) {
    return { label: `${score.toFixed(1)}% (Cần xem xét)`, tone: 'warning' }
  }
  return { label: `${score.toFixed(1)}% (Nghi vấn thấp)`, tone: 'danger' }
}

export function ekycQueueRow(item: EkycQueueItemResponse): EkycRow {
  const conf = confidenceInfo(item.confidenceScore)
  return {
    workerId: item.workerId,
    fullName: item.fullName,
    phoneNumber: item.phoneNumber,
    nationalId: item.nationalId,
    confidenceScore: item.confidenceScore,
    confidenceText: conf.label,
    confidenceTone: conf.tone,
    submittedAtFormatted: formatDateTime(item.submittedAt),
    raw: item,
  }
}

export function validateRejectionReason(reason: string): string | null {
  const trimmed = reason.trim()
  if (!trimmed) return 'Vui lòng nhập lý do từ chối hồ sơ eKYC.'
  if (trimmed.length > REASON_MAX_LENGTH) {
    return `Lý do từ chối quá dài (${trimmed.length}/${REASON_MAX_LENGTH} ký tự).`
  }
  return null
}

export function parseActionError(err: FailedCall): ActionError {
  if (err.status === 400 && err.fieldErrors?.rejectionReason) {
    return { message: err.fieldErrors.rejectionReason[0], reasons: [] }
  }
  if (err.status === 409) {
    return { message: 'Hồ sơ eKYC này đã được xử lý hoặc thay đổi trạng thái.', reasons: [] }
  }
  if (err.status === 404) {
    return { message: 'Không tìm thấy hồ sơ thợ.', reasons: [] }
  }
  return { message: err.message || 'Thao tác không thành công.', reasons: [] }
}

export const MOCK_EKYC_QUEUE: EkycQueueItemResponse[] = [
  {
    workerId: 102,
    fullName: 'Trần Văn Bình',
    phoneNumber: '0987654321',
    nationalId: '098765432109',
    frontCccdUrl: 'https://images.unsplash.com/photo-1544717305-2782549b5136?w=600&auto=format&fit=crop&q=80',
    backCccdUrl: 'https://images.unsplash.com/photo-1589829545856-d10d557cf95f?w=600&auto=format&fit=crop&q=80',
    selfieUrl: 'https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=600&auto=format&fit=crop&q=80',
    confidenceScore: 78.5,
    submittedAt: '2026-10-08T11:30:00Z',
  },
  {
    workerId: 105,
    fullName: 'Phạm Thị Hoa',
    phoneNumber: '0912345678',
    nationalId: '012345678905',
    frontCccdUrl: 'https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?w=600&auto=format&fit=crop&q=80',
    backCccdUrl: 'https://images.unsplash.com/photo-1589829545856-d10d557cf95f?w=600&auto=format&fit=crop&q=80',
    selfieUrl: 'https://images.unsplash.com/photo-1573497019940-1c28c88b4f3e?w=600&auto=format&fit=crop&q=80',
    confidenceScore: 65.2,
    submittedAt: '2026-10-08T14:15:00Z',
  },
]
