import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ChevronRight, CircleAlert, Gavel, Heart, Loader, LogOut, MapPin, User } from 'lucide-react'
import { ApiError } from '@/services/api'
import { useAuth } from '@/auth/useAuth'
import { afterShiftApi } from '../after-shift/api'
import { accountApi } from './api'
import type { CustomerProfile } from './types'
import { accountProblem, initials, phoneText } from './view'

export function AccountPage() {
  const { logout } = useAuth()
  const [profile, setProfile] = useState<CustomerProfile | null>(null)
  const [addresses, setAddresses] = useState<number | null>(null)
  const [favorites, setFavorites] = useState<number | null>(null)
  const [openDisputes, setOpenDisputes] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    accountApi.profile().then((p) => !cancelled && setProfile(p)).catch((e: unknown) => !cancelled && setError(accountProblem(e as ApiError)))
    // The counts are extras: a failure leaves the row without a number instead of failing the page.
    accountApi.addresses().then((l) => !cancelled && setAddresses(l.length)).catch(() => undefined)
    accountApi.favorites().then((l) => !cancelled && setFavorites(l.length)).catch(() => undefined)
    afterShiftApi.disputes().then((l) => !cancelled && setOpenDisputes(l.filter((d) => d.disputeStatus !== 'RESOLVED').length)).catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [])

  const rows = [
    { to: '/app/addresses', icon: <MapPin size={18} />, title: 'Địa chỉ', sub: addresses === null ? '' : `${addresses} địa chỉ` },
    { to: '/app/favorites', icon: <Heart size={18} />, title: 'Thợ yêu thích', sub: favorites === null ? '' : `${favorites} thợ` },
    { to: '/app/disputes', icon: <Gavel size={18} />, title: 'Khiếu nại', sub: openDisputes === null ? '' : `${openDisputes} đang xử lý` },
    { to: '/app/profile', icon: <User size={18} />, title: 'Thông tin cá nhân', sub: '' },
  ]
  return <div className="mx-auto max-w-2xl">
    <h1 className="text-3xl font-semibold">Tài khoản</h1>
    {error && <p role="alert" className="mt-6 flex gap-2 rounded-lg bg-red-50 p-4 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{error}</p>}
    {!profile && !error && <p className="mt-6 flex items-center gap-2 text-stone-500"><Loader className="animate-spin" size={18} />Đang tải…</p>}
    {profile && <section className="mt-6 flex items-center gap-4 rounded-xl border border-stone-200 bg-white p-6">
      <span className="grid h-14 w-14 place-items-center rounded-full bg-[#1a2f25] text-lg font-semibold text-white">{initials(profile.fullName)}</span>
      <span className="grow"><b className="text-lg">{profile.fullName || 'Chưa có tên'}</b><span className="block text-sm text-stone-500">{phoneText(profile.phoneNumber)}</span></span>
      <Link to="/app/profile" className="inline-flex h-10 items-center rounded-lg border border-stone-300 px-4 font-semibold">Chỉnh sửa</Link>
    </section>}
    <ul className="mt-4 divide-y divide-stone-100 rounded-xl border border-stone-200 bg-white">
      {rows.map((r) => <li key={r.to}><Link to={r.to} className="flex items-center gap-3 p-5">{r.icon}<span className="grow font-semibold">{r.title}</span>
        {r.sub && <span className="text-sm text-stone-500">{r.sub}</span>}<ChevronRight size={18} /></Link></li>)}
    </ul>
    <button type="button" onClick={() => void logout()} className="mt-6 flex items-center gap-2 font-semibold text-red-700"><LogOut size={18} />Đăng xuất</button>
  </div>
}
