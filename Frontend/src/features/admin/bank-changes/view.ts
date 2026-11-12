// Pure logic of the page that approves changes of a worker's payout account (no React, so `node --test` covers it).
// Rules: .spec/contracts/admin.md 2.6 and workers.md (bank section); LEDGER-28 and LEDGER-29.
import { formatDateTime } from '../../../lib/format.ts'
import { conflictCode, type FailedCall, type Tone } from '../../ledger/view.ts'
import type { BankChangeRequest, BankChangeStatus } from './types'

/** The tabs of the page, in the order the Admin works through them. */
export const BANK_CHANGE_TABS: { value: BankChangeStatus; label: string }[] = [
  { value: 'PENDING', label: 'Chờ duyệt' },
  { value: 'APPROVED', label: 'Đã duyệt' },
  { value: 'REJECTED', label: 'Bị từ chối' },
]

export function bankChangeStatusInfo(status: string): { label: string; tone: Tone } {
  switch (status) {
    case 'PENDING':
      return { label: 'Chờ duyệt', tone: 'warning' }
    case 'APPROVED':
      return { label: 'Đã duyệt', tone: 'success' }
    case 'REJECTED':
      return { label: 'Bị từ chối', tone: 'danger' }
    default:
      return { label: status, tone: 'neutral' }
  }
}

/** `Vietcombank · 0011001234567`, or `Chưa có tài khoản` when there was none (a request needs one, so this is only a guard). */
export function accountText(bankName: string | null | undefined, accountNo: string | null | undefined): string {
  const text = [bankName?.trim(), accountNo?.trim()].filter((x) => x).join(' · ')
  return text || 'Chưa có tài khoản'
}

export interface BankChangeRow {
  requestId: number
  worker: string
  phone: string
  previous: string
  next: string
  requestedAt: string
  status: { label: string; tone: Tone }
  /** Why it was rejected, or when it was decided; empty while pending. */
  detail: string
  pending: boolean
}

export function bankChangeRow(r: BankChangeRequest): BankChangeRow {
  return {
    requestId: r.requestId,
    worker: r.workerName?.trim() ? r.workerName : `Thợ #${r.workerId}`,
    phone: r.workerPhone || '—',
    previous: accountText(r.previousBankName, r.previousBankAccountNo),
    next: accountText(r.bankName, r.bankAccountNo),
    requestedAt: formatDateTime(r.requestedAt),
    status: bankChangeStatusInfo(r.status),
    detail: r.status === 'REJECTED' ? (r.rejectReason ?? '') : r.status === 'APPROVED' && r.reviewedAt ? `Duyệt lúc ${formatDateTime(r.reviewedAt)}` : '',
    pending: r.status === 'PENDING',
  }
}

/** The line of the approval step: the account changes at once and the worker cannot send another request for 15 days. */
export function approveConfirmText(row: Pick<BankChangeRow, 'worker' | 'previous' | 'next'>): string {
  return `Duyệt đổi tài khoản của ${row.worker} từ ${row.previous} sang ${row.next}? Từ lúc duyệt, các lệnh rút và kỳ lương sau dùng tài khoản mới; thợ không gửi được đơn đổi nào khác trong 15 ngày kể từ ngày gửi đơn này.`
}

/** What the Admin reads when approving or rejecting fails; the codes of contract admin.md 2.6 are translated. */
export function bankChangeActionError(e: FailedCall): string {
  if (e.status === 409) {
    switch (conflictCode(e)) {
      case 'BANK_CHANGE_NOT_PENDING':
        return 'Đơn đã được xử lý. Danh sách đã được tải lại.'
      case 'LEDGER_BUSY':
        return 'Tài khoản của thợ đang bận, hãy thử lại sau ít giây.'
      default:
        return e.message || 'Đơn không còn ở trạng thái chờ duyệt.'
    }
  }

  switch (e.status) {
    case 400:
      return e.fieldErrors?.reason?.[0] ?? 'Dữ liệu không hợp lệ.'
    case 404:
      return 'Không tìm thấy đơn này.'
    case 0:
      return 'Không kết nối được máy chủ.'
    default:
      return e.message || `Lỗi ${e.status}`
  }
}

/** The query of the list: the tab and the page (no order parameters: the API keeps the queue order). */
export function bankChangeQuery(status: BankChangeStatus, page: number, pageSize: number): Record<string, string | number> {
  return { status, page, pageSize }
}
