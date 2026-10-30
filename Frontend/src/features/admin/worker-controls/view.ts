// Pure logic of the "Cờ & mở khoá thợ" page (no React, so `node --test` covers it).
import type { WorkerAction } from './types'

export const REASON_MAX_LENGTH = 255 // ADMIN_AUDIT_LOG.reason NVARCHAR(255), contract admin.md 2.5 and 2.6

export interface ActionInfo {
  value: WorkerAction
  label: string
  hint: string
  /** What the confirmation says the action does. */
  effect: string
}

export const ACTIONS: ActionInfo[] = [
  {
    value: 'APPROVE_SUPER',
    label: 'Duyệt Super-Freelancer',
    hint: 'Chỉ khi thợ đạt: đánh giá từ 4,80, từ 50 ca hoàn thành, eKYC đã duyệt, không có khiếu nại lỗi Freelancer trong 180 ngày.',
    effect: 'Thợ trở thành Super-Freelancer và được dùng cho cứu hộ khẩn cấp của đơn Premium. Tự thu hồi khi điểm xuống dưới 4,70.',
  },
  {
    value: 'REVOKE_SUPER',
    label: 'Thu hồi Super-Freelancer',
    hint: 'Gỡ cờ ngay, không cần điều kiện.',
    effect: 'Thợ không còn là Super-Freelancer.',
  },
  {
    value: 'UNLOCK',
    label: 'Mở khoá thợ',
    hint: 'Thợ Freelancer đang bị khoá (phán quyết khiếu nại, huỷ ca nhiều lần) và đã được duyệt eKYC trở lại trạng thái sẵn sàng.',
    effect: 'Thợ chuyển từ bị khoá sang sẵn sàng (IDLE) và nhận được ca mới.',
  },
]

export function actionInfo(action: WorkerAction): ActionInfo {
  return ACTIONS.find((a) => a.value === action) ?? ACTIONS[0]
}

/** The worker number the Admin typed: a whole number of at least 1. */
export function parseWorkerId(text: string): { id: number } | { error: string } {
  const trimmed = text.trim()
  if (trimmed === '') return { error: 'Nhập mã số thợ.' }
  if (!/^\d+$/.test(trimmed)) return { error: 'Mã số thợ chỉ gồm chữ số.' }
  const id = Number(trimmed)
  if (!Number.isSafeInteger(id) || id < 1 || id > 2147483647) return { error: 'Mã số thợ không hợp lệ.' }
  return { id }
}

export function validateReason(text: string): string | null {
  const trimmed = text.trim()
  if (trimmed.length === 0) return 'Vui lòng nhập lý do (ghi vào nhật ký kiểm toán).'
  if (trimmed.length > REASON_MAX_LENGTH) return `Lý do tối đa ${REASON_MAX_LENGTH} ký tự (đang ${trimmed.length}).`
  return null
}

/** The request of an action (contract admin.md 2.5 and 2.6); the reason is sent trimmed. */
export function actionRequest(
  action: WorkerAction,
  workerId: number,
  reason: string,
): { method: 'POST' | 'DELETE'; path: string; body: { reason: string } } {
  const body = { reason: reason.trim() }
  switch (action) {
    case 'APPROVE_SUPER':
      return { method: 'POST', path: `/admin/workers/${workerId}/super-freelancer`, body }
    case 'REVOKE_SUPER':
      return { method: 'DELETE', path: `/admin/workers/${workerId}/super-freelancer`, body }
    case 'UNLOCK':
      return { method: 'POST', path: `/admin/workers/${workerId}/unlock`, body }
  }
}

/** What a 409 `failedCriteria` / `blockReasons` code means, in words. */
const REASONS: Record<string, string> = {
  RATING_BELOW_MINIMUM: 'Điểm đánh giá trung bình của thợ dưới 4,80.',
  COMPLETED_JOBS_BELOW_MINIMUM: 'Thợ chưa đủ 50 ca hoàn thành.',
  KYC_NOT_APPROVED: 'eKYC của thợ chưa được duyệt (hãy xử lý ở trang Hậu kiểm eKYC Thợ).',
  UPHELD_DISPUTE_RECENT: 'Thợ có khiếu nại lỗi Freelancer được phán quyết trong 180 ngày gần đây.',
  NOT_FREELANCER: 'Chỉ áp dụng cho thợ Freelancer (thợ Agency do Agency quản lý).',
  NOT_LOCKED: 'Thợ này đang không bị khoá.',
}

export function reasonText(code: string): string {
  return REASONS[code] ?? code
}

/** The part of an `ApiError` the page needs, so this file does not import the client (which reads `import.meta`). */
export interface FailedCall {
  status: number
  message: string
  data: unknown
  fieldErrors?: Record<string, string[]>
}

export interface ActionError {
  message: string
  /** Why a 409 refused the action, in words; empty otherwise. */
  reasons: string[]
}

function codesOf(data: unknown): string[] {
  if (typeof data !== 'object' || data === null) return []
  const bag = data as { failedCriteria?: unknown; blockReasons?: unknown }
  const list = Array.isArray(bag.failedCriteria) ? bag.failedCriteria : Array.isArray(bag.blockReasons) ? bag.blockReasons : []
  return list.filter((x): x is string => typeof x === 'string')
}

/** Turns a failed call into what the Admin should read (contract admin.md 2.5 / 2.6: 400, 401, 404, 409). */
export function actionError(action: WorkerAction, e: FailedCall): ActionError {
  switch (e.status) {
    case 400:
      return { message: e.fieldErrors?.reason?.[0] ?? (e.message || 'Dữ liệu không hợp lệ.'), reasons: [] }
    case 404:
      return { message: 'Không tìm thấy thợ này (hoặc chức năng chưa có trên máy chủ).', reasons: [] }
    case 409: {
      const reasons = codesOf(e.data).map(reasonText)
      if (reasons.length > 0) {
        return { message: action === 'UNLOCK' ? 'Chưa thể mở khoá:' : 'Chưa đủ điều kiện để duyệt:', reasons }
      }
      return { message: 'Yêu cầu bị từ chối vì trạng thái của thợ.', reasons: [] }
    }
    case 0:
      return { message: 'Không kết nối được máy chủ.', reasons: [] }
    default:
      return { message: e.message || `Lỗi ${e.status}`, reasons: [] }
  }
}

export function successText(action: WorkerAction, workerId: number): string {
  switch (action) {
    case 'APPROVE_SUPER':
      return `Đã duyệt thợ #${workerId} là Super-Freelancer.`
    case 'REVOKE_SUPER':
      return `Đã thu hồi cờ Super-Freelancer của thợ #${workerId}.`
    case 'UNLOCK':
      return `Đã mở khoá thợ #${workerId}: thợ ở trạng thái sẵn sàng.`
  }
}
