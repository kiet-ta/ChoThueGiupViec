// Wire shapes of .spec/contracts/booking.md section 2 used by the order screens (camelCase, UTC ISO-8601).
export interface ProgressAssignment {
  assignmentId: number
  assignmentSeq: number
  workerId: number
  workerName: string | null
  workerRatingAvg: number
  assignmentStatus: string
  acceptedAt: string | null
  completedAt: string | null
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
}

export interface AddressDetail {
  addressId: number
  label: string
  addressLine: string
  district: string
  city: string
  totalAreaM2: number
}
