import { apiClient } from '@/services/api'
import type { AuthApi } from './auth-controller'
import type { AuthResult } from './session'

/** The three identity endpoints of .spec/contracts/identity.md (2.3, 2.4, 2.5), over the typed client. */
export const authApi: AuthApi = {
  login: (email, password, role) => apiClient.post<AuthResult>('/auth/password/login', { email, password, role }),
  refresh: (refreshToken) => apiClient.post<AuthResult>('/auth/refresh', { refreshToken }),
  logout: async (refreshToken) => {
    await apiClient.post('/auth/logout', { refreshToken })
  },
}
