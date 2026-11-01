import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Building2, CircleAlert, Home, Loader, Plus } from 'lucide-react'
import { ApiError } from '@/services/api'
import { accountApi } from './api'
import type { Address } from './types'
import { accountProblem, areaText, housingLabel } from './view'

export function AddressesPage() {
  const [items, setItems] = useState<Address[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [confirmId, setConfirmId] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)

  const load = () => accountApi.addresses().then(setItems).catch((e: unknown) => setError(accountProblem(e as ApiError)))
  useEffect(() => {
    let cancelled = false
    accountApi.addresses().then((l) => !cancelled && setItems(l)).catch((e: unknown) => !cancelled && setError(accountProblem(e as ApiError)))
    return () => {
      cancelled = true
    }
  }, [])

  async function remove(id: number) {
    if (busy) return
    setBusy(true)
    setError(null)
    try {
      await accountApi.deleteAddress(id)
      setConfirmId(null)
      await load()
    } catch (e) {
      setError(accountProblem(e as ApiError, true))
      setConfirmId(null)
    } finally {
      setBusy(false)
    }
  }

  return <div>
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div><Link to="/app/account" className="text-sm text-stone-500 underline">← Tài khoản</Link><h1 className="mt-2 text-3xl font-semibold">Địa chỉ của tôi</h1></div>
      <Link to="/app/addresses/new" className="inline-flex h-10 items-center gap-2 rounded-lg bg-[#1a2f25] px-4 font-semibold text-white"><Plus size={16} />Thêm địa chỉ</Link>
    </div>
    {error && <p role="alert" className="mt-6 flex gap-2 rounded-lg bg-red-50 p-4 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{error}</p>}
    {!items && !error && <p className="mt-6 flex items-center gap-2 text-stone-500"><Loader className="animate-spin" size={18} />Đang tải…</p>}
    {items && items.length === 0 && <p className="mt-6 rounded-xl border border-stone-200 bg-white p-8 text-center text-stone-500">Bạn chưa có địa chỉ nào. Thêm địa chỉ để đặt ca.</p>}
    {items && items.length > 0 && <ul className="mt-6 space-y-3">
      {items.map((a) => <li key={a.addressId} className="rounded-xl border border-stone-200 bg-white p-5">
        <div className="flex items-start gap-3">
          <span className="mt-0.5 text-[#1a2f25]">{a.housingType === 'HOUSE' ? <Home size={20} /> : <Building2 size={20} />}</span>
          <span className="grow"><b>{a.label}</b>{a.isDefault && <span className="ml-2 rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-semibold text-emerald-900">Mặc định</span>}
            <span className="block text-sm text-stone-600">{a.addressLine} · {a.district} · {a.city}</span>
            <span className="block text-sm text-stone-500">{housingLabel(a.housingType)} · {areaText(a)}</span></span>
          <span className="flex gap-2">
            <Link to={`/app/addresses/${a.addressId}`} className="inline-flex h-9 items-center rounded-lg border border-stone-300 px-3 text-sm font-semibold">Sửa</Link>
            <button type="button" onClick={() => setConfirmId(a.addressId)} className="h-9 rounded-lg border border-stone-300 px-3 text-sm font-semibold text-red-700">Xóa</button>
          </span>
        </div>
        {confirmId === a.addressId && <div role="group" aria-label="Xác nhận xóa" className="mt-4 flex flex-wrap items-center gap-3 rounded-lg bg-red-50 p-3 text-sm">
          <span className="grow text-red-800">Xóa địa chỉ “{a.label}”?{a.isDefault && ' Địa chỉ mới nhất còn lại sẽ thành mặc định.'}</span>
          <button type="button" disabled={busy} onClick={() => void remove(a.addressId)} className="h-9 rounded-lg bg-red-700 px-4 font-semibold text-white disabled:opacity-50">{busy ? 'Đang xóa…' : 'Xác nhận xóa'}</button>
          <button type="button" disabled={busy} onClick={() => setConfirmId(null)} className="h-9 rounded-lg border border-stone-300 bg-white px-4 font-semibold">Giữ lại</button>
        </div>}
      </li>)}
    </ul>}
  </div>
}
