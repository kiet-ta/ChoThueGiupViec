import { apiClient } from '@/services/api'
import type { AssignmentResult, Dispute, EvidenceFile, FileDisputeBody, JobPhoto, RatingBody, RatingWindow } from './types'

export const afterShiftApi = {
  photos: (assignmentId: number) => apiClient.get<JobPhoto[]>(`/workers/assignments/${assignmentId}/photos`),
  /** The photo file needs the bearer token, so it is fetched as a blob and shown through an object URL. */
  photoFile: (assignmentId: number, photoId: number) => apiClient.download(`/workers/assignments/${assignmentId}/photos/${photoId}/file`),
  accept: (assignmentId: number, feedback: string | null) => apiClient.post<AssignmentResult>(`/customers/assignments/${assignmentId}/accept`, { feedback }),
  requestRedo: (assignmentId: number, reason: string) => apiClient.post<AssignmentResult>(`/customers/assignments/${assignmentId}/request-redo`, { reason }),
  ratingWindow: (assignmentId: number) => apiClient.get<RatingWindow>(`/customers/me/assignments/${assignmentId}/rating-window`),
  rate: (assignmentId: number, body: RatingBody) => apiClient.post<unknown>(`/customers/me/assignments/${assignmentId}/rating`, body),
  disputes: () => apiClient.get<Dispute[]>('/customers/me/disputes'),
  fileDispute: (body: FileDisputeBody) => apiClient.post<Dispute>('/customers/me/disputes', body),
  uploadEvidence: (fileName: string, contentType: string, contentBase64: string) =>
    apiClient.post<EvidenceFile>('/customers/me/disputes/evidence', { fileName, contentType, contentBase64 }),
}
