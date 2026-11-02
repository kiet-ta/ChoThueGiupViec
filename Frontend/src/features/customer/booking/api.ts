import { apiClient } from '@/services/api'
import type { BookingOptions, CreateOrderBody, CustomerAddress, FreeShift, Order, OrderPage, PriceQuote, ServiceTier } from './types'

export const bookingApi = {
  options: () => apiClient.get<BookingOptions>('/booking/options'),
  availability: (workerId: number, from: string, to: string) =>
    apiClient.get<FreeShift[]>(`/customers/me/favorite-workers/${workerId}/availability`, { query: { from, to } }),
  addresses: () => apiClient.get<CustomerAddress[]>('/customers/me/addresses'),
  quote: (addressId: number, serviceTier: ServiceTier) =>
    apiClient.get<PriceQuote>('/booking/price-quote', { query: { addressId, serviceTier } }),
  createOrder: (body: CreateOrderBody) => apiClient.post<Order>('/booking/orders', body),
  order: (orderId: number) => apiClient.get<Order>(`/booking/orders/${orderId}`),
  orders: (pageSize = 20) => apiClient.get<OrderPage>('/booking/orders', { query: { page: 1, pageSize } }),
}
