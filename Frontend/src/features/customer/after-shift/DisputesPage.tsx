import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { CircleAlert, Loader, Plus } from 'lucide-react'
import { ApiError } from '@/services/api'
import { formatDateTime } from '@/lib/format'
import { bookingApi } from '../booking/api'
import { afterShiftApi } from './api'
import type { Dispute } from './types'
import { afterShiftProblem, byNewest, categoryLabel, disputeOutcomeLines, disputeStatusLabel } from './view'

const tone: Record<string, string> = { OPEN: 'bg-amber-100 text-amber-900', IN_REVIEW: 'bg-amber-100 text-amber-900', RESOLVED: 'bg-emerald-100 text-emerald-900' }

export function DisputesPage() {
  const [items, setItems] = useState<Dispute[] | null>(null)
  const [codes, setCodes] = useState<Record<number, string>>({})
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    afterShiftApi.disputes().then((d) => !cancelled && setItems([...d].sort(byNewest))).catch((e: unknown) => !cancelled && setError(afterShiftProblem(e as ApiError)))
    // The dispute carries the order id only; the order codes make the list readable.
    bookingApi.orders(100).then((page) => !cancelled && setCodes(Object.fromEntries(page.items.map((o) => [o.orderId, o.orderCode])))).catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [])

  return <div>
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div><p className="text-xs font-semibold uppercase tracking-wide text-stone-500">Hỗ trợ</p><h1 className="mt-1 text-3xl font-semibold">Khiếu nại của tôi</h1></div>
      <Link to="/app/disputes/new" className="inline-flex h-10 items-center gap-2 rounded-lg bg-[#1a2f25] px-4 font-semibold text-white"><Plus size={16} />Tạo khiếu nại</Link>
    </div>
    {error && <p role="alert" className="mt-6 flex gap-2 rounded-lg bg-red-50 p-4 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{error}</p>}
    {!items && !error && <p className="mt-6 flex items-center gap-2 text-stone-500"><Loader className="animate-spin" size={18} />Đang tải…</p>}
    {items && items.length === 0 && <p className="mt-6 rounded-xl border border-stone-200 bg-white p-8 text-center text-stone-500">Bạn chưa gửi khiếu nại nào.</p>}
    {items && items.length > 0 && <div className="mt-6 overflow-x-auto rounded-xl border border-stone-200 bg-white">
      <table className="w-full min-w-[640px] text-left text-sm">
        <thead className="border-b border-stone-200 text-xs uppercase text-stone-500"><tr><th className="px-5 py-3">Mã</th><th className="px-5 py-3">Đơn</th><th className="px-5 py-3">Vấn đề</th><th className="px-5 py-3">Gửi lúc</th><th className="px-5 py-3">Trạng thái</th></tr></thead>
        <tbody>{items.map((d) => <tr key={d.disputeId} className="border-b border-stone-100 last:border-0 align-top">
          <td className="px-5 py-4 font-semibold">#D-{d.disputeId}</td>
          <td className="px-5 py-4"><Link to={`/app/orders/${d.orderId}`} className="underline">{codes[d.orderId] ?? `#${d.orderId}`}</Link></td>
          <td className="px-5 py-4">{categoryLabel(d.category)}<span className="mt-1 block max-w-xs truncate text-xs text-stone-500">{d.description}</span></td>
          <td className="px-5 py-4 tabular-nums">{formatDateTime(d.createdAt)}</td>
          <td className="px-5 py-4"><span className={`rounded-full px-3 py-1 text-xs font-semibold ${tone[d.disputeStatus] ?? 'bg-stone-200'}`}>{disputeStatusLabel(d.disputeStatus)}</span>
            {d.compensationAmount ? <span className="mt-1 block text-xs text-stone-500">Bồi hoàn {d.compensationAmount.toLocaleString('vi-VN')} đ</span> : null}
            {disputeOutcomeLines(d).map((line) => <span key={line} className="mt-1 block text-xs text-stone-500">{line}</span>)}</td>
        </tr>)}</tbody>
      </table>
    </div>}
  </div>
}
