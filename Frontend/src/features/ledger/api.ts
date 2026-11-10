import { apiClient } from '@/services/api'
import { toQuery } from './view'
import type { ChainResult, LedgerEntry, LedgerFilters, LedgerSummary, Page, ReconciliationReport } from './types'

export const PAGE_SIZE = 20

export const ledgerApi = {
  list: (filters: LedgerFilters, page: number) =>
    apiClient.get<Page<LedgerEntry>>('/admin/ledger', { query: toQuery(filters, page, PAGE_SIZE) }),
  summary: () => apiClient.get<LedgerSummary>('/admin/ledger/summary'),
  verify: () => apiClient.get<ChainResult>('/admin/ledger/verify'),
  reconciliation: () => apiClient.get<ReconciliationReport>('/admin/ledger/reconciliation'),
}
