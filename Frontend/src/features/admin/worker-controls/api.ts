import { apiClient } from '@/services/api'
import type { SuperFreelancerResult, UnlockResult, WorkerAction } from './types'
import { actionRequest } from './view'

/** One call for the three actions: `actionRequest` (pure, tested) decides the method, the path and the body. */
export const workerControlsApi = {
  run: (action: WorkerAction, workerId: number, reason: string) => {
    const { method, path, body } = actionRequest(action, workerId, reason)
    return apiClient.request<SuperFreelancerResult | UnlockResult>(method, path, { body })
  },
}
