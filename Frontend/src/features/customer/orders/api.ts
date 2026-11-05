import { apiClient } from '@/services/api'
import type { Order, OrderPage } from '../booking/types'
import type { Payment } from '../payment/types'
import type { AddressDetail, Extension, OrderProgress } from './types'
import type { ArrivalStatus } from './view'

export const ORDERS_PAGE_SIZE = 20

export const ordersApi = {
  list: (page: number, status: string | null) =>
    apiClient.get<OrderPage>('/booking/orders', { query: { page, pageSize: ORDERS_PAGE_SIZE, status } }),
  get: (orderId: number) => apiClient.get<Order>(`/booking/orders/${orderId}`),
  progress: (orderId: number) => apiClient.get<OrderProgress>(`/booking/orders/${orderId}/progress`),
  payments: (orderId: number) => apiClient.get<Payment[]>('/payments', { query: { orderId } }),
  address: (addressId: number) => apiClient.get<AddressDetail>(`/customers/me/addresses/${addressId}`),
  cancel: (orderId: number, reason: string) => apiClient.post<Order>(`/booking/orders/${orderId}/cancel`, { reason }),
  arrival: (assignmentId: number) => apiClient.get<ArrivalStatus>(`/dispatch/assignments/${assignmentId}/check-in`),
  confirmArrival: (assignmentId: number) =>
    apiClient.post<{ assignmentId: number; isCustomerConfirmed: boolean; status: string }>(`/dispatch/assignments/${assignmentId}/check-in/confirm`, {}),
  requestExtension: (orderId: number, assignmentId: number, extraHours: number) =>
    apiClient.post<Extension>(`/booking/orders/${orderId}/extensions`, { assignmentId, extraHours }),
}
