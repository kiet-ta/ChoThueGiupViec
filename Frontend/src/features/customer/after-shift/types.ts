// Wire shapes of the after-shift endpoints (workers.md 2.4/2.5, ratings.md, disputes.md 2.1), camelCase, UTC ISO-8601.
export interface JobPhoto {
  photoId: number
  assignmentId: number
  /** BEFORE | AFTER */
  photoPhase: string
  angleNo: number
  photoUrl: string
  isAccepted: boolean
  uploadedAt: string
}

export interface RatingWindow {
  assignmentId: number
  canRate: boolean
  /** OPEN | ALREADY_RATED | WINDOW_CLOSED | NOT_COMPLETED */
  reason: string
  opensAt: string | null
  closesAt: string | null
  secondsRemaining: number
}

export interface RatingBody {
  stars: number
  criteria: { punctuality: number; cleaningQuality: number; attitude: number }
  comment: string | null
}

export interface Dispute {
  disputeId: number
  orderId: number
  raisedBy: string
  category: string
  description: string
  evidenceUrls: string[]
  /** Server value: OPEN | IN_REVIEW | RESOLVED (shown as sent when unknown). */
  disputeStatus: string
  faultParty: string | null
  compensationAmount: number | null
  slaDueAt: string
  resolvedAt: string | null
  createdAt: string
}

export interface FileDisputeBody {
  orderId: number
  category: string
  description: string
  evidenceUrls: string[]
}

export interface EvidenceFile {
  path: string
  url: string
  sizeBytes: number
}

export interface AssignmentResult {
  assignmentId: number
  status: string
}
