// Pure logic of the payment screen: state mapping, countdown, poll rule (no React, so `node --test` covers it).
export const POLL_INTERVAL_MS = 3000

export type PaymentState = 'PENDING' | 'PAID' | 'EXPIRED' | 'FAILED'

/**
 * Wire status -> screen state. The backend sends SUCCESS / PENDING / EXPIRED / REFUNDED (payments.md 1.1); PAID is accepted too.
 * Unknown text reads as FAILED so the screen never claims a payment the server did not confirm.
 */
export function paymentState(txnStatus: string): PaymentState {
  switch (txnStatus.toUpperCase()) {
    case 'PENDING':
      return 'PENDING'
    case 'SUCCESS':
    case 'PAID':
    case 'REFUNDED':
      return 'PAID'
    case 'EXPIRED':
      return 'EXPIRED'
    default:
      return 'FAILED'
  }
}

/** Polling goes on only while the payment is still pending. */
export const shouldPoll = (txnStatus: string) => paymentState(txnStatus) === 'PENDING'

/** Whole seconds left until `expiresAt` (never below 0); 0 for a missing or invalid time. */
export function secondsLeft(expiresAt: string, now: Date): number {
  const end = new Date(expiresAt).getTime()
  if (Number.isNaN(end)) return 0
  return Math.max(0, Math.floor((end - now.getTime()) / 1000))
}

/** `14:21` (mm:ss). */
export function formatCountdown(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds))
  return `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`
}

export interface StateView {
  label: string
  detail: string
  /** The state is shown in words plus an icon, never by colour alone. */
  icon: 'Loader' | 'CircleCheck' | 'Clock' | 'CircleAlert'
}

export function stateView(state: PaymentState): StateView {
  switch (state) {
    case 'PENDING':
      return { label: 'Đang chờ thanh toán', detail: 'Trạng thái tự cập nhật mỗi 3 giây.', icon: 'Loader' }
    case 'PAID':
      return { label: 'Đã thanh toán', detail: 'Cảm ơn bạn. Chúng tôi đang tìm thợ cho ca của bạn.', icon: 'CircleCheck' }
    case 'EXPIRED':
      return { label: 'Mã đã hết hạn', detail: 'Đơn đã bị hủy vì chưa thanh toán. Bạn có thể đặt lại ca.', icon: 'Clock' }
    default:
      return { label: 'Thanh toán không thành công', detail: 'Hãy thử lại hoặc đặt lại ca.', icon: 'CircleAlert' }
  }
}

/** Text of a failed QR request: 409 PAYMENT_EXPIRED means the order is cancelled, 502 means the gateway did not answer (retry). */
export function qrProblem(error: { status?: number; message?: string; data?: unknown }): { text: string; expired: boolean } {
  const code = error.data && typeof error.data === 'object' ? (error.data as { code?: unknown }).code : undefined
  if (error.status === 409 && code === 'PAYMENT_EXPIRED') {
    return { text: 'Đơn đã quá hạn thanh toán và bị hủy. Hãy đặt lại ca.', expired: true }
  }
  if (error.status === 409) return { text: 'Đơn này không còn ở trạng thái chờ thanh toán.', expired: false }
  if (error.status === 502) return { text: 'Cổng thanh toán chưa phản hồi. Vui lòng thử lại.', expired: false }
  return { text: error.message || 'Không tạo được mã thanh toán.', expired: false }
}
