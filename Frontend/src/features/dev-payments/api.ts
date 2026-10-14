import { apiClient } from '@/services/api'
import type { DevConfirmResult, DevPayment } from './types'

/** Development-only tool (UAT-PAY): every call is 404 on a backend that is not in Development. */
export const devPaymentsApi = {
  list: () => apiClient.get<DevPayment[]>('/dev/payments'),
  confirm: (paymentId: number) => apiClient.post<DevConfirmResult>(`/dev/payments/${paymentId}/confirm`, {}),
}
