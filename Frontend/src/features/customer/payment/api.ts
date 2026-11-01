import { apiClient } from '@/services/api'
import type { Payment } from './types'

export const paymentApi = {
  /** 201 for a new QR, 200 with the existing PENDING one: tapping twice never creates two. */
  createOrderQr: (orderId: number) => apiClient.post<Payment>(`/payments/orders/${orderId}/qr`, {}),
  /** Same contract as the order QR (payments.md 2.2): the extension must be PENDING_PAYMENT, the amount is its extraAmount. */
  createExtensionQr: (extensionId: number) => apiClient.post<Payment>(`/payments/extensions/${extensionId}/qr`, {}),
  get: (paymentId: number) => apiClient.get<Payment>(`/payments/${paymentId}`),
}
