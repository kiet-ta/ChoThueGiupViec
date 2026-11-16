/** `data` of POST / DELETE /api/v1/admin/workers/{id}/super-freelancer (contract admin.md 2.5). */
export interface SuperFreelancerResult {
  workerId: number
  isSuperFreelancer: boolean
}

/** `data` of POST /api/v1/admin/workers/{id}/unlock (contract admin.md 2.6, ADMIN-UAT-10). */
export interface UnlockResult {
  workerId: number
  workStatus: string
}

export type WorkerAction = 'APPROVE_SUPER' | 'REVOKE_SUPER' | 'UNLOCK'

/** One worker of GET /api/v1/admin/workers (contract admin.md 2.8, ADMIN-UAT-14). */
export interface AdminWorker {
  workerId: number
  fullName: string
  phoneNumber: string
  nationalId: string
  /** FREELANCER | AGENCY_STAFF */
  workerType: string
  agencyId: number | null
  agencyName: string | null
  /** PENDING | IDLE | BUSY | LOCKED | RETIRING | RETIRED */
  workStatus: string
  kycStatus: string
  isSuperFreelancer: boolean
  ratingAvg: number
  completedJobs: number
}

export interface AdminWorkerPage {
  items: AdminWorker[]
  page: number
  pageSize: number
  total: number
}
