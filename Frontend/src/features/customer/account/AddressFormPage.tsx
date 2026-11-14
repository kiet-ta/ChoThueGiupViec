import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { CircleAlert, LocateFixed, Loader, Ruler } from 'lucide-react'
import { ApiError } from '@/services/api'
import { accountApi } from './api'
import { AddressMap } from './AddressMap'
import { SearchSelect } from './SearchSelect'
import {
  accountProblem, bandText, DEFAULT_LATITUDE, DEFAULT_LONGITUDE, fillFromGeocode, formFromAddress, HOUSING_TYPES, latestRequest, parseNumber, placeText,
  roundCoordinate, toAddressBody, totalArea, safeAddressNext, validateAddress, wardsOf, type AddressErrors, type AddressForm,
} from './view'

const input = 'mt-1 h-11 w-full rounded-lg border border-stone-300 bg-white px-3'
const blank: AddressForm = {
  label: '', city: 'Thành phố Hồ Chí Minh', district: '', addressLine: '', housingType: 'APARTMENT', floorAreaM2: '', numFloors: '1',
  isDefault: false, latitude: DEFAULT_LATITUDE, longitude: DEFAULT_LONGITUDE,
}

export function AddressFormPage() {
  const id = Number(useParams().addressId) || null
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [units, setUnits] = useState<Record<string, string[]> | null>(null)
  const [form, setForm] = useState<AddressForm>(blank)
  const [loaded, setLoaded] = useState(id === null)
  const [errors, setErrors] = useState<AddressErrors>({})
  const [banner, setBanner] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [locating, setLocating] = useState(false)
  // ADDR-MAP-02: a saved address has its point; a new one waits for a click on the map.
  const [hasPoint, setHasPoint] = useState(id !== null)
  const [reading, setReading] = useState(false)
  const [mapNote, setMapNote] = useState<{ ok: boolean; text: string } | null>(null)
  const [focus, setFocus] = useState(0)
  const latest = useRef(latestRequest())

  useEffect(() => {
    // The ward catalogue (about 70 KB) is loaded only on this screen.
    import('./vn-admin-units').then((m) => setUnits(m.vnAdminUnits)).catch(() => setBanner('Không tải được danh sách phường / xã.'))
  }, [])
  useEffect(() => {
    if (id === null) return
    let cancelled = false
    accountApi.address(id).then((a) => {
      if (cancelled) return
      setForm(formFromAddress(a))
      setLoaded(true)
    }).catch((e: unknown) => !cancelled && setBanner(accountProblem(e as ApiError)))
    return () => {
      cancelled = true
    }
  }, [id])

  // A field's error goes away as soon as the field is edited (the next save validates again).
  const clear = (...keys: (keyof AddressErrors)[]) => setErrors((prev) => Object.fromEntries(Object.entries(prev).filter(([k]) => !keys.includes(k as keyof AddressErrors))) as AddressErrors)
  const set = <K extends keyof AddressForm>(key: K, value: AddressForm[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }))
    clear(key === 'isDefault' ? 'location' : (key as keyof AddressErrors))
  }
  const total = totalArea(parseNumber(form.floorAreaM2), parseNumber(form.numFloors))
  const wards = units ? wardsOf(units, form.city) : []

  /** A click on the map (or "Vị trí của tôi"): the pin moves there and the address read there fills the form. */
  async function pick(latitude: number, longitude: number) {
    const lat = roundCoordinate(latitude)
    const lng = roundCoordinate(longitude)
    const ticket = latest.current.next()
    setForm((prev) => ({ ...prev, latitude: lat, longitude: lng }))
    setHasPoint(true)
    clear('location')
    setReading(true)
    setMapNote(null)
    try {
      const res = await accountApi.reverseGeocode(lat, lng)
      if (!latest.current.isLatest(ticket)) return
      setForm((prev) => {
        if (!units) return { ...prev, addressLine: res.addressLine.trim() || prev.addressLine }
        const fill = fillFromGeocode(units, res, prev.city)
        return { ...prev, addressLine: fill.addressLine ?? prev.addressLine, city: fill.city, district: fill.district }
      })
      clear('city', 'district', 'addressLine')
      setMapNote({ ok: true, text: placeText(res) })
    } catch {
      if (latest.current.isLatest(ticket)) setMapNote({ ok: false, text: 'Không đọc được địa chỉ ở điểm này. Bạn có thể nhập tay.' })
    } finally {
      if (latest.current.isLatest(ticket)) setReading(false)
    }
  }

  function locateMe() {
    if (!('geolocation' in navigator)) return setMapNote({ ok: false, text: 'Trình duyệt không hỗ trợ định vị. Hãy bấm vào nhà bạn trên bản đồ.' })
    setLocating(true)
    setMapNote(null)
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        setLocating(false)
        setFocus((n) => n + 1)
        void pick(pos.coords.latitude, pos.coords.longitude)
      },
      () => {
        setLocating(false)
        setMapNote({ ok: false, text: 'Không lấy được vị trí của bạn. Hãy cho phép trình duyệt dùng vị trí, hoặc bấm vào nhà bạn trên bản đồ.' })
      },
      { timeout: 8000 },
    )
  }

  async function save() {
    const problems = validateAddress(form)
    setErrors(problems)
    if (Object.keys(problems).length || busy) return
    setBusy(true)
    setBanner(null)
    try {
      const body = toAddressBody(form)
      if (id === null) {
        const created = await accountApi.createAddress(body)
        const next = safeAddressNext(params.get('next'))
        if (next) {
          const url = new URL(next, 'https://example.invalid')
          url.searchParams.set('address', String(created.addressId))
          navigate(url.pathname + url.search + url.hash)
        } else navigate('/app/addresses')
      } else {
        await accountApi.updateAddress(id, body)
        navigate('/app/addresses')
      }
    } catch (e) {
      const err = e as ApiError
      const f = Object.fromEntries(Object.entries(err.fieldErrors ?? {}).map(([k, v]) => [k.toLowerCase(), v[0]]))
      setErrors({ label: f.label, addressLine: f.addressline, district: f.district, city: f.city, floorAreaM2: f.flooraream2, numFloors: f.numfloors, location: f.latitude ?? f.longitude })
      setBanner(accountProblem(err))
      setBusy(false)
    }
  }

  if (!loaded) return banner ? <p role="alert" className="rounded-lg bg-red-50 p-4 text-sm text-red-700">{banner}</p> : <p className="flex items-center gap-2 text-stone-500"><Loader className="animate-spin" size={18} />Đang tải…</p>

  const err = (key: keyof AddressErrors) => errors[key] && <p role="alert" className="mt-1 text-sm text-red-700">{errors[key]}</p>
  return <div className="mx-auto max-w-2xl">
    <Link to="/app/addresses" className="text-sm text-stone-500 underline">← Địa chỉ của tôi</Link>
    <h1 className="mt-3 text-3xl font-semibold">{id === null ? 'Thêm địa chỉ' : 'Sửa địa chỉ'}</h1>
    <section className="mt-6 space-y-5 rounded-xl border border-stone-200 bg-white p-6">
      <div><label className="block text-sm font-medium">Tên gợi nhớ<input className={input} value={form.label} maxLength={50} onChange={(e) => set('label', e.target.value)} aria-invalid={!!errors.label} placeholder="Nhà, Văn phòng…" /></label>{err('label')}</div>
      <div>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="text-sm font-medium">Vị trí nhà trên bản đồ</p>
          <button type="button" onClick={locateMe} disabled={locating} className="flex h-9 items-center gap-1.5 rounded-lg border border-stone-300 px-3 text-sm font-semibold disabled:opacity-50"><LocateFixed size={16} />{locating ? 'Đang định vị…' : 'Vị trí của tôi'}</button>
        </div>
        <p className="mt-1 text-sm text-stone-500">Chạm vào nhà bạn trên bản đồ, địa chỉ sẽ tự điền. Bạn vẫn sửa được từng ô bên dưới.</p>
        <div data-testid="address-map" className="mt-2 h-72 overflow-hidden rounded-lg border border-stone-300">
          <AddressMap latitude={form.latitude} longitude={form.longitude} hasPoint={hasPoint} focus={focus} onPick={(lat, lng) => void pick(lat, lng)} />
        </div>
        <p data-testid="address-map-note" aria-live="polite" className={`mt-2 text-sm ${mapNote && !mapNote.ok ? 'text-red-700' : 'text-stone-600'}`}>
          {reading ? 'Đang tìm địa chỉ…' : mapNote ? (mapNote.ok ? `Đã điền theo bản đồ: ${mapNote.text}. Hãy thêm số nhà nếu còn thiếu.` : mapNote.text) : hasPoint ? 'Đã chọn vị trí. Bấm chỗ khác để đổi.' : 'Chưa chọn vị trí trên bản đồ.'}
        </p>
        {err('location')}
      </div>
      <div className="grid gap-5 sm:grid-cols-2">
        <div><SearchSelect label="Tỉnh / Thành phố" value={form.city} options={units ? Object.keys(units) : []} onChange={(city) => { setForm((p) => ({ ...p, city, district: '' })); clear('city', 'district') }} onEdit={() => clear('city')} invalid={!!errors.city} />{err('city')}</div>
        <div><SearchSelect label="Phường / Xã" value={form.district} options={form.district && !wards.includes(form.district) ? [form.district, ...wards] : wards} onChange={(district) => set('district', district)} onEdit={() => clear('district')} invalid={!!errors.district} disabled={!form.city} />{err('district')}</div>
      </div>
      <div><label className="block text-sm font-medium">Số nhà, đường<input className={input} value={form.addressLine} maxLength={255} onChange={(e) => set('addressLine', e.target.value)} aria-invalid={!!errors.addressLine} autoComplete="street-address" /></label>{err('addressLine')}</div>
      <fieldset><legend className="text-sm font-medium">Loại nhà</legend>
        <div className="mt-2 flex flex-wrap gap-2">{HOUSING_TYPES.map((t) => <button key={t.value} type="button" aria-pressed={form.housingType === t.value}
          onClick={() => { setForm((p) => ({ ...p, housingType: t.value, numFloors: t.value === 'HOUSE' ? p.numFloors : '1' })); clear('floorAreaM2', 'numFloors') }}
          className={`h-9 rounded-full border px-4 text-sm font-medium ${form.housingType === t.value ? 'border-[#1a2f25] bg-[#1a2f25] text-white' : 'border-stone-300'}`}>{t.label}</button>)}</div></fieldset>
      <div className="grid gap-5 sm:grid-cols-2">
        <div><label className="block text-sm font-medium">Diện tích mỗi tầng (m²)<input className={input} inputMode="decimal" value={form.floorAreaM2} onChange={(e) => set('floorAreaM2', e.target.value)} aria-invalid={!!errors.floorAreaM2} /></label>{err('floorAreaM2')}</div>
        <div><label className="block text-sm font-medium">Số tầng<input className={`${input} disabled:bg-stone-100`} inputMode="numeric" value={form.numFloors} disabled={form.housingType !== 'HOUSE'} onChange={(e) => set('numFloors', e.target.value)} aria-invalid={!!errors.numFloors} /></label>{err('numFloors')}</div>
      </div>
      <p className="flex items-center gap-2 rounded-lg bg-stone-50 p-3 text-sm" aria-live="polite"><Ruler size={16} />
        {total === null ? 'Nhập diện tích để xem khung giá.' : <span><b>{parseNumber(form.floorAreaM2)} m² × {parseNumber(form.numFloors)} tầng = {total} m²</b> · {bandText(total)}</span>}</p>
      <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={form.isDefault} onChange={(e) => set('isDefault', e.target.checked)} />Đặt làm địa chỉ mặc định</label>
      {banner && <p role="alert" className="flex gap-2 rounded-lg bg-red-50 p-3 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{banner}</p>}
      <div className="flex justify-end gap-3">
        <Link to="/app/addresses" className="inline-flex h-10 items-center rounded-lg border border-stone-300 px-4 font-semibold">Hủy</Link>
        <button type="button" disabled={busy} onClick={() => void save()} className="flex h-10 items-center gap-2 rounded-lg bg-[#1a2f25] px-4 font-semibold text-white disabled:opacity-50">{busy && <Loader size={16} className="animate-spin" />}Lưu địa chỉ</button>
      </div>
    </section>
  </div>
}
