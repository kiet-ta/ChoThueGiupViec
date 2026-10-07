import { configureApi } from '@/services/api'
import { authApi } from './auth-api'
import { createAuthController } from './auth-controller'
import { createSessionStore, type KeyValueStorage } from './session'

function browserSessionStorage(): KeyValueStorage | null {
  try {
    return typeof sessionStorage === 'undefined' ? null : sessionStorage
  } catch {
    return null // reading the property itself can throw when site data is blocked
  }
}

/** The one controller of the app: the router guard, the login page and the layout all read it. */
export const authController = createAuthController({
  api: authApi,
  store: createSessionStore(browserSessionStorage()),
  timers: { setTimeout: (h, ms) => window.setTimeout(h, ms), clearTimeout: (id) => window.clearTimeout(id as number) },
})

// The API client takes the token from memory and reports a rejected token here (WEB-BASE-03 hook).
configureApi({
  getAccessToken: authController.getAccessToken,
  onUnauthorized: authController.handleUnauthorized,
})
