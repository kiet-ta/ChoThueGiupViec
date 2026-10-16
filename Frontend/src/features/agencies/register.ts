// Pure logic of the agency registration page (no React, so `node --test` covers it). The rules mirror
// Backend AgencyRegistrationValidator / AgencyBankValidator; the server stays the judge, these only spare a round trip.

export interface RegisterForm {
  taxCode: string
  legalName: string
  legalRepresentative: string
  contactPhone: string
  contactEmail: string
  password: string
  bankName: string
  bankAccountNo: string
}

export const EMPTY_REGISTER_FORM: RegisterForm = {
  taxCode: '',
  legalName: '',
  legalRepresentative: '',
  contactPhone: '',
  contactEmail: '',
  password: '',
  bankName: '',
  bankAccountNo: '',
}

/** Auth.MinPasswordLength of the backend (decisions Q16). */
export const MIN_PASSWORD_LENGTH = 10

const TAX_CODE = /^[0-9]{10}(-[0-9]{3})?$/
const PHONE = /^0[35789][0-9]{8}$/
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** The national form of a Vietnamese mobile number as the backend stores it (`+84` and separators accepted). */
export function normalizePhone(input: string): string {
  let cleaned = input.trim().replace(/[\s\-.()]/g, '')
  if (cleaned.startsWith('+84')) cleaned = '0' + cleaned.slice(3)
  else if (cleaned.startsWith('84') && cleaned.length === 11) cleaned = '0' + cleaned.slice(2)
  return cleaned
}

export type RegisterFieldErrors = Partial<Record<keyof RegisterForm, string>>

/** Field problems of the form, keyed by field; empty when it can be sent. */
export function registerProblems(form: RegisterForm): RegisterFieldErrors {
  const out: RegisterFieldErrors = {}
  const taxCode = form.taxCode.trim()
  if (!taxCode) out.taxCode = 'Nhập mã số thuế.'
  else if (!TAX_CODE.test(taxCode)) out.taxCode = 'Mã số thuế gồm 10 chữ số, hoặc 10 chữ số, dấu gạch ngang và 3 chữ số (ví dụ 0123456789-001).'

  const legalName = form.legalName.trim()
  if (!legalName) out.legalName = 'Nhập tên pháp lý của công ty.'
  else if (legalName.length > 200) out.legalName = 'Tên công ty tối đa 200 ký tự.'

  const representative = form.legalRepresentative.trim()
  if (!representative) out.legalRepresentative = 'Nhập người đại diện pháp luật.'
  else if (representative.length > 100) out.legalRepresentative = 'Tên người đại diện tối đa 100 ký tự.'

  if (!PHONE.test(normalizePhone(form.contactPhone))) out.contactPhone = 'Nhập số điện thoại di động Việt Nam (ví dụ 0901234567).'

  const email = form.contactEmail.trim()
  if (!email) out.contactEmail = 'Nhập email liên hệ.'
  else if (email.length > 255 || !EMAIL.test(email)) out.contactEmail = 'Email không hợp lệ.'

  const password = form.password
  if (!password) out.password = 'Nhập mật khẩu.'
  else if (password.length < MIN_PASSWORD_LENGTH || !/[A-Z]/.test(password) || !/[a-z]/.test(password) || !/[0-9]/.test(password)) {
    out.password = `Mật khẩu từ ${MIN_PASSWORD_LENGTH} ký tự, có chữ hoa, chữ thường và chữ số.`
  }

  const bankName = form.bankName.trim()
  const bankAccount = form.bankAccountNo.replace(/\s+/g, '')
  if (bankName || bankAccount) {
    if (!bankName) out.bankName = 'Nhập tên ngân hàng cùng với số tài khoản.'
    else if (bankName.length > 100) out.bankName = 'Tên ngân hàng tối đa 100 ký tự.'
    if (!bankAccount) out.bankAccountNo = 'Nhập số tài khoản cùng với tên ngân hàng.'
    else if (!/^[0-9]{6,30}$/.test(bankAccount)) out.bankAccountNo = 'Số tài khoản gồm 6 đến 30 chữ số.'
  }
  return out
}

/** The body of `POST /api/agencies/registration`: trimmed text, the bank pair only when it was filled in. */
export function registerBody(form: RegisterForm) {
  const bankName = form.bankName.trim()
  const bankAccountNo = form.bankAccountNo.replace(/\s+/g, '')
  return {
    taxCode: form.taxCode.trim(),
    legalName: form.legalName.trim(),
    legalRepresentative: form.legalRepresentative.trim(),
    contactPhone: normalizePhone(form.contactPhone),
    contactEmail: form.contactEmail.trim(),
    password: form.password,
    ...(bankName && bankAccountNo ? { bankName, bankAccountNo } : {}),
  }
}

export interface RegisterFailure {
  /** A sentence for the whole form (409, network, anything else), or null when only fields are wrong. */
  message: string | null
  fields: RegisterFieldErrors
}

const FIELD_KEYS: (keyof RegisterForm)[] = [
  'taxCode', 'legalName', 'legalRepresentative', 'contactPhone', 'contactEmail', 'password', 'bankName', 'bankAccountNo',
]

/** Words for a failed registration: the 400 field errors of the envelope, the 409 codes, a network failure. */
export function registerFailure(status: number, data: unknown, fieldErrors: Record<string, string[]> = {}): RegisterFailure {
  const fields: RegisterFieldErrors = {}
  for (const key of FIELD_KEYS) {
    const first = Object.entries(fieldErrors).find(([k]) => k.toLowerCase() === key.toLowerCase())?.[1]?.[0]
    if (first) fields[key] = first
  }
  if (status === 0) return { message: 'Không kết nối được máy chủ. Thử lại sau.', fields }
  if (status === 409) {
    const code = data && typeof data === 'object' ? (data as { code?: unknown }).code : null
    if (code === 'AGENCY_TAX_CODE_EXISTS') return { message: 'Mã số thuế này đã được đăng ký.', fields: { ...fields, taxCode: 'Đã được đăng ký.' } }
    if (code === 'AGENCY_EMAIL_EXISTS') return { message: 'Email này đã được đăng ký.', fields: { ...fields, contactEmail: 'Đã được đăng ký.' } }
    return { message: 'Công ty này đã được đăng ký.', fields }
  }
  if (status === 400) return { message: Object.keys(fields).length === 0 ? 'Thông tin chưa hợp lệ.' : null, fields }
  return { message: 'Đăng ký chưa thành công. Thử lại sau.', fields }
}
