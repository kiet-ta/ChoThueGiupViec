// Pure part of AuthImage (no React, no import.meta), so `node --test` covers it.

/** The prefix every protected address of this API starts with (the server sends addresses like `/api/workers/assignments/1/photos/2/file`). */
const API_PREFIX = '/api/'

/**
 * True for an address that answers only to a Bearer token: an `<img src>` cannot send one, so such a photo is fetched through the API
 * client and shown from a blob. Anything else (an `https://` photo, `/files/dispute-evidence/...` which is anonymous on purpose) stays a
 * plain `<img src>`.
 */
export function needsAuthenticatedFetch(url: string): boolean {
  return url.startsWith(API_PREFIX)
}

/** The path the API client wants: it adds the `/api` base itself (and `VITE_API_BASE_URL` when the API is on another origin). */
export function apiClientPath(url: string): string {
  return url.slice(API_PREFIX.length - 1)
}
