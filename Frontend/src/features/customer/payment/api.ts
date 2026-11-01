import { apiClient } from '@/services/api'
import type { Payment } from './types'

export const paymentApi = {
  /** 201 for a new QR, 200 with the existing PENDING one: tapping twice never creates two. */
  createOrderQr: (orderId: number) => apiClient.post<Payment>(`/payments/orders/${orderId}/qr`, {}),
  get: (paymentId: number) => apiClient.get<Payment>(`/payments/${paymentId}`),
}
