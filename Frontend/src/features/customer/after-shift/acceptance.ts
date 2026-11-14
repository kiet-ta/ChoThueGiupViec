// ACCEPT-02 (decision D32): the 24-hour acceptance window as the customer web shows it. Every time comes from the server's `acceptance`
// block (contract workers.md 2.5.4); the page never counts the window itself. Pure: `node --test` runs it without Vite.

export interface AcceptanceBlock {
  phase: string
  submittedAt: string | null
  redoUntil: string | null
  autoAcceptAt: string | null
  disputeUntil: string | null
  completionSource: string | null
  redoCount: number
}

export interface AcceptanceView {
  title: string
  note: string
  tone: 'warn' | 'done'
}

export const UNKNOWN_ACCEPTANCE_TITLE = 'Đang cập nhật trạng thái'
export const REDO_WINDOW_CLOSED_TEXT = 'Người làm đã rời đi nên không nhờ dọn lại được nữa. Bạn có thể nghiệm thu hoặc gửi khiếu nại.'
const KNOWN = new Set(['IN_PROGRESS', 'ON_SITE', 'REVIEW', 'DISPUTED', 'COMPLETED'])

/** `HH:mm` in Asia/Ho_Chi_Minh (UTC+7), with `dd/MM` when the day is not today's (decision G-3). */
export function acceptanceTime(iso: string, now: Date): string {
  const local = new Date(new Date(iso).getTime() + 7 * 3600_000)
  const today = new Date(now.getTime() + 7 * 3600_000)
  const two = (v: number) => String(v).padStart(2, '0')
  const time = `${two(local.getUTCHours())}:${two(local.getUTCMinutes())}`
  const sameDay = local.getUTCFullYear() === today.getUTCFullYear() && local.getUTCMonth() === today.getUTCMonth() && local.getUTCDate() === today.getUTCDate()
  return sameDay ? time : `${time} ${two(local.getUTCDate())}/${two(local.getUTCMonth() + 1)}`
}

/** A redo is possible only while the worker is still on site; without a block (an older server) the server decides. */
export function canAskRedo(block: AcceptanceBlock | null | undefined, now: Date): boolean {
  if (!block) return true
  return block.phase !== 'COMPLETED' && block.redoUntil !== null && now.getTime() < new Date(block.redoUntil).getTime()
}

/** The customer's line for one job; null when there is nothing to show (no block, or the work is still going on). */
export function customerAcceptanceView(block: AcceptanceBlock | null | undefined, now: Date): AcceptanceView | null {
  if (!block || block.phase === 'IN_PROGRESS') return null
  if (!KNOWN.has(block.phase)) return { title: UNKNOWN_ACCEPTANCE_TITLE, note: 'Trang sẽ tải lại.', tone: 'warn' }
  switch (block.phase) {
    case 'ON_SITE':
      return {
        title: 'Mời bạn kiểm tra',
        note: block.redoUntil ? `Người làm còn ở đây đến ${acceptanceTime(block.redoUntil, now)}. Chỗ nào chưa sạch, hãy nhờ dọn lại ngay.` : 'Người làm vẫn đang ở nhà bạn.',
        tone: 'warn',
      }
    case 'REVIEW':
      return {
        title: 'Chờ bạn nghiệm thu',
        note: block.autoAcceptAt ? `Tự nghiệm thu lúc ${acceptanceTime(block.autoAcceptAt, now)} nếu bạn không phản hồi.` : 'Hãy kiểm tra và nghiệm thu ca làm.',
        tone: 'warn',
      }
    case 'DISPUTED':
      return { title: 'Đang xử lý khiếu nại', note: 'Admin đang xem khiếu nại. Bạn vẫn có thể nghiệm thu nếu đã hài lòng.', tone: 'warn' }
    default:
      return {
        title: 'Hoàn tất',
        note: block.completionSource === 'AUTO' ? 'Đã tự nghiệm thu sau 24 giờ.' : block.completionSource === 'VERDICT' ? 'Đã đóng theo kết quả khiếu nại.' : 'Bạn đã nghiệm thu ca này.',
        tone: 'done',
      }
  }
}
