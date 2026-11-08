// Pure logic of the address, favorites and profile screens (no React, so `node --test` covers it).
// Rules: .spec/contracts/customers.md 2.1/2.2 (validation), booking.md 1.3 (area bracket, workers), Address.RoomMaxAreaM2 30, Address.HouseMaxFloors 10.
import type { Address, AddressBody, HousingType } from './types.ts'

/** Match Vietnamese place names without case, accents or the đ/d distinction. */
export function filterAdminUnits(items: string[], query: string): string[] {
  const normalize = (value: string) => value.toLocaleLowerCase('vi').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd')
  const needle = normalize(query.trim())
  return items.filter((item) => normalize(item).includes(needle))
}

/** Keep return paths inside the customer area, including after URL normalization. */
export function safeAddressNext(next: string | null): string | null {
  if (!next?.startsWith('/app/') || next.includes('//') || next.includes('\\') || [...next].some((c) => c.charCodeAt(0) < 32 || c.charCodeAt(0) === 127)) return null
  try {
    const url = new URL(next, 'https://example.invalid')
    if (url.origin !== 'https://example.invalid' || !url.pathname.startsWith('/app/')) return null
    return url.pathname + url.search + url.hash
  } catch {
    return null
  }
}

export const ROOM_MAX_AREA_M2 = 30
export const HOUSE_MAX_FLOORS = 10
export const STANDARD_MAX_AREA_M2 = 80
export const DEFAULT_LATITUDE = 10.7769
export const DEFAULT_LONGITUDE = 106.7009

export const HOUSING_TYPES: { value: HousingType; label: string }[] = [
  { value: 'APARTMENT', label: 'Chung cư' },
  { value: 'HOUSE', label: 'Nhà phố' },
  { value: 'ROOM', label: 'Phòng trọ' },
]
export const housingLabel = (type: string) => HOUSING_TYPES.find((t) => t.value === type)?.label ?? type

/** `floorAreaM2 x numFloors`, rounded to 2 decimals half away from zero like the server; null for a missing or non-positive input. */
export function totalArea(floorAreaM2: number | null, numFloors: number | null): number | null {
  if (floorAreaM2 === null || numFloors === null || !(floorAreaM2 > 0) || !(numFloors > 0)) return null
  const raw = floorAreaM2 * numFloors
  return Math.sign(raw) * (Math.round(Math.abs(raw) * 100 + Number.EPSILON) / 100)
}

export type AreaBracket = 'UP_TO_30' | 'FROM_31_TO_80' | 'OVER_80'

/** Bracket of booking.md 1.3: <= 30, > 30 and <= 80, > 80. */
export function areaBracket(total: number): AreaBracket {
  if (total <= 30) return 'UP_TO_30'
  return total <= STANDARD_MAX_AREA_M2 ? 'FROM_31_TO_80' : 'OVER_80'
}

/** Workers needed: 1 up to 80 m2, exactly 2 above (BR-02). */
export const requiredWorkers = (total: number) => (total <= STANDARD_MAX_AREA_M2 ? 1 : 2)

/** `31–80 m² · 1 thợ`, the price band shown under the area. */
export function bandText(total: number): string {
  const band = { UP_TO_30: 'Đến 30 m²', FROM_31_TO_80: '31–80 m²', OVER_80: 'Trên 80 m²' }[areaBracket(total)]
  return `Khung giá ${band} · ${requiredWorkers(total)} thợ`
}

export const areaText = (a: Pick<Address, 'floorAreaM2' | 'numFloors' | 'totalAreaM2'>) => `${a.floorAreaM2} m² × ${a.numFloors} tầng = ${a.totalAreaM2} m²`

export interface AddressForm {
  label: string
  city: string
  district: string
  addressLine: string
  housingType: HousingType
  floorAreaM2: string
  numFloors: string
  isDefault: boolean
  latitude: number
  longitude: number
}

export type AddressErrors = Partial<Record<'label' | 'city' | 'district' | 'addressLine' | 'floorAreaM2' | 'numFloors' | 'location', string>>

/** Number from a form field (a decimal comma is accepted); null for empty or not a number. */
export function parseNumber(text: string): number | null {
  const t = text.trim().replace(',', '.')
  if (t === '') return null
  const n = Number(t)
  return Number.isFinite(n) ? n : null
}

export function validateAddress(f: AddressForm): AddressErrors {
  const e: AddressErrors = {}
  if (!f.label.trim()) e.label = 'Hãy đặt tên gợi nhớ.'
  else if (f.label.trim().length > 50) e.label = 'Tối đa 50 ký tự.'
  if (!f.city) e.city = 'Hãy chọn tỉnh / thành phố.'
  if (!f.district) e.district = 'Hãy chọn phường / xã.'
  if (!f.addressLine.trim()) e.addressLine = 'Hãy nhập số nhà, đường.'
  else if (f.addressLine.trim().length > 255) e.addressLine = 'Tối đa 255 ký tự.'
  const area = parseNumber(f.floorAreaM2)
  if (area === null || area <= 0) e.floorAreaM2 = 'Diện tích phải lớn hơn 0.'
  else if (area > 9999.99) e.floorAreaM2 = 'Diện tích tối đa 9999,99 m².'
  else if (f.housingType === 'ROOM' && area > ROOM_MAX_AREA_M2) e.floorAreaM2 = `Phòng trọ tối đa ${ROOM_MAX_AREA_M2} m².`
  const floors = parseNumber(f.numFloors)
  if (floors === null || !Number.isInteger(floors) || floors < 1) e.numFloors = 'Số tầng phải là số nguyên từ 1.'
  else if (f.housingType !== 'HOUSE' && floors !== 1) e.numFloors = 'Chung cư và phòng trọ chỉ có 1 tầng.'
  else if (floors > HOUSE_MAX_FLOORS) e.numFloors = `Nhà phố tối đa ${HOUSE_MAX_FLOORS} tầng.`
  if (!(f.latitude >= -90 && f.latitude <= 90 && f.longitude >= -180 && f.longitude <= 180)) e.location = 'Vị trí không hợp lệ.'
  return e
}

/** The request body of a valid form (call only when `validateAddress` returned no errors). */
export function toAddressBody(f: AddressForm): AddressBody {
  return {
    label: f.label.trim(),
    addressLine: f.addressLine.trim(),
    district: f.district,
    city: f.city,
    housingType: f.housingType,
    floorAreaM2: parseNumber(f.floorAreaM2) ?? 0,
    numFloors: parseNumber(f.numFloors) ?? 1,
    bedrooms: null,
    bathrooms: null,
    latitude: f.latitude,
    longitude: f.longitude,
    isDefault: f.isDefault,
  }
}

export function formFromAddress(a: Address): AddressForm {
  return {
    label: a.label, city: a.city, district: a.district, addressLine: a.addressLine,
    housingType: (['APARTMENT', 'HOUSE', 'ROOM'].includes(a.housingType) ? a.housingType : 'APARTMENT') as HousingType,
    floorAreaM2: String(a.floorAreaM2), numFloors: String(a.numFloors), isDefault: a.isDefault, latitude: a.latitude, longitude: a.longitude,
  }
}

/** The wards of a province, empty for an unknown province. */
export const wardsOf = (units: Record<string, string[]>, city: string): string[] => units[city] ?? []

// ---------- Profile ----------
export interface ProfileErrors {
  fullName?: string
  email?: string
}

export function validateProfile(fullName: string, email: string): ProfileErrors {
  const e: ProfileErrors = {}
  const name = fullName.trim()
  if (!name) e.fullName = 'Hãy nhập họ và tên.'
  else if (name.length > 100) e.fullName = 'Tối đa 100 ký tự.'
  const mail = email.trim()
  if (mail && (mail.length > 255 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(mail))) e.email = 'Email không hợp lệ.'
  return e
}

/** `0912 345 678` for a Vietnamese phone number of 10 digits, otherwise as given. */
export function phoneText(phone: string): string {
  const d = phone.replace(/\D/g, '')
  return d.length === 10 ? `${d.slice(0, 4)} ${d.slice(4, 7)} ${d.slice(7)}` : phone
}

/** Two-letter initials of a name, `?` for an empty one. */
export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  if (!parts.length) return '?'
  return parts.slice(-2).map((p) => p[0]).join('').toUpperCase()
}

// WorkStatus of the Domain (DbEnum upper-case): PENDING, IDLE, BUSY, LOCKED, RETIRING, RETIRED.
const WORK_STATUS: Record<string, string> = {
  IDLE: 'Đang hoạt động',
  BUSY: 'Đang bận',
  PENDING: 'Chưa kích hoạt',
  LOCKED: 'Tạm khóa',
  RETIRING: 'Sắp nghỉ việc',
  RETIRED: 'Đã nghỉ việc',
}
/** The label of a worker's `workStatus`; an unknown value is shown as the server sent it. */
export const workStatusLabel = (status: string) => WORK_STATUS[status.toUpperCase()] ?? status

/** Whether a favorite worker can still be booked: a worker who is not locked, retiring, retired or pending. */
export const canBookWorker = (status: string) => ['IDLE', 'BUSY'].includes(status.toUpperCase())

/** Text of a failed call: first field message of a 400, the delete rule of a 409, the server message otherwise. */
export function accountProblem(error: { status?: number; message?: string; fieldErrors?: Record<string, string[]> }, deleting = false): string {
  if (error.status === 400) {
    const first = Object.values(error.fieldErrors ?? {}).flat()[0]
    if (first) return first
  }
  if (error.status === 409 && deleting) return 'Địa chỉ này đã có đơn nên không xóa được.'
  if (error.status === 404) return 'Không tìm thấy mục này. Có thể nó đã bị xóa.'
  return error.message || 'Đã có lỗi xảy ra. Vui lòng thử lại.'
}

// ---------- Favorite toggle (rating page; PUT / DELETE /customers/me/favorite-workers/{workerId}, both idempotent) ----------
export const isFavorite = (favorites: { workerId: number }[], workerId: number) => favorites.some((f) => f.workerId === workerId)

export const favoriteToggleLabel = (saved: boolean) => (saved ? 'Đã lưu vào yêu thích' : 'Lưu thợ yêu thích')

/** The line shown when the toggle failed and was rolled back; `adding` = the customer tried to save the worker. */
export const favoriteToggleError = (adding: boolean) =>
  adding ? 'Chưa lưu được thợ vào yêu thích. Vui lòng thử lại.' : 'Chưa bỏ được thợ khỏi yêu thích. Vui lòng thử lại.'
