import { apiClient } from '@/services/api'
import { toQuery } from './view'
import type { ChainResult, LedgerEntry, LedgerFilters, LedgerSummary, Page, ReconciliationReport, Withdrawal, WithdrawalStatus } from './types'

export const PAGE_SIZE = 20

export const ledgerApi = {
  list: (filters: LedgerFilters, page: number) =>
    apiClient.get<Page<LedgerEntry>>('/admin/ledger', { query: toQuery(filters, page, PAGE_SIZE) }),
  summary: () => apiClient.get<LedgerSummary>('/admin/ledger/summary'),
  verify: () => apiClient.get<ChainResult>('/admin/ledger/verify'),
  reconciliation: () => apiClient.get<ReconciliationReport>('/admin/ledger/reconciliation'),
}

export const withdrawalsApi = {
  list: (status: WithdrawalStatus, page: number) => apiClient.get<Page<Withdrawal>>('/admin/withdrawals', { query: { status, page, pageSize: PAGE_SIZE } }),
  /** Writes the WITHDRAWAL entry and pays the request in one transaction (409 WITHDRAWAL_INSUFFICIENT / WITHDRAWAL_NOT_PENDING). */
  approve: (id: number) => apiClient.post<Withdrawal>(`/admin/withdrawals/${id}/approve`),
  reject: (id: number, reason: string) => apiClient.post<Withdrawal>(`/admin/withdrawals/${id}/reject`, { reason }),
}
