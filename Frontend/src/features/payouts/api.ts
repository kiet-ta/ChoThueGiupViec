import { apiClient } from '@/services/api'
import type { BatchDetail, BatchStatus, PayoutBatch, Page } from './types'

export const PAGE_SIZE = 20

export const payoutsApi = {
  list: (page: number) => apiClient.get<Page<PayoutBatch>>('/admin/payout-batches', { query: { page, pageSize: PAGE_SIZE } }),
  detail: (batchId: number, payeeType: string, page: number) =>
    apiClient.get<BatchDetail>(`/admin/payout-batches/${batchId}`, { query: { payeeType: payeeType || undefined, page, pageSize: PAGE_SIZE } }),
}

export type { BatchStatus }
