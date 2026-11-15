// Maps the failures of `POST /api/v1/auth/password/login` to what the form shows (identity.md section 2.3).
// Pure: runs under `node --test`. It never echoes the password or says which of email/password was wrong.

export interface LoginErrorView {
  /** Message under the form; undefined when only field messages apply. */
  message?: string
  /** Messages per form field, keys lower-cased: `email`, `password`, `role`. */
  fields: Record<string, string[]>
}

export interface ErrorLike {
  status: number
  fieldErrors?: Record<string, string[]>
  retryAfterSeconds?: number | null
}

export function describeLoginError(error: ErrorLike): LoginErrorView {
  switch (error.status) {
    case 400: {
      const fields: Record<string, string[]> = {}
      for (const [name, messages] of Object.entries(error.fieldErrors ?? {})) fields[name.toLowerCase()] = messages
      return Object.keys(fields).length > 0
        ? { fields }
        : { message: 'Thông tin đăng nhập chưa hợp lệ.', fields: {} }
    }
    case 401:
      return { message: 'Email hoặc mật khẩu không đúng.', fields: {} }
    case 403:
      return { message: 'Tài khoản đã bị vô hiệu hóa. Liên hệ quản trị viên.', fields: {} }
    case 423: {
      const seconds = error.retryAfterSeconds
      const when = seconds && seconds > 0 ? ` Thử lại sau ${Math.ceil(seconds / 60)} phút.` : ' Vui lòng thử lại sau.'
      return { message: `Tài khoản tạm thời bị khóa do đăng nhập sai nhiều lần.${when}`, fields: {} }
    }
    case 0:
      return { message: 'Không kết nối được tới máy chủ.', fields: {} }
    default:
      return { message: 'Đã có lỗi xảy ra. Vui lòng thử lại.', fields: {} }
  }
}

const VN_MOBILE_RE = /^0[35789]\d{8}$/
const OTP_CODE_RE = /^\d{6}$/

/** Strips formatting punctuation/spaces and normalizes `+84`/`84` prefixes to national `0...` format. */
export function normalizeVietnamesePhone(input: string): string {
  let cleaned = input.trim().replace(/[\s\-().]/g, '')
  if (cleaned.startsWith('+84')) cleaned = `0${cleaned.slice(3)}`
  else if (cleaned.startsWith('84') && cleaned.length === 11) cleaned = `0${cleaned.slice(2)}`
  return cleaned
}

/** Client-side Vietnamese mobile phone validation matching `PhoneValidator` rules (10 digits starting with 03/05/07/08/09). */
export function otpPhoneError(input: string): string | null {
  const normalized = normalizeVietnamesePhone(input)
  if (!normalized) return 'Vui lòng nhập số điện thoại.'
  if (!VN_MOBILE_RE.test(normalized)) return 'Số điện thoại không hợp lệ (gồm 10 số, bắt đầu bằng 03, 05, 07, 08 hoặc 09).'
  return null
}

/** Client-side 6-digit OTP code check. */
export function otpCodeError(input: string): string | null {
  const trimmed = input.trim()
  if (!trimmed) return 'Vui lòng nhập mã xác nhận 6 chữ số.'
  if (!OTP_CODE_RE.test(trimmed)) return 'Mã OTP phải gồm đúng 6 chữ số.'
  return null
}

function translateOtpFieldMessage(field: string, message: string): string {
  if (field === 'phonenumber' && /invalid vietnamese mobile phone|phonenumber field is required/i.test(message)) {
    return 'Số điện thoại không hợp lệ (gồm 10 số, bắt đầu bằng 03, 05, 07, 08 hoặc 09).'
  }
  if (field === 'code' && /code must be a \d+-digit|code field is required/i.test(message)) {
    return 'Mã OTP phải gồm đúng 6 chữ số.'
  }
  return message
}

export function describeOtpError(error: ErrorLike, step: 'phone' | 'code'): LoginErrorView {
  if (error.status === 400) {
    const fields: Record<string, string[]> = {}
    for (const [name, messages] of Object.entries(error.fieldErrors ?? {})) {
      const key = name.toLowerCase()
      fields[key] = messages.map((msg) => translateOtpFieldMessage(key, msg))
    }
    return Object.keys(fields).length ? { fields } : { fields: {}, message: 'Thông tin chưa hợp lệ.' }
  }
  if (error.status === 401) return { fields: {}, message: 'Mã xác nhận không đúng hoặc đã hết hạn.' }
  if (error.status === 403) return { fields: {}, message: 'Tài khoản đã bị khóa hoặc vô hiệu hóa.' }
  if (error.status === 429) {
    const wait = error.retryAfterSeconds ? ` Thử lại sau ${error.retryAfterSeconds} giây.` : ''
    return { fields: {}, message: step === 'code' ? `Bạn đã thử quá số lần cho phép. Vui lòng yêu cầu mã mới.${wait}` : `Bạn đã yêu cầu mã quá nhiều lần.${wait}` }
  }
  return { fields: {}, message: error.status === 0 ? 'Không kết nối được tới máy chủ.' : 'Đã có lỗi xảy ra. Vui lòng thử lại.' }
}
