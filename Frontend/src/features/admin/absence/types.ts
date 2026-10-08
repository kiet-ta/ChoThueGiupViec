// Hand-written from the approved contract until gate G4: .spec/contracts/admin.md section 1 (`AbsenceReport`) and 2.2.

export type AbsenceStatus = 'PENDING' | 'APPROVED' | 'REJECTED'

export interface AbsenceReport {
  assignmentId: number
  orderId: number
  orderCode: string
  workerId: number
  workerName: string
  /** FREELANCER or AGENCY_STAFF. */
  workerType: string
  agencyName: string | null
  customerName: string
  status: AbsenceStatus
  checkedInAt: string
  customerAbsentAt: string
  gpsVerified: boolean
  distanceM: number
  deviceLat: number
  deviceLng: number
  callAttempts: number
  waitedMinutes: number
  grossAmount: number
  absenceFeeAmount: number
  customerRefundAmount: number
  photoUrl: string | null
  canApprove: boolean
  /** GPS_NOT_VERIFIED, CALLS_BELOW_MINIMUM, WAIT_BELOW_MINIMUM or ABSENCE_NOT_REPORTED. */
  blockReasons: string[]
}

export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}
