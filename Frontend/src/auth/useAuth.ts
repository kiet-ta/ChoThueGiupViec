import { useSyncExternalStore } from 'react'
import { authController } from './index'

export function useAuth() {
  const state = useSyncExternalStore(authController.subscribe, authController.getSnapshot)
  return { ...state, login: authController.login, logout: authController.logout }
}
