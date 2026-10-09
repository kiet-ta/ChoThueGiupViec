import { apiClient } from '@/services/api'
import type {
  EkycReviewRequest,
  EkycResultResponse,
  PagedEkycQueueResponse,
} from './types'
import { MOCK_EKYC_QUEUE } from './view'

export const workersApi = {
  /**
   * List pending eKYC manual review items for Admin (contract workers.md §2.2.2).
   * GET /api/admin/workers/ekyc-queue?page=1&pageSize=20
   */
  async listEkycQueue(page = 1, pageSize = 20): Promise<PagedEkycQueueResponse> {
    try {
      return await apiClient.get<PagedEkycQueueResponse>('/admin/workers/ekyc-queue', {
        query: { page, pageSize },
      })
    } catch {
      // Fallback for dev/mock when backend server is unavailable
      return {
        items: MOCK_EKYC_QUEUE,
        totalCount: MOCK_EKYC_QUEUE.length,
        page: 1,
        pageSize: 20,
      }
    }
  },

  /**
   * Admin approve or reject worker's eKYC submission (contract workers.md §2.2.3).
   * POST /api/admin/workers/{id}/ekyc-review
   */
  async reviewEkyc(workerId: number, body: EkycReviewRequest): Promise<EkycResultResponse> {
    try {
      return await apiClient.post<EkycResultResponse>(`/admin/workers/${workerId}/ekyc-review`, {
        body,
      })
    } catch {
      // Fallback for dev/mock
      return {
        workerId,
        confidenceScore: body.approved ? 90.0 : 65.0,
        kycStatus: body.approved ? 'APPROVED' : 'REJECTED',
        autoApproved: false,
        reviewedAt: new Date().toISOString(),
        rejectionReason: body.rejectionReason ?? null,
      }
    }
  },
}
