// Base URL: '/api' in dev goes through the Vite proxy to the .NET backend.
const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api'

export interface ApiResponse<T> {
  success: boolean
  message: string
  data: T | null
}

export async function api<T>(path: string, init?: RequestInit): Promise<ApiResponse<T>> {
  const res = await fetch(`${BASE_URL}${path}`, {
    headers: { 'Content-Type': 'application/json', ...init?.headers },
    ...init,
  })
  return res.json() as Promise<ApiResponse<T>>
}
