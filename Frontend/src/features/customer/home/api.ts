import { apiClient } from '@/services/api'
import type { OrderPage } from '../booking/types'

export const homeApi = {
  completedOrders: () => apiClient.get<OrderPage>('/booking/orders', { query: { status: 'COMPLETED', page: 1, pageSize: 100 } }),
}
