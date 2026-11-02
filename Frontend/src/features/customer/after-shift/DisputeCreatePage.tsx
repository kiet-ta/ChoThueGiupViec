import { useEffect, useRef, useState, type ChangeEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { CircleAlert, ImagePlus, Loader, X } from 'lucide-react'
import { ApiError } from '@/services/api'
import { bookingApi } from '../booking/api'
import type { OrderSummary } from '../booking/types'
import { dayText, shiftLabel } from '../booking/view'
import { afterShiftApi } from './api'
import { afterShiftProblem, CATEGORIES, DESCRIPTION_MAX, evidenceFileError, EVIDENCE_MAX, validateDispute, type DisputeErrors } from './view'

interface Evidence {
  url: string
  preview: string
  name: string
}

const toBase64 = (file: File) => new Promise<string>((resolve, reject) => {
  const reader = new FileReader()
  reader.onload = () => resolve(String(reader.result).split(',')[1] ?? '')
  reader.onerror = () => reject(reader.error)
  reader.readAsDataURL(file)
})

export function DisputeCreatePage() {
  const navigate = useNavigate()
  const [search] = useSearchParams()
  const [orders, setOrders] = useState<OrderSummary[]>([])
  const [orderId, setOrderId] = useState<number | null>(Number(search.get('order')) || null)
  const [category, setCategory] = useState('')
  const [description, setDescription] = useState('')
  const [evidence, setEvidence] = useState<Evidence[]>([])
  const [errors, setErrors] = useState<DisputeErrors>({})
  const [banner, setBanner] = useState<string | null>(null)
  const [uploading, setUploading] = useState(false)
  const [busy, setBusy] = useState(false)
  const input = useRef<HTMLInputElement>(null)

  useEffect(() => {
    // A dispute is about an order that has had a worker: completed, awaiting acceptance (assigned) or absence.
    bookingApi.orders(100).then((p) => setOrders(p.items.filter((o) => ['ASSIGNED', 'COMPLETED'].includes(o.orderStatus)))).catch(() => undefined)
  }, [])
  useEffect(() => () => evidence.forEach((e) => URL.revokeObjectURL(e.preview)), [evidence])

  async function pick(e: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(e.target.files ?? [])
    e.target.value = ''
    if (!files.length) return
    setBanner(null)
    const room = EVIDENCE_MAX - evidence.length
    const accepted = files.slice(0, Math.max(0, room))
    const problems = [...files.slice(accepted.length).map(() => `Tối đa ${EVIDENCE_MAX} ảnh.`), ...accepted.map(evidenceFileError).filter(Boolean)] as string[]
    const valid = accepted.filter((f) => evidenceFileError(f) === null)
    if (problems.length) setErrors((prev) => ({ ...prev, evidence: problems[0] }))
    else setErrors((prev) => ({ ...prev, evidence: undefined }))
    if (!valid.length) return
    setUploading(true)
    try {
      const uploaded: Evidence[] = []
      for (const file of valid) {
        const result = await afterShiftApi.uploadEvidence(file.name, file.type, await toBase64(file))
        uploaded.push({ url: result.url, preview: URL.createObjectURL(file), name: file.name })
      }
      setEvidence((prev) => [...prev, ...uploaded])
    } catch (err) {
      setErrors((prev) => ({ ...prev, evidence: afterShiftProblem(err as ApiError) }))
    } finally {
      setUploading(false)
    }
  }

  async function submit() {
    const problems = validateDispute({ category, description, evidenceCount: evidence.length })
    if (orderId === null) setBanner('Hãy chọn đơn cần khiếu nại.')
    setErrors(problems)
    if (orderId === null || Object.keys(problems).length || busy || uploading) return
    setBusy(true)
    setBanner(null)
    try {
      await afterShiftApi.fileDispute({ orderId, category, description: description.trim(), evidenceUrls: evidence.map((e) => e.url) })
      navigate('/app/disputes')
    } catch (err) {
      const e = err as ApiError
      const fields = Object.fromEntries(Object.entries(e.fieldErrors ?? {}).map(([k, v]) => [k.toLowerCase(), v[0]]))
      setErrors({ category: fields.category, description: fields.description, evidence: fields.evidenceurls })
      setBanner(afterShiftProblem(e))
      setBusy(false)
    }
  }

  return <div className="mx-auto max-w-2xl">
    <Link to="/app/disputes" className="text-sm text-stone-500 underline">← Khiếu nại của tôi</Link>
    <h1 className="mt-3 text-3xl font-semibold">Tạo khiếu nại</h1>
    <p className="mt-2 text-stone-500">Khiếu nại trong 24 giờ sau ca. Mỗi đơn chỉ có một khiếu nại.</p>
    <section className="mt-6 space-y-6 rounded-xl border border-stone-200 bg-white p-6">
      <label className="block text-sm font-medium">Đơn
        <select value={orderId ?? ''} onChange={(e) => setOrderId(Number(e.target.value) || null)} className="mt-1 h-11 w-full rounded-lg border border-stone-300 bg-white px-3">
          <option value="">Chọn đơn…</option>
          {orderId !== null && !orders.some((o) => o.orderId === orderId) && <option value={orderId}>Đơn #{orderId}</option>}
          {orders.map((o) => <option key={o.orderId} value={o.orderId}>{o.orderCode} · {dayText(o.scheduledDate)} · {shiftLabel(o.shiftCode)}</option>)}
        </select></label>
      <fieldset><legend className="text-sm font-medium">Vấn đề</legend>
        <div className="mt-2 flex flex-wrap gap-2">{CATEGORIES.map((c) => <button key={c.value} type="button" aria-pressed={category === c.value} onClick={() => { setCategory(c.value); setErrors((p) => ({ ...p, category: undefined })) }}
          className={`h-9 rounded-full border px-4 text-sm font-medium ${category === c.value ? 'border-[#1a2f25] bg-[#1a2f25] text-white' : 'border-stone-300'}`}>{c.label}</button>)}</div>
        {errors.category && <p role="alert" className="mt-1 text-sm text-red-700">{errors.category}</p>}</fieldset>
      <div><label className="block text-sm font-medium">Mô tả
        <textarea value={description} maxLength={DESCRIPTION_MAX} onChange={(e) => { setDescription(e.target.value); setErrors((p) => ({ ...p, description: undefined })) }} aria-invalid={!!errors.description} className="mt-1 min-h-28 w-full rounded-lg border border-stone-300 p-3 text-sm" /></label>
        <p className="mt-1 text-right text-xs text-stone-500">{description.length}/{DESCRIPTION_MAX}</p>
        {errors.description && <p role="alert" className="text-sm text-red-700">{errors.description}</p>}</div>
      <div><p className="text-sm font-medium">Ảnh bằng chứng · {evidence.length}/{EVIDENCE_MAX}</p>
        <div className="mt-2 grid grid-cols-3 gap-3 sm:grid-cols-4">
          {evidence.map((e, i) => <div key={e.url} className="relative aspect-square overflow-hidden rounded-lg border border-stone-200"><img src={e.preview} alt={e.name} className="h-full w-full object-cover" />
            <button type="button" aria-label={`Bỏ ảnh ${e.name}`} onClick={() => setEvidence(evidence.filter((_, j) => j !== i))} className="absolute right-1 top-1 grid h-6 w-6 place-items-center rounded-full bg-white/90"><X size={14} /></button></div>)}
          {evidence.length < EVIDENCE_MAX && <button type="button" disabled={uploading} onClick={() => input.current?.click()} className="flex aspect-square flex-col items-center justify-center gap-1 rounded-lg border border-dashed border-stone-400 text-sm text-stone-600 disabled:opacity-50">
            {uploading ? <Loader className="animate-spin" size={20} /> : <ImagePlus size={20} />}{uploading ? 'Đang tải…' : 'Thêm ảnh'}</button>}
        </div>
        <input ref={input} type="file" accept="image/jpeg,image/png,image/webp" multiple hidden onChange={(e) => void pick(e)} aria-label="Chọn ảnh bằng chứng" />
        {errors.evidence && <p role="alert" className="mt-1 text-sm text-red-700">{errors.evidence}</p>}</div>
      {banner && <p role="alert" className="flex gap-2 rounded-lg bg-red-50 p-3 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{banner}</p>}
      <div className="flex justify-end gap-3">
        <Link to="/app/disputes" className="inline-flex h-10 items-center rounded-lg border border-stone-300 px-4 font-semibold">Hủy</Link>
        <button type="button" disabled={busy || uploading} onClick={() => void submit()} className="flex h-10 items-center gap-2 rounded-lg bg-[#1a2f25] px-4 font-semibold text-white disabled:opacity-50">{busy && <Loader size={16} className="animate-spin" />}Gửi khiếu nại</button>
      </div>
    </section>
  </div>
}
