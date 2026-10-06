import { createApiClient, type ApiConfig, type ApiResponse } from './client'

export { ApiError, buildQuery } from './client'
export type { ApiConfig, ApiResponse, FieldErrors, Query, RequestOptions } from './client'

// Base URL: '/api' in dev goes through the Vite proxy to the .NET backend.
const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api'

let config: ApiConfig = {}

/** WEB-BASE-02 plugs the token and the 401 handler in here; features never call this. */
export function configureApi(next: ApiConfig) {
  config = next
}

/**
 * Typed client: `await apiClient.get<Dispute[]>('/admin/disputes', { query: { page: 1 } })`
 * returns `data` or throws ApiError. Paths are relative to /api (no leading /api).
 */
export const apiClient = createApiClient({
  baseUrl: BASE_URL,
  fetch: (input, init) => fetch(input, init),
  config: () => config,
})

/** Raw envelope access kept from the original scaffold; prefer `apiClient`. */
export async function api<T>(path: string, init?: RequestInit): Promise<ApiResponse<T>> {
  const res = await fetch(`${BASE_URL}${path}`, {
    headers: { 'Content-Type': 'application/json', ...init?.headers },
    ...init,
  })
  return res.json() as Promise<ApiResponse<T>>
}
