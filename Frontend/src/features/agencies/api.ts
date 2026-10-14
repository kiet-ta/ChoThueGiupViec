import { ApiError, apiClient } from '@/services/api'
import type {
  AgencyJob,
  DepositPurchase,
  EscrowAppeal,
  EscrowState,
  EscrowTransaction,
  ImportResult,
  Page,
  PerformanceReport,
  RosterEntry,
  RosterEntryError,
  RosterResult,
  SubscriptionPackage,
  SubscriptionPurchase,
  SubscriptionState,
  SwapWindow,
} from './types'

export const PAGE_SIZE = 20

function fileForm(file: File): FormData {
  const form = new FormData()
  form.append('file', file)
  return form
}

/** Calls of the Partner Portal (M5 backend, policy PartnerOnly). Paths are relative to /api. */
export const agenciesApi = {
  // WEB-M5-01 subscription
  packages: () => apiClient.get<SubscriptionPackage[]>('/agencies/packages'),
  subscription: () => apiClient.get<SubscriptionState>('/agencies/subscriptions/me'),
  subscribe: (packageId: number) => apiClient.post<SubscriptionPurchase>('/agencies/subscriptions', { packageId }),

  // WEB-M5-02 worker import
  importTemplate: () => apiClient.download('/agencies/workers/import/template'),
  signGuarantee: (file: File) => apiClient.post<{ guaranteeSignedAt: string }>('/agencies/guarantee', fileForm(file)),
  importDryRun: (file: File) => apiClient.post<ImportResult>('/agencies/workers/import/dry-run', fileForm(file)),
  /** A 422 carries the same result as its data (nothing was written): returned as a result, not thrown. */
  async importCommit(file: File): Promise<ImportResult> {
    try {
      return await apiClient.post<ImportResult>('/agencies/workers/import/commit', fileForm(file))
    } catch (e) {
      if (e instanceof ApiError && e.status === 422 && e.data && typeof e.data === 'object') return e.data as ImportResult
      throw e
    }
  },

  // WEB-M5-03 roster
  roster: (date?: string) => apiClient.get<RosterEntry[]>('/agencies/roster', { query: { date } }),
  /** A 422 carries the per-entry errors; they come back in `errors` instead of being thrown. */
  async putRoster(entries: { workerId: number; slotDate: string; shiftCode: string }[]): Promise<
    { ok: true; result: RosterResult } | { ok: false; errors: RosterEntryError[] }
  > {
    try {
      return { ok: true, result: await apiClient.put<RosterResult>('/agencies/roster', { entries }) }
    } catch (e) {
      if (e instanceof ApiError && e.status === 422) {
        const data = e.data as { errors?: RosterEntryError[] } | null
        return { ok: false, errors: data?.errors ?? [] }
      }
      throw e
    }
  },
  removeSlot: (slotId: number) => apiClient.delete<null>(`/agencies/roster/${slotId}`),

  // WEB-M5-04 escrow
  escrow: () => apiClient.get<EscrowState>('/agencies/escrow'),
  escrowTransactions: (page: number) =>
    apiClient.get<Page<EscrowTransaction>>('/agencies/escrow/transactions', { query: { page, pageSize: PAGE_SIZE } }),
  deposit: (amount: number) => apiClient.post<DepositPurchase>('/agencies/escrow/deposits', { amount }),
  appeal: (escrowTxnId: number, reason: string) =>
    apiClient.post<EscrowAppeal>(`/agencies/escrow/transactions/${escrowTxnId}/appeal`, { reason }),

  // WEB-M5-05 jobs and report
  jobs: (page: number, status?: string) =>
    apiClient.get<Page<AgencyJob>>('/agencies/orders', { query: { page, pageSize: PAGE_SIZE, status } }),
  performance: (from?: string, to?: string) => apiClient.get<PerformanceReport>('/agencies/reports/performance', { query: { from, to } }),

  // WEB-M5-06 swap window
  swapWindows: () => apiClient.get<SwapWindow[]>('/agencies/swap-windows'),
  reportUnavailable: (assignmentId: number, reason: string) =>
    apiClient.post<SwapWindow>(`/agencies/assignments/${assignmentId}/unavailable`, { reason }),
  swap: (assignmentId: number, newWorkerId: number) =>
    apiClient.post<SwapWindow>(`/agencies/assignments/${assignmentId}/swap`, { newWorkerId }),
}
