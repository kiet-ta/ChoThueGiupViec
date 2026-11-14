import { apiClient } from '@/services/api'
import type { AdminWorkerPage, SuperFreelancerResult, UnlockResult, WorkerAction } from './types'
import { actionRequest, searchQuery } from './view'

/** One call for the three actions: `actionRequest` (pure, tested) decides the method, the path and the body. */
export const workerControlsApi = {
  run: (action: WorkerAction, workerId: number, reason: string) => {
    const { method, path, body } = actionRequest(action, workerId, reason)
    return apiClient.request<SuperFreelancerResult | UnlockResult>(method, path, { body })
  },

  /** The worker lookup of the picker (contract admin.md 2.8); `searchQuery` (pure, tested) builds the query. */
  search: (text: string, signal?: AbortSignal) => apiClient.get<AdminWorkerPage>('/admin/workers', { query: searchQuery(text), signal }),
}
