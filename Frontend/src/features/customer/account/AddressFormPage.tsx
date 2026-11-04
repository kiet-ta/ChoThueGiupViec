import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { CircleAlert, Loader, Ruler } from 'lucide-react'
import { ApiError } from '@/services/api'
import { accountApi } from './api'
import {
  accountProblem, bandText, DEFAULT_LATITUDE, DEFAULT_LONGITUDE, formFromAddress, HOUSING_TYPES, parseNumber, toAddressBody, totalArea,
  safeAddressNext, validateAddress, wardsOf, type AddressErrors, type AddressForm,
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
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [locating, setLocating] = useState(false)
  const [located, setLocated] = useState(false)

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

  function useMyLocation() {
    if (!('geolocation' in navigator)) return setBanner('Trình duyệt không hỗ trợ định vị.')
    setLocating(true)
    setBanner(null)
    setNotice(null)
    navigator.geolocation.getCurrentPosition(
      async (pos) => {
        const lat = Number(pos.coords.latitude.toFixed(6))
        const lng = Number(pos.coords.longitude.toFixed(6))
        try {
          const res = await accountApi.reverseGeocode(lat, lng)
          setForm((prev) => ({
            ...prev,
            latitude: lat,
            longitude: lng,
            city: res.city || prev.city,
            district: res.district || prev.district,
            addressLine: res.addressLine || prev.addressLine,
          }))
          clear('city', 'district', 'addressLine', 'location')
          setLocated(true)
          setNotice('Đã định vị và tự động điền địa chỉ từ vị trí hiện tại.')
        } catch {
          // Graceful degradation: keeps coordinates
          setForm((prev) => ({ ...prev, latitude: lat, longitude: lng }))
          clear('location')
          setLocated(true)
          setNotice('Đã lấy toạ độ GPS. Vui lòng kiểm tra và nhập địa chỉ bên trên.')
        } finally {
          setLocating(false)
        }
      },
      () => {
        setBanner('Không lấy được vị trí. Địa chỉ sẽ dùng vị trí mặc định của thành phố.')
        setLocating(false)
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
      <div className="grid gap-5 sm:grid-cols-2">
        <div><label className="block text-sm font-medium">Tỉnh / Thành phố
          <select className={input} value={form.city} onChange={(e) => { setForm((p) => ({ ...p, city: e.target.value, district: '' })); clear('city', 'district') }} aria-invalid={!!errors.city}>
            <option value="">Chọn…</option>{units && Object.keys(units).map((c) => <option key={c} value={c}>{c}</option>)}</select></label>{err('city')}</div>
        <div><label className="block text-sm font-medium">Phường / Xã
          <select className={input} value={form.district} onChange={(e) => set('district', e.target.value)} aria-invalid={!!errors.district} disabled={!form.city}>
            <option value="">Chọn…</option>{form.district && !wards.includes(form.district) && <option value={form.district}>{form.district}</option>}{wards.map((w) => <option key={w} value={w}>{w}</option>)}</select></label>{err('district')}</div>
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
      <div className="flex flex-wrap items-center gap-3 text-sm">
        <button type="button" onClick={useMyLocation} disabled={locating} className="h-9 rounded-lg border border-stone-300 px-3 font-semibold disabled:opacity-50">{locating ? 'Đang định vị…' : 'Dùng vị trí của tôi'}</button>
        <span className={notice ? 'font-medium text-[#1a2f25]' : 'text-stone-500'}>{notice ? notice : located ? `Đã lấy vị trí (${form.latitude}, ${form.longitude}).` : 'Chưa định vị: dùng vị trí mặc định để tìm thợ gần nhà.'}</span>
      </div>
      {err('location')}
      <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={form.isDefault} onChange={(e) => set('isDefault', e.target.checked)} />Đặt làm địa chỉ mặc định</label>
      {banner && <p role="alert" className="flex gap-2 rounded-lg bg-red-50 p-3 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{banner}</p>}
      <div className="flex justify-end gap-3">
        <Link to="/app/addresses" className="inline-flex h-10 items-center rounded-lg border border-stone-300 px-4 font-semibold">Hủy</Link>
        <button type="button" disabled={busy} onClick={() => void save()} className="flex h-10 items-center gap-2 rounded-lg bg-[#1a2f25] px-4 font-semibold text-white disabled:opacity-50">{busy && <Loader size={16} className="animate-spin" />}Lưu địa chỉ</button>
      </div>
    </section>
  </div>
}
