import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { CircleAlert, Heart, Loader, Star } from 'lucide-react'
import { ApiError } from '@/services/api'
import { accountApi } from './api'
import type { FavoriteWorker } from './types'
import { accountProblem, canBookWorker, initials, workStatusLabel } from './view'

export function FavoritesPage() {
  const [items, setItems] = useState<FavoriteWorker[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<number | null>(null)

  useEffect(() => {
    let cancelled = false
    accountApi.favorites().then((l) => !cancelled && setItems(l)).catch((e: unknown) => !cancelled && setError(accountProblem(e as ApiError)))
    return () => {
      cancelled = true
    }
  }, [])

  async function remove(workerId: number) {
    if (busyId !== null) return
    setBusyId(workerId)
    setError(null)
    try {
      await accountApi.removeFavorite(workerId)
      setItems((prev) => prev?.filter((w) => w.workerId !== workerId) ?? null)
    } catch (e) {
      setError(accountProblem(e as ApiError))
    } finally {
      setBusyId(null)
    }
  }

  return <div>
    <p className="text-xs font-semibold uppercase tracking-wide text-stone-500">Yêu thích</p>
    <h1 className="mt-1 text-3xl font-semibold">Thợ yêu thích</h1>
    <p className="mt-2 text-stone-500">{items ? `${items.length} thợ bạn đã lưu.` : 'Những thợ bạn đã lưu để đặt lại nhanh.'}</p>
    {error && <p role="alert" className="mt-6 flex gap-2 rounded-lg bg-red-50 p-4 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{error}</p>}
    {!items && !error && <p className="mt-6 flex items-center gap-2 text-stone-500"><Loader className="animate-spin" size={18} />Đang tải…</p>}
    {items && items.length === 0 && <p className="mt-6 rounded-xl border border-stone-200 bg-white p-8 text-center text-stone-500">Bạn chưa lưu thợ nào. Sau một ca hoàn tất, bạn có thể lưu thợ vào danh sách này trong ứng dụng.</p>}
    {items && items.length > 0 && <ul className="mt-6 grid gap-4 md:grid-cols-2">
      {items.map((w) => <li key={w.workerId} className="rounded-xl border border-stone-200 bg-white p-5">
        <div className="flex items-center gap-3">
          <span className="grid h-12 w-12 place-items-center rounded-full bg-stone-200 font-semibold">{initials(w.fullName)}</span>
          <span className="grow"><b>{w.fullName}</b>
            <span className="block text-sm text-stone-500"><Star size={12} className="mr-1 inline fill-amber-400 text-amber-500" />{w.ratingAvg.toFixed(1)} · {w.completedJobs} ca hoàn tất</span></span>
          <span className="rounded-full bg-stone-100 px-3 py-1 text-xs font-semibold">{workStatusLabel(w.workStatus)}</span>
        </div>
        <div className="mt-4 flex gap-3">
          {canBookWorker(w.workStatus)
            ? <Link to={`/app/booking?worker=${w.workerId}`} className="inline-flex h-10 grow items-center justify-center rounded-lg bg-[#1a2f25] px-4 font-semibold text-white">Đặt thợ này</Link>
            : <span className="inline-flex h-10 grow items-center justify-center rounded-lg bg-stone-100 px-4 text-sm text-stone-500">Hiện chưa đặt được</span>}
          <button type="button" disabled={busyId === w.workerId} onClick={() => void remove(w.workerId)} aria-label={`Bỏ ${w.fullName} khỏi danh sách yêu thích`}
            className="flex h-10 items-center gap-2 rounded-lg border border-stone-300 px-3 text-sm font-semibold disabled:opacity-50"><Heart size={16} className="fill-red-500 text-red-500" />Bỏ lưu</button>
        </div>
      </li>)}
    </ul>}
  </div>
}
