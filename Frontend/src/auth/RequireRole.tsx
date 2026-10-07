import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { decideAccess, type Area } from './access'
import { useAuth } from './useAuth'

/** Wraps an area: nothing of it renders until the session is known and the role fits the area. */
export function RequireRole({ area, children }: { area: Area; children: ReactNode }) {
  const { status, session } = useAuth()
  const location = useLocation()

  if (status === 'loading') {
    return (
      <div className="flex min-h-screen items-center justify-center text-sm text-muted-foreground" role="status">
        Đang kiểm tra phiên đăng nhập…
      </div>
    )
  }

  const decision = decideAccess(session?.user.role ?? null, area, location.pathname + location.search)
  if (decision.kind === 'allow') return <>{children}</>
  if (decision.kind === 'redirect') return <Navigate to={decision.to} replace />
  return <Navigate to={`/login?next=${encodeURIComponent(decision.next)}`} replace />
}
