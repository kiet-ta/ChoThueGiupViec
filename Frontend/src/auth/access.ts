// Who may see which area, and where to send the others (WEB-BASE-02). Pure: runs under `node --test`.

export type Area = 'admin' | 'partner'

const AREA_OF_ROLE: Record<string, Area | undefined> = { Admin: 'admin', Partner: 'partner' }

/** The area a role belongs to; the other roles (Customer, Worker) have no page in this web app. */
export function areaOfRole(role: string | null | undefined): Area | null {
  return (role && AREA_OF_ROLE[role]) || null
}

export type AccessDecision =
  | { kind: 'allow' }
  | { kind: 'login'; next: string }
  | { kind: 'redirect'; to: string }

/**
 * `role` is the role of the live session (null = not logged in).
 * Not logged in -> login, remembering where the user wanted to go. Logged in with a role of another area
 * (or without any area here) -> sent to their own area home; that page of the wrong area is never rendered.
 */
export function decideAccess(role: string | null, area: Area, requestedPath: string): AccessDecision {
  if (role === null) return { kind: 'login', next: requestedPath }
  const own = areaOfRole(role)
  if (own === area) return { kind: 'allow' }
  if (own) return { kind: 'redirect', to: `/${own}` }
  return { kind: 'login', next: requestedPath }
}

/**
 * Where to go after a login. `next` comes from the URL, so it is untrusted: only a same-origin path inside
 * the user's own area is accepted, anything else (external URL, `//host`, backslashes, another area) falls
 * back to the area home. This closes the open-redirect hole.
 */
export function safeNext(next: string | null | undefined, role: string): string {
  const area = areaOfRole(role)
  const home = area ? `/${area}` : '/login'
  if (!area || !next) return home
  if (!next.startsWith('/') || next.startsWith('//') || next.includes('\\')) return home
  if ([...next].some((ch) => ch.charCodeAt(0) < 32)) return home
  const path = next.split(/[?#]/)[0]
  if (path !== home && !path.startsWith(`${home}/`)) return home
  return next
}
