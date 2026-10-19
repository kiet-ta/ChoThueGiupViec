import { apiClient } from '@/services/api'
import type {
  EkycFilterStatus,
  EkycReviewRequest,
  EkycResultResponse,
  PagedEkycQueueResponse,
} from './types'
import { ekycQueueRequest, ekycReviewRequest } from './view'

// Failures are thrown to the page (a list with a retry, or the error of the action). This file used to catch every error and answer with
// sample workers and a made-up "approved" result, so an Admin could be told a worker was approved when nothing had been saved.
export const workersApi = {
  /** One of the three lists of the audit page (contract workers.md 2.2.2). */
  listEkycQueue(page = 1, pageSize = 20, status: EkycFilterStatus = 'PENDING'): Promise<PagedEkycQueueResponse> {
    const request = ekycQueueRequest(page, pageSize, status)
    return apiClient.get<PagedEkycQueueResponse>(request.path, { query: request.query })
  },

  /** Admin approves or rejects a worker's eKYC (contract workers.md 2.2.3). */
  reviewEkyc(workerId: number, body: EkycReviewRequest): Promise<EkycResultResponse> {
    const request = ekycReviewRequest(workerId, body)
    return apiClient.post<EkycResultResponse>(request.path, request.body)
  },
}
