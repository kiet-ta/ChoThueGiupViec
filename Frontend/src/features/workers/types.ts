/** eKYC item returned from GET /api/admin/workers/ekyc-queue (contract workers.md §2.2.2). */
export interface EkycQueueItemResponse {
  workerId: number
  fullName: string
  phoneNumber: string
  nationalId: string
  frontCccdUrl: string
  backCccdUrl: string
  selfieUrl: string
  confidenceScore: number
  submittedAt: string
}

/** Paginated list envelope. */
export interface PagedEkycQueueResponse {
  items: EkycQueueItemResponse[]
  totalCount: number
  page: number
  pageSize: number
}

/** Payload for POST /api/admin/workers/{id}/ekyc-review (contract workers.md §2.2.3). */
export interface EkycReviewRequest {
  approved: boolean
  rejectionReason?: string | null
}

/** Response from eKYC review or submission (contract workers.md §2.2.1 / §2.2.3). */
export interface EkycResultResponse {
  workerId: number
  confidenceScore: number
  kycStatus: 'APPROVED' | 'REJECTED' | 'MANUAL_REVIEW' | 'PENDING'
  autoApproved: boolean
  reviewedAt: string
  rejectionReason?: string | null
}

export type EkycFilterStatus = 'PENDING' | 'APPROVED' | 'REJECTED'
