import { apiClient } from '@/services/api'
import type { Address, AddressBody, CustomerProfile, FavoriteWorker, ProfileBody, ReverseGeocodeResult } from './types'

export const accountApi = {
  addresses: () => apiClient.get<Address[]>('/customers/me/addresses'),
  address: (id: number) => apiClient.get<Address>(`/customers/me/addresses/${id}`),
  createAddress: (body: AddressBody) => apiClient.post<Address>('/customers/me/addresses', body),
  updateAddress: (id: number, body: AddressBody) => apiClient.put<Address>(`/customers/me/addresses/${id}`, body),
  deleteAddress: (id: number) => apiClient.delete<null>(`/customers/me/addresses/${id}`),
  favorites: () => apiClient.get<FavoriteWorker[]>('/customers/me/favorite-workers'),
  removeFavorite: (workerId: number) => apiClient.delete<null>(`/customers/me/favorite-workers/${workerId}`),
  profile: () => apiClient.get<CustomerProfile>('/customers/me'),
  updateProfile: (body: ProfileBody) => apiClient.put<CustomerProfile>('/customers/me', body),
  reverseGeocode: (latitude: number, longitude: number) =>
    apiClient.post<ReverseGeocodeResult>('/locations/reverse-geocode', { latitude, longitude }),
}
