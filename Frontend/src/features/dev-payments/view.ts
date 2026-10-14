// Pure logic of the development payment page (no React, so `node --test` covers it).
import { formatDateTime, formatVnd } from '../../lib/format.ts'
import type { DevPayment } from './types'

const PURPOSE_LABELS: Record<string, string> = {
  ORDER: 'Đơn hàng',
  EXTENSION: 'Gia hạn ca',
  SUBSCRIPTION: 'Mua gói thuê bao',
  ESCROWDEPOSIT: 'Nạp ký quỹ',
}

/** What the payment pays for, with the id a tester can recognise (order, subscription or agency). */
export function subjectOf(payment: DevPayment): string {
  const label = PURPOSE_LABELS[payment.purpose] ?? payment.purpose
  if (payment.orderId != null) return `${label} · đơn #${payment.orderId}`
  if (payment.subscriptionId != null) return `${label} · gói #${payment.subscriptionId}`
  if (payment.agencyId != null) return `${label} · doanh nghiệp #${payment.agencyId}`
  return label
}

export function paymentRow(payment: DevPayment) {
  return {
    id: payment.paymentId,
    subject: subjectOf(payment),
    amount: formatVnd(payment.amount),
    reference: payment.gatewayTxnRef,
    created: formatDateTime(payment.createdAt),
    isFake: payment.gatewayTxnRef.startsWith('FAKE-'),
  }
}

/** The text for a failed call: 404 means the backend is not in Development, 409 a payment that is not of the Fake gateway. */
export function problemText(status: number | undefined, message: string | undefined): string {
  if (status === 404) return 'Công cụ này chỉ có khi backend chạy ở môi trường Development (hoặc không tìm thấy giao dịch).'
  if (status === 409) return 'Chỉ giao dịch của cổng giả (FAKE-) mới xác nhận được ở đây.'
  return message || 'Đã xảy ra lỗi.'
}
