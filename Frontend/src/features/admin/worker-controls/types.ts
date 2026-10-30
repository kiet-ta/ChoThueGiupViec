/** `data` of POST / DELETE /api/admin/workers/{id}/super-freelancer (contract admin.md 2.5). */
export interface SuperFreelancerResult {
  workerId: number
  isSuperFreelancer: boolean
}

/** `data` of POST /api/admin/workers/{id}/unlock (contract admin.md 2.6, ADMIN-UAT-10). */
export interface UnlockResult {
  workerId: number
  workStatus: string
}

export type WorkerAction = 'APPROVE_SUPER' | 'REVOKE_SUPER' | 'UNLOCK'
