import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { CircleAlert, Home, Loader } from 'lucide-react'
import { ApiError } from '@/services/api'
import { formatVnd } from '@/lib/format'
import { accountApi } from '../account/api'
import { bookingApi } from './api'
import type { BookingOptions, CustomerAddress, FreeShift, PriceQuote, ServiceTier } from './types'
import { bookingProblem, dayText, NOTE_MAX_LENGTH, shiftBlockedReason, shiftLabel, shiftRange, tierLabel, upcomingDays, workersPrice } from './view'

const card = 'rounded-xl border border-stone-200 bg-white p-6'
const choice = (on: boolean, disabled = false) =>
  `flex w-full items-start gap-3 rounded-xl border p-4 text-left ${on ? 'border-[#1a2f25] bg-[#f1f5ef]' : 'border-stone-200 bg-white'} ${disabled ? 'cursor-not-allowed opacity-50' : ''}`
const radio = (on: boolean) => <span className={`mt-1 grid h-4 w-4 shrink-0 place-items-center rounded-full border ${on ? 'border-[#1a2f25]' : 'border-stone-400'}`}>{on && <span className="h-2 w-2 rounded-full bg-[#1a2f25]" />}</span>

export function BookingPage() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const workerParam = Number(params.get('worker')) || null
  const [availability, setAvailability] = useState<{ workerId: number; shifts: FreeShift[] | null; failed: boolean } | null>(null)
  const [favoriteName, setFavoriteName] = useState<string | null>(null)
  const [options, setOptions] = useState<BookingOptions | null>(null)
  const [addresses, setAddresses] = useState<CustomerAddress[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [tier, setTier] = useState<ServiceTier>(params.get('tier') === 'PREMIUM' ? 'PREMIUM' : 'ECONOMY')
  const [addressId, setAddressId] = useState<number | null>(null)
  const [quotes, setQuotes] = useState<Partial<Record<ServiceTier, PriceQuote>>>({})
  const [quoteError, setQuoteError] = useState<string | null>(null)
  const [date, setDate] = useState<string | null>(null)
  const [shiftCode, setShiftCode] = useState<string | null>(null)
  const [note, setNote] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [banner, setBanner] = useState<string | null>(null)
  const [now] = useState(() => new Date())
  const days = useMemo(() => upcomingDays(now), [now])

  useEffect(() => {
    let cancelled = false
    Promise.all([bookingApi.options(), bookingApi.addresses()])
      .then(([opts, list]) => {
        if (cancelled) return
        setOptions(opts)
        setAddresses(list)
        setAddressId((list.find((a) => a.isDefault) ?? list[0])?.addressId ?? null)
      })
      .catch((e: unknown) => !cancelled && setLoadError(e instanceof ApiError ? e.message : 'Không tải được dữ liệu đặt ca.'))
    return () => {
      cancelled = true
    }
  }, [])

  // "Đặt thợ này" from the favorites: show the worker's name (a wish for Economy, ignored for Premium).
  useEffect(() => {
    if (workerParam === null) return
    accountApi.favorites().then((l) => setFavoriteName(l.find((w) => w.workerId === workerParam)?.fullName ?? null)).catch(() => undefined)
  }, [workerParam])

  useEffect(() => {
    if (workerParam === null) return
    let cancelled = false
    bookingApi.availability(workerParam, days[0].date, days[days.length - 1].date)
      .then((shifts) => { if (!cancelled) setAvailability({ workerId: workerParam, shifts, failed: false }) })
      .catch(() => { if (!cancelled) setAvailability({ workerId: workerParam, shifts: null, failed: true }) })
    return () => { cancelled = true }
  }, [workerParam, days])

  // The server price per tier for the chosen address (the prices in the prototype are samples).
  useEffect(() => {
    if (addressId === null) return
    let cancelled = false
    for (const t of ['ECONOMY', 'PREMIUM'] as ServiceTier[]) {
      bookingApi
        .quote(addressId, t)
        .then((q) => !cancelled && setQuotes((prev) => ({ ...prev, [t]: q })))
        .catch((e: unknown) => !cancelled && setQuoteError(bookingProblem(e as ApiError)))
    }
    return () => {
      cancelled = true
    }
  }, [addressId])

  const workerAvailability = availability?.workerId === workerParam ? availability : null
  const address = addresses.find((a) => a.addressId === addressId) ?? null
  const quote = quotes[tier] ?? null
  const shift = options?.shifts.find((s) => s.shiftCode === shiftCode) ?? null
  const blocked = (d: string, code: string) => {
    const s = options?.shifts.find((x) => x.shiftCode === code)
    return options && s ? shiftBlockedReason(d, s, tier, options, now, workerAvailability?.shifts ?? null) : null
  }
  // A choice made for Economy may be invalid for Premium: drop it instead of sending it.
  const selectionValid = date !== null && shiftCode !== null && blocked(date, shiftCode) === null
  const canSubmit = !submitting && addressId !== null && quote !== null && selectionValid

  async function submit() {
    if (!canSubmit || date === null || shiftCode === null || addressId === null) return
    setSubmitting(true)
    setBanner(null)
    try {
      const order = await bookingApi.createOrder({ addressId, serviceTier: tier, scheduledDate: date, shiftCode, customerNote: note.trim() || null, preferredWorkerId: tier === 'ECONOMY' ? workerParam : null })
      navigate(`/app/payments/${order.orderId}`)
    } catch (e) {
      setBanner(bookingProblem(e as ApiError))
      setSubmitting(false)
    }
  }

  if (loadError) return <p role="alert" className="rounded-lg bg-red-50 p-4 text-sm text-red-700"><CircleAlert className="mr-2 inline" size={16} />{loadError}</p>
  if (!options) return <p className="flex items-center gap-2 text-stone-500"><Loader className="animate-spin" size={18} />Đang tải…</p>

  return <div>
    <p className="text-xs font-semibold uppercase tracking-wide text-stone-500">Đặt ca</p>
    <h1 className="mt-1 text-3xl font-semibold">Đặt ca dọn nhà</h1>
    <p className="mt-2 text-stone-500">Chọn gói, địa chỉ và khung giờ. Giá lấy từ máy chủ theo diện tích.</p>
    <div className="mt-8 grid gap-6 lg:grid-cols-[1fr_360px]">
      <div className="space-y-4">
        <section className={card}>
          <h2 className="text-lg font-semibold">1. Chọn gói</h2>
          <div className="mt-4 grid gap-3 sm:grid-cols-2">
            {options.serviceTiers.map((t) => <button key={t} type="button" aria-pressed={tier === t} className={choice(tier === t)} onClick={() => setTier(t)}>
              {radio(tier === t)}
              <span><b>{tierLabel(t)}</b><span className="mt-1 block text-sm text-stone-500">Giá mỗi thợ, mỗi ca</span>
                <span className="mt-2 block text-xl font-bold">{quotes[t] ? formatVnd(quotes[t].unitPrice) : '—'}</span></span>
            </button>)}
          </div>
          <p className="mt-3 text-sm text-stone-500">Gói Premium đặt trước tối thiểu {options.premiumMinLeadHours} giờ.</p>
          {workerParam !== null && <p className="mt-3 rounded-lg bg-emerald-50 p-3 text-sm text-emerald-900">{tier === 'ECONOMY' ? <>Ưu tiên thợ <b>{favoriteName ?? `#${workerParam}`}</b>. Đây là mong muốn, hệ thống sẽ hỏi thợ này trước nếu thợ rảnh ca bạn chọn.{workerAvailability?.shifts?.length === 0 && <span className="mt-2 block">Thợ không có ca rảnh trong 7 ngày tới. Bạn vẫn đặt được ca.</span>}</> : 'Chọn thợ cụ thể chỉ áp dụng cho gói Economy.'}</p>}
          {workerParam !== null && tier === 'ECONOMY' && workerAvailability?.failed && <p role="alert" className="mt-3 text-sm text-stone-500">Không xem được lịch của thợ này, bạn vẫn đặt được ca.</p>}
          {quoteError && <p role="alert" className="mt-3 text-sm text-red-700">{quoteError}</p>}
        </section>
        <section className={card}>
          <h2 className="text-lg font-semibold">2. Địa chỉ</h2>
          {addresses.length === 0
            ? <p className="mt-4 text-sm text-stone-500">Bạn chưa có địa chỉ nào. Hãy thêm địa chỉ trong ứng dụng TỔ ẤM rồi quay lại.</p>
            : <div className="mt-4 space-y-3">{addresses.map((a) => <button key={a.addressId} type="button" aria-pressed={addressId === a.addressId} className={choice(addressId === a.addressId)} onClick={() => { if (addressId !== a.addressId) { setQuotes({}); setQuoteError(null); setAddressId(a.addressId) } }}>
              {radio(addressId === a.addressId)}<Home size={18} className="mt-0.5 text-[#1a2f25]" />
              <span><b>{a.label}</b><span className="block text-sm text-stone-500">{a.addressLine} · {a.district} · {a.totalAreaM2} m²</span></span>
            </button>)}</div>}
        </section>
        <section className={card}>
          <h2 className="text-lg font-semibold">3. Ngày và ca</h2>
          <div className="mt-4 flex gap-2 overflow-x-auto" role="group" aria-label="Chọn ngày">
            {days.map((d) => <button key={d.date} type="button" aria-pressed={date === d.date} onClick={() => setDate(d.date)}
              className={`flex h-16 min-w-16 flex-col items-center justify-center rounded-lg border ${date === d.date ? 'border-[#1a2f25] bg-[#1a2f25] text-white' : 'border-stone-200 bg-white'}`}>
              <span className="text-xs">{d.weekday}</span><b className="text-lg">{d.day}</b></button>)}
          </div>
          <div className="mt-4 grid gap-3 sm:grid-cols-3" role="group" aria-label="Chọn ca">
            {options.shifts.map((s) => {
              const reason = date ? blocked(date, s.shiftCode) : null
              const on = shiftCode === s.shiftCode && !reason
              return <button key={s.shiftCode} type="button" disabled={!date || reason !== null} aria-pressed={on} className={choice(on, !date || reason !== null)} onClick={() => setShiftCode(s.shiftCode)}>
                {radio(on)}<span><b>{shiftRange(s)}</b><span className="block text-sm text-stone-500">{reason ?? shiftLabel(s.shiftCode)}</span></span></button>
            })}
          </div>
          {!date && <p className="mt-3 text-sm text-stone-500">Chọn ngày trước để xem các ca còn đặt được.</p>}
        </section>
        <section className={card}>
          <h2 className="text-lg font-semibold">4. Ghi chú cho thợ</h2>
          <textarea value={note} maxLength={NOTE_MAX_LENGTH} onChange={(e) => setNote(e.target.value)} aria-label="Ghi chú cho thợ" placeholder="Có mèo, chìa khóa ở bảo vệ…"
            className="mt-4 min-h-24 w-full rounded-lg border border-stone-300 p-3 text-sm" />
          <p className="mt-1 text-right text-xs text-stone-500">{note.length}/{NOTE_MAX_LENGTH}</p>
        </section>
      </div>
      <aside className={`${card} h-fit lg:sticky lg:top-24`}>
        <h2 className="text-lg font-semibold">Tóm tắt đơn</h2>
        <dl className="mt-4 space-y-3 text-sm">
          <div className="flex justify-between gap-4"><dt className="text-stone-500">Gói</dt><dd className="font-semibold">{tierLabel(tier)}</dd></div>
          <div className="flex justify-between gap-4"><dt className="text-stone-500">Thời gian</dt><dd className="text-right font-semibold">{date && shift && selectionValid ? `${dayText(date)} · ${shiftRange(shift)}` : '—'}</dd></div>
          <div className="flex justify-between gap-4"><dt className="text-stone-500">Địa chỉ</dt><dd className="text-right font-semibold">{address?.label ?? '—'}</dd></div>
          <div className="flex justify-between gap-4"><dt className="text-stone-500">Số thợ</dt><dd className="font-semibold">{quote ? workersPrice(quote.requiredWorkers, quote.unitPrice) : '—'}</dd></div>
        </dl>
        <hr className="my-4 border-stone-200" />
        <div className="flex items-baseline justify-between"><b>Tổng cộng</b><b className="text-2xl">{quote ? formatVnd(quote.totalAmount) : '—'}</b></div>
        {banner && <p role="alert" className="mt-4 flex gap-2 rounded-lg bg-red-50 p-3 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{banner}</p>}
        {!canSubmit && !submitting && <p className="mt-4 text-sm text-stone-500">{addressId === null ? 'Chọn địa chỉ để xem giá.' : !selectionValid ? 'Chọn ngày và ca để tiếp tục.' : 'Đang tính giá…'}</p>}
        <button type="button" disabled={!canSubmit} onClick={() => void submit()}
          className="mt-4 flex h-11 w-full items-center justify-center gap-2 rounded-lg bg-[#1a2f25] font-semibold text-white disabled:opacity-50">
          {submitting && <Loader className="animate-spin" size={16} />}Đặt và thanh toán</button>
        <Link to="/app" className="mt-3 block text-center text-sm text-stone-500">Quay lại trang chủ</Link>
      </aside>
    </div>
  </div>
}
