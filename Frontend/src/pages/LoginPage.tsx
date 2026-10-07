import { useState, type FormEvent } from 'react'
import { Navigate, useSearchParams } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { safeNext } from '@/auth/access'
import type { LoginRole } from '@/auth/auth-controller'
import { describeLoginError, type LoginErrorView } from '@/auth/login-errors'
import { useAuth } from '@/auth/useAuth'
import { ApiError } from '@/services/api'

const ROLES: { value: LoginRole; label: string }[] = [
  { value: 'Admin', label: 'Quản trị viên (Admin)' },
  { value: 'Partner', label: 'Đối tác doanh nghiệp (Partner)' },
]

const INPUT =
  'h-10 w-full rounded-lg border border-input bg-background px-3 text-sm outline-none focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive'

export function LoginPage() {
  const { status, session, login } = useAuth()
  const [params] = useSearchParams()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState<LoginRole>('Admin')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<LoginErrorView | null>(null)

  // Already logged in: go where they were heading (validated) or to their own area.
  if (status === 'authenticated' && session) {
    return <Navigate to={safeNext(params.get('next'), session.user.role)} replace />
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setSubmitting(true)
    setError(null)
    try {
      await login(email.trim(), password, role)
      // The redirect happens through the branch above once the session exists.
    } catch (err) {
      setError(describeLoginError(err instanceof ApiError ? err : { status: -1 }))
      setPassword('') // a failed password is never kept in the form
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-muted/40 p-6">
      <form
        onSubmit={onSubmit}
        noValidate
        className="flex w-full max-w-sm flex-col gap-4 rounded-xl border border-border bg-card p-6 text-card-foreground"
      >
        <h1 className="text-xl font-semibold">Đăng nhập GiupViec</h1>

        <label className="flex flex-col gap-1 text-sm">
          Vai trò
          <select className={INPUT} value={role} onChange={(e) => setRole(e.target.value as LoginRole)}>
            {ROLES.map((r) => (
              <option key={r.value} value={r.value}>
                {r.label}
              </option>
            ))}
          </select>
        </label>

        <label className="flex flex-col gap-1 text-sm">
          Email
          <input
            className={INPUT}
            type="email"
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            aria-invalid={!!error?.fields.email}
            required
          />
          {error?.fields.email?.map((m) => (
            <span key={m} className="text-xs text-destructive">
              {m}
            </span>
          ))}
        </label>

        <label className="flex flex-col gap-1 text-sm">
          Mật khẩu
          <input
            className={INPUT}
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aria-invalid={!!error?.fields.password}
            required
          />
          {error?.fields.password?.map((m) => (
            <span key={m} className="text-xs text-destructive">
              {m}
            </span>
          ))}
        </label>

        {error?.message && (
          <p role="alert" className="text-sm text-destructive">
            {error.message}
          </p>
        )}

        <Button type="submit" disabled={submitting || status === 'loading'}>
          {submitting ? 'Đang đăng nhập…' : 'Đăng nhập'}
        </Button>
      </form>
    </main>
  )
}
