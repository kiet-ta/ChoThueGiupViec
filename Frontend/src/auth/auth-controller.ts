// Login, refresh and logout orchestration (WEB-BASE-02). The HTTP calls, the clock and the timers are injected,
// so the rules below run under `node --test`. React reads it through `subscribe` / `getSnapshot`.

import type { AuthResult, Session, SessionStore } from './session'

export type LoginRole = 'Admin' | 'Partner'

export interface AuthApi {
  login(email: string, password: string, role: LoginRole): Promise<AuthResult>
  refresh(refreshToken: string): Promise<AuthResult>
  /** Needs the live access token (the endpoint is authenticated); the refresh token is the one to revoke. */
  logout(refreshToken: string): Promise<void>
}

export interface Timers {
  setTimeout(handler: () => void, ms: number): unknown
  clearTimeout(id: unknown): void
}

export type AuthStatus = 'loading' | 'authenticated' | 'anonymous'

export interface AuthState {
  status: AuthStatus
  session: Session | null
}

/** Refresh when this share of the access token lifetime has passed (15 min token -> after 12 min). */
export const REFRESH_AT_FRACTION = 0.8
/** Never schedule a refresh sooner than this, so a very short token cannot cause a tight loop. */
export const MIN_REFRESH_DELAY_MS = 5_000

export function refreshDelayMs(expiresInSeconds: number): number {
  return Math.max(MIN_REFRESH_DELAY_MS, Math.floor(expiresInSeconds * 1000 * REFRESH_AT_FRACTION))
}

export function createAuthController(deps: { api: AuthApi; store: SessionStore; timers: Timers }) {
  const { api, store, timers } = deps
  let state: AuthState = { status: 'loading', session: null }
  let timer: unknown = null
  let inFlight: Promise<Session | null> | null = null
  const listeners = new Set<() => void>()

  function publish(next: AuthState) {
    state = next
    listeners.forEach((l) => l())
  }

  function cancelTimer() {
    if (timer !== null) timers.clearTimeout(timer)
    timer = null
  }

  function establish(result: AuthResult): Session {
    const session = store.set(result)
    cancelTimer()
    timer = timers.setTimeout(() => void refresh(), refreshDelayMs(result.accessTokenExpiresInSeconds))
    publish({ status: 'authenticated', session })
    return session
  }

  function drop() {
    cancelTimer()
    store.clear()
    publish({ status: 'anonymous', session: null })
  }

  /**
   * Refresh tokens rotate (the old one stops working), so two parallel refreshes would invalidate each other:
   * every caller shares the one request that is in flight. Any failure ends the session.
   */
  function refresh(): Promise<Session | null> {
    if (inFlight) return inFlight
    const token = store.loadRefreshToken()
    if (!token) {
      drop()
      return Promise.resolve(null)
    }
    inFlight = api
      .refresh(token)
      .then((result) => establish(result))
      .catch(() => {
        drop()
        return null
      })
      .finally(() => {
        inFlight = null
      })
    return inFlight
  }

  return {
    getSnapshot: (): AuthState => state,

    subscribe(listener: () => void): () => void {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },

    getAccessToken: (): string | null => state.session?.accessToken ?? null,

    /** App start: with a refresh token left from before a reload, get a new access token; otherwise anonymous. */
    bootstrap(): Promise<Session | null> {
      if (state.status !== 'loading') return Promise.resolve(state.session)
      return refresh()
    },

    refresh,

    async login(email: string, password: string, role: LoginRole): Promise<Session> {
      // Errors (400/401/403/423) propagate to the form; nothing is stored on failure.
      return establish(await api.login(email, password, role))
    },

    async logout(): Promise<void> {
      const refreshToken = store.loadRefreshToken()
      try {
        if (refreshToken) await api.logout(refreshToken)
      } catch {
        // The local session ends anyway: a failed network call must not keep someone logged in.
      } finally {
        drop()
      }
    },

    /** A 401 on a request that carried a token: the session is no longer valid. */
    handleUnauthorized(): void {
      if (state.status === 'authenticated') drop()
    },
  }
}

export type AuthController = ReturnType<typeof createAuthController>
