// Wire shapes of .spec/contracts/booking.md section 2 used by the order screens (camelCase, UTC ISO-8601).
import type { AcceptanceBlock } from '../after-shift/acceptance'

export interface ProgressAssignment {
  assignmentId: number
  assignmentSeq: number
  workerId: number
  workerName: string | null
  workerRatingAvg: number
  assignmentStatus: string
  acceptedAt: string | null
  completedAt: string | null
  /** ACCEPT-02: the server's acceptance phase and deadlines (absent on an older server, null outside the execution). */
  acceptance?: AcceptanceBlock | null
}

export interface Extension {
  extensionId: number
  orderId: number
  workerId: number
  extraHours: number
  extraAmount: number
  extStatus: string
  workerDecision: string
  requestedAt: string
  decidedAt: string | null
}

export interface OrderProgress {
  orderId: number
  orderStatus: string
  requiredWorkers: number
  assignments: ProgressAssignment[]
  extension: Extension | null
  /** Decisions D35: while a seat a worker cancelled is re-dispatched, when the search stops (UTC); null otherwise. */
  substituteSearchUntil?: string | null
}

export interface AddressDetail {
  addressId: number
  label: string
  addressLine: string
  district: string
  city: string
  totalAreaM2: number
}
