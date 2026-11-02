import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { CircleAlert, CircleCheck, Loader } from 'lucide-react'
import { ApiError } from '@/services/api'
import { accountApi } from './api'
import { accountProblem, phoneText, validateProfile, type ProfileErrors } from './view'

const input = 'mt-1 h-11 w-full rounded-lg border border-stone-300 bg-white px-3'

export function ProfilePage() {
  const [phone, setPhone] = useState<string | null>(null)
  const [fullName, setFullName] = useState('')
  const [email, setEmail] = useState('')
  const [errors, setErrors] = useState<ProfileErrors>({})
  const [banner, setBanner] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let cancelled = false
    accountApi.profile()
      .then((p) => {
        if (cancelled) return
        setPhone(p.phoneNumber)
        setFullName(p.fullName)
        setEmail(p.email ?? '')
      })
      .catch((e: unknown) => !cancelled && setBanner(accountProblem(e as ApiError)))
    return () => {
      cancelled = true
    }
  }, [])

  async function save() {
    const problems = validateProfile(fullName, email)
    setErrors(problems)
    setSaved(false)
    if (Object.keys(problems).length || busy) return
    setBusy(true)
    setBanner(null)
    try {
      const p = await accountApi.updateProfile({ fullName: fullName.trim(), email: email.trim() || null })
      setFullName(p.fullName)
      setEmail(p.email ?? '')
      setSaved(true)
    } catch (e) {
      const err = e as ApiError
      const fields = Object.fromEntries(Object.entries(err.fieldErrors ?? {}).map(([k, v]) => [k.toLowerCase(), v[0]]))
      setErrors({ fullName: fields.fullname, email: fields.email })
      setBanner(accountProblem(err))
    } finally {
      setBusy(false)
    }
  }

  return <div className="mx-auto max-w-xl">
    <Link to="/app/account" className="text-sm text-stone-500 underline">← Tài khoản</Link>
    <h1 className="mt-3 text-3xl font-semibold">Thông tin cá nhân</h1>
    {phone === null && !banner && <p className="mt-6 flex items-center gap-2 text-stone-500"><Loader className="animate-spin" size={18} />Đang tải…</p>}
    {phone !== null && <section className="mt-6 space-y-5 rounded-xl border border-stone-200 bg-white p-6">
      <label className="block text-sm font-medium">Họ và tên<input className={input} value={fullName} maxLength={100} onChange={(e) => setFullName(e.target.value)} aria-invalid={!!errors.fullName} autoComplete="name" /></label>
      {errors.fullName && <p role="alert" className="-mt-3 text-sm text-red-700">{errors.fullName}</p>}
      <label className="block text-sm font-medium">Email<input className={input} type="email" value={email} maxLength={255} onChange={(e) => setEmail(e.target.value)} aria-invalid={!!errors.email} autoComplete="email" /></label>
      {errors.email && <p role="alert" className="-mt-3 text-sm text-red-700">{errors.email}</p>}
      <label className="block text-sm font-medium">Số điện thoại<input className={`${input} bg-stone-100 text-stone-500`} value={phoneText(phone)} readOnly disabled /></label>
      <p className="-mt-3 text-xs text-stone-500">Số điện thoại đăng nhập, không sửa tại đây.</p>
      {banner && <p role="alert" className="flex gap-2 rounded-lg bg-red-50 p-3 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{banner}</p>}
      {saved && <p role="status" className="flex gap-2 rounded-lg bg-emerald-50 p-3 text-sm text-emerald-900"><CircleCheck size={16} className="mt-0.5 shrink-0" />Đã lưu thông tin.</p>}
      <div className="flex justify-end gap-3">
        <Link to="/app/account" className="inline-flex h-10 items-center rounded-lg border border-stone-300 px-4 font-semibold">Hủy</Link>
        <button type="button" disabled={busy} onClick={() => void save()} className="flex h-10 items-center gap-2 rounded-lg bg-[#1a2f25] px-4 font-semibold text-white disabled:opacity-50">{busy && <Loader size={16} className="animate-spin" />}Lưu</button>
      </div>
    </section>}
    {phone === null && banner && <p role="alert" className="mt-6 rounded-lg bg-red-50 p-4 text-sm text-red-700">{banner}</p>}
  </div>
}
