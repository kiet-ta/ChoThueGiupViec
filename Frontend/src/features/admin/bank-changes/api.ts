import { apiClient } from '@/services/api'
import type { BankChangePage, BankChangeRequest, BankChangeStatus } from './types'
import { bankChangeQuery } from './view'

export const PAGE_SIZE = 20

export const bankChangesApi = {
  list: (status: BankChangeStatus, page: number) =>
    apiClient.get<BankChangePage>('/admin/bank-change-requests', { query: bankChangeQuery(status, page, PAGE_SIZE) }),
  /** Writes the new account on the worker and the status APPROVED in one transaction (409 BANK_CHANGE_NOT_PENDING when decided). */
  approve: (id: number) => apiClient.post<BankChangeRequest>(`/admin/bank-change-requests/${id}/approve`),
  reject: (id: number, reason: string) => apiClient.post<BankChangeRequest>(`/admin/bank-change-requests/${id}/reject`, { reason }),
}
