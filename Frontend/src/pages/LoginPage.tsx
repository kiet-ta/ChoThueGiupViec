import { useEffect, useState, type FormEvent } from 'react'
import { Link, Navigate, useSearchParams } from 'react-router-dom'
import { House, ShieldCheck } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { OtpInput } from '@/components/ui/otp-input'
import { safeNext } from '@/auth/access'
import { authController } from '@/auth'
import { describeLoginError, describeOtpError, type LoginErrorView } from '@/auth/login-errors'
import { useAuth } from '@/auth/useAuth'
import { apiClient, ApiError } from '@/services/api'
import type { AuthResult } from '@/auth/session'

const input = 'h-12 w-full rounded-lg border border-stone-300 bg-white px-4 text-sm outline-none focus-visible:ring-2 focus-visible:ring-emerald-800'
export function LoginPage({ admin = false }: { admin?: boolean }) {
  const { status, session, login } = useAuth()
  const [params] = useSearchParams()
  const [tab, setTab] = useState<'customer' | 'partner'>('customer')
  const [phone, setPhone] = useState('')
  const [code, setCode] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [step, setStep] = useState<'phone' | 'code'>('phone')
  const [resendAt, setResendAt] = useState(0)
  const [clock, setClock] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<LoginErrorView | null>(null)
  const remaining = Math.max(0, Math.ceil((resendAt - clock) / 1000))
  useEffect(() => {
    if (!resendAt) return
    const timer = window.setInterval(() => setClock(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [resendAt])
  if (status === 'authenticated' && session) return <Navigate to={safeNext(params.get('next'), session.user.role)} replace />
  function countdown(seconds: number) {
    const end = Date.now() + seconds * 1000
    setResendAt(end); setClock(Date.now())
  }
  async function requestOtp() {
    const result = await apiClient.post<{ expiresInSeconds: number; resendAvailableInSeconds: number }>('/auth/otp/request', { phoneNumber: phone.trim(), role: 'Customer' })
    countdown(result.resendAvailableInSeconds); setStep('code'); setCode('')
  }
  async function submit(e: FormEvent) {
    e.preventDefault(); setBusy(true); setError(null)
    try {
      if (admin || tab === 'partner') await login(email.trim(), password, admin ? 'Admin' : 'Partner')
      else if (step === 'phone') await requestOtp()
      else {
        const result = await apiClient.post<AuthResult>('/auth/otp/verify', { phoneNumber: phone.trim(), role: 'Customer', code })
        authController.establishOtp(result)
      }
    } catch (err) {
      const failure = err instanceof ApiError ? err : { status: 0 }
      setError(admin || tab === 'partner' ? describeLoginError(failure) : describeOtpError(failure, step))
      if (admin || tab === 'partner') setPassword('')
      if (failure instanceof ApiError && failure.status === 429 && failure.retryAfterSeconds) countdown(failure.retryAfterSeconds)
    } finally { setBusy(false) }
  }
  const field = (name: string) => error?.fields[name]?.map(message => <span key={message} className="text-xs text-red-700">{message}</span>)
  return <main className="grid min-h-screen bg-[#f8f8f4] lg:grid-cols-2">
    <section className={`relative hidden min-h-screen flex-col justify-between overflow-hidden p-14 text-white lg:flex ${admin ? 'bg-[#1a2f25]' : 'bg-[#1a2f25]'}`} style={admin ? undefined : { backgroundImage: 'linear-gradient(0deg, rgba(18,38,28,.8), rgba(18,38,28,.15)), url(https://images.unsplash.com/photo-1613490493576-7fde63acd811?auto=format&fit=crop&w=1400&q=70)', backgroundSize: 'cover', backgroundPosition: 'center' }}>
      <Link to="/" className="flex items-center gap-3 text-xl font-bold"><House /> TỔ ẤM {admin && <span className="rounded-full bg-white/15 px-3 py-1 text-xs">Admin Console</span>}</Link>
      <div className="max-w-lg pb-12">{admin && <ShieldCheck className="mb-5 h-12 w-12" />}<h2 className="text-5xl font-semibold leading-tight">{admin ? 'Khu vực quản trị nội bộ.' : 'Nhà sạch, cuối tuần là của bạn.'}</h2><p className="mt-5 text-white/80">{admin ? 'Chỉ dành cho nhân sự được cấp tài khoản Admin. Mọi thao tác đều ghi vào nhật ký kiểm toán.' : 'Đặt thợ dọn nhà theo giờ, giá cố định, nghiệm thu rồi mới thanh toán.'}</p></div>
    </section>
    <section className="flex items-center justify-center px-6 py-12"><div className="w-full max-w-md">
      <Link to="/" className="mb-10 flex items-center gap-2 font-bold text-[#1a2f25] lg:hidden"><House /> TỔ ẤM</Link>
      {admin && <span className="rounded-full bg-[#1a2f25] px-3 py-1 text-xs text-white">Quản trị viên</span>}
      <h1 className="mt-4 text-3xl font-semibold tracking-tight text-[#1a2f25]">{admin ? 'Đăng nhập quản trị' : 'Đăng nhập hoặc đăng ký'}</h1>
      <p className="mt-3 text-sm text-stone-500">{admin ? 'Dùng email Admin được cấp.' : tab === 'customer' ? 'Nhập số điện thoại, mã 6 số sẽ gửi qua SMS.' : 'Đăng nhập tài khoản công ty cung cấp thợ.'}</p>
      {!admin && <div className="mt-8 flex rounded-lg bg-stone-200 p-1" role="tablist" aria-label="Loại tài khoản">{(['customer','partner'] as const).map(t => <button key={t} type="button" role="tab" aria-selected={tab === t} onClick={() => { setTab(t); setError(null) }} className={`flex-1 rounded-md px-3 py-2 text-sm font-medium ${tab === t ? 'bg-white text-[#1a2f25] shadow-sm' : 'text-stone-600'}`}>{t === 'customer' ? 'Khách hàng' : 'Đối tác'}</button>)}</div>}
      <form onSubmit={submit} noValidate className="mt-8 flex flex-col gap-5">
        {admin || tab === 'partner' ? <><label className="flex flex-col gap-2 text-sm font-medium">Email<input className={input} type="email" autoComplete="username" value={email} onChange={e => setEmail(e.target.value)} aria-invalid={!!error?.fields.email} />{field('email')}</label><label className="flex flex-col gap-2 text-sm font-medium">Mật khẩu<input className={input} type="password" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} aria-invalid={!!error?.fields.password} />{field('password')}</label></> : step === 'phone' ? <label className="flex flex-col gap-2 text-sm font-medium">Số điện thoại<input className={input} type="tel" autoComplete="tel" value={phone} onChange={e => setPhone(e.target.value)} aria-invalid={!!error?.fields.phonenumber} placeholder="0912 345 678" />{field('phonenumber')}</label> : (
          <div className="flex flex-col gap-4">
            <p className="text-sm text-stone-600">
              Nhập mã 6 số đã gửi đến <span className="font-semibold text-stone-900">{phone}</span>.{' '}
              <button
                type="button"
                className="font-medium text-[#1a2f25] underline hover:text-[#284b39]"
                onClick={() => {
                  setStep('phone')
                  setError(null)
                }}
              >
                Thay đổi
              </button>
            </p>
            <div className="flex flex-col gap-2">
              <label className="text-sm font-medium text-stone-800">Mã xác nhận</label>
              <OtpInput
                value={code}
                onChange={setCode}
                disabled={busy}
                invalid={!!error?.fields.code}
              />
              {field('code')}
            </div>
          </div>
        )}
        {error?.message && <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-700">{error.message}</p>}
        <Button type="submit" disabled={busy || status === 'loading'} className="h-12 rounded-lg bg-[#1a2f25] text-white hover:bg-[#284b39]">{busy ? 'Đang xử lý…' : admin || tab === 'partner' ? 'Đăng nhập' : step === 'phone' ? 'Nhận mã' : 'Xác nhận'}</Button>
        {!admin && tab === 'customer' && step === 'code' && <button type="button" disabled={busy || remaining > 0} onClick={async () => { setBusy(true); setError(null); try { await requestOtp() } catch (e) { const err = e instanceof ApiError ? e : { status: 0 }; setError(describeOtpError(err, 'phone')); if (err instanceof ApiError && err.retryAfterSeconds) countdown(err.retryAfterSeconds) } finally { setBusy(false) } }} className="text-sm text-[#1a2f25] underline disabled:text-stone-400">{remaining ? `Gửi lại mã sau ${remaining} giây` : 'Gửi lại mã'}</button>}
      </form>
      {!admin && tab === 'partner' && <p className="mt-7 text-sm text-stone-500">Công ty chưa có tài khoản? <Link to="/register" className="font-semibold text-[#1a2f25] underline">Đăng ký đối tác</Link></p>}
      {!admin && <p className="mt-7 border-t border-stone-200 pt-6 text-sm text-stone-500">Là thợ làm việc tự do? Hãy dùng ứng dụng TỔ ẤM trên điện thoại.</p>}
      {admin && <p className="mt-7 text-xs text-stone-500">Trang này chỉ đăng nhập với vai trò Admin. Khách hàng và đối tác dùng trang đăng nhập riêng của họ.</p>}
    </div></section>
  </main>
}
