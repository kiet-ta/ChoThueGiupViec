// Maps the failures of `POST /api/auth/password/login` to what the form shows (identity.md section 2.3).
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
