// Session store for the Admin / Partner web app (WEB-BASE-02, identity.md section 2).
// Pure on purpose (storage and clock are injected): runs under `node --test`.
//
// Where tokens live (decision of this ticket, to be confirmed by the leader):
// - access token: memory only, never written to any storage;
// - refresh token: sessionStorage, so a page reload can get a new access token and the session ends when the
//   tab closes. localStorage is not used: it outlives the tab and any XSS can read both. The backend sets no
//   httpOnly cookie, so a script-readable store is the only option for the refresh token.

/** The four backend roles; this app serves `Admin` and `Partner` (decision D3). */
export type Role = 'Customer' | 'Worker' | 'Partner' | 'Admin'

/** `data` of `/auth/password/login` and `/auth/refresh` (identity.md AuthResult). */
export interface AuthResult {
  tokenType: string
  accessToken: string
  accessTokenExpiresInSeconds: number
  refreshToken: string
  user: { id: number; role: string; isNewUser?: boolean }
}

export interface Session {
  accessToken: string
  /** Epoch milliseconds at which the access token stops being valid. */
  accessExpiresAt: number
  refreshToken: string
  user: { id: number; role: string }
}

export interface KeyValueStorage {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
  removeItem(key: string): void
}

export const REFRESH_TOKEN_KEY = 'giupviec.refreshToken'

export function createSessionStore(storage: KeyValueStorage | null, now: () => number = Date.now) {
  let session: Session | null = null

  function safe<T>(action: () => T, fallback: T): T {
    try {
      return action()
    } catch {
      return fallback // storage can throw (private mode, blocked site data): the app still works in memory
    }
  }

  return {
    get(): Session | null {
      return session
    },

    /** Keeps the tokens of a login/refresh result. Only the refresh token reaches the storage. */
    set(result: AuthResult): Session {
      session = {
        accessToken: result.accessToken,
        accessExpiresAt: now() + result.accessTokenExpiresInSeconds * 1000,
        refreshToken: result.refreshToken,
        user: { id: result.user.id, role: result.user.role },
      }
      safe(() => storage?.setItem(REFRESH_TOKEN_KEY, result.refreshToken), undefined)
      return session
    },

    /** The refresh token that survived a reload, or the one of the live session. */
    loadRefreshToken(): string | null {
      return session?.refreshToken ?? safe(() => storage?.getItem(REFRESH_TOKEN_KEY) ?? null, null)
    },

    clear(): void {
      session = null
      safe(() => storage?.removeItem(REFRESH_TOKEN_KEY), undefined)
    },
  }
}

export type SessionStore = ReturnType<typeof createSessionStore>
