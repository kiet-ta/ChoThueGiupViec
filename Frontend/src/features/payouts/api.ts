import type { SortState } from '@/components/data-table'
import { apiClient } from '@/services/api'
import type { BatchDetail, BatchStatus, PayoutBatch, Page } from './types'
import { batchQuery } from './view'

export const PAGE_SIZE = 20

export const payoutsApi = {
  list: (page: number, sort: SortState | null = null) =>
    apiClient.get<Page<PayoutBatch>>('/admin/payout-batches', { query: batchQuery(page, PAGE_SIZE, sort) }),
  detail: (batchId: number, payeeType: string, page: number) =>
    apiClient.get<BatchDetail>(`/admin/payout-batches/${batchId}`, { query: { payeeType: payeeType || undefined, page, pageSize: PAGE_SIZE } }),
}

export type { BatchStatus }
