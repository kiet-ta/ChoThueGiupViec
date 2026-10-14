import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { formatDateTime } from '../../lib/format.ts'
import { agenciesApi } from './api'
import { useAction } from './actions'
import { Alert, PageHeader, Panel } from './shared'
import { formatCountdown, secondsLeft, swapStateText } from './view'

/** WEB-M5-06: the 30-minute window to replace the worker of an assigned Premium job (decisions Q09, D13). */
export function SwapPage() {
  const windows = useResource(() => agenciesApi.swapWindows(), [])
  const [now, setNow] = useState(() => Date.now())
  const [assignmentId, setAssignmentId] = useState('')
  const [reason, setReason] = useState('')
  const [newWorker, setNewWorker] = useState<Record<number, string>>({})
  const report = useAction()
  const swap = useAction()
  const [notice, setNotice] = useState<string | null>(null)

  // The countdown ticks locally and the list is read again when a window runs out.
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [])

  async function reportUnavailable() {
    const id = Number(assignmentId)
    if (!Number.isInteger(id) || id <= 0) return report.setError('Nhập mã ca (số nguyên dương).')
    if (!reason.trim()) return report.setError('Nêu lý do thợ không đi được.')
    const result = await report.run(() => agenciesApi.reportUnavailable(id, reason.trim()))
    if (result) {
      setNotice(result.state === 'OPEN' ? 'Đã mở cửa sổ 30 phút để bạn tự đổi thợ.' : 'Còn không quá 2 giờ nên hệ thống chuyển cho điều phối cứu hộ.')
      windows.reload()
    }
  }

  async function doSwap(id: number) {
    const worker = Number(newWorker[id])
    if (!Number.isInteger(worker) || worker <= 0) return swap.setError('Nhập mã thợ thay thế.')
    const result = await swap.run(() => agenciesApi.swap(id, worker))
    if (result) {
      setNotice('Đã đổi thợ.')
      windows.reload()
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader eyebrow="Đối tác · Cửa sổ đổi thợ" title="Đổi thợ trong 30 phút">
        Báo thợ không đi được khi còn hơn 2 giờ trước ca: bạn có tối đa 30 phút để thay bằng thợ của công ty có ca trống. Hết hạn hoặc còn không quá
        2 giờ thì hệ thống chuyển cho điều phối cứu hộ.
      </PageHeader>

      <Panel title="Báo thợ không đi được">
        <form
          className="flex flex-wrap items-end gap-3 text-sm"
          onSubmit={(e) => {
            e.preventDefault()
            void reportUnavailable()
          }}
        >
          <label className="flex flex-col gap-1">
            Mã ca
            <input value={assignmentId} onChange={(e) => setAssignmentId(e.target.value)} inputMode="numeric" className="w-28 rounded-lg border border-border bg-background p-2" />
          </label>
          <label className="flex min-w-72 flex-1 flex-col gap-1">
            Lý do
            <input value={reason} onChange={(e) => setReason(e.target.value)} maxLength={255} className="rounded-lg border border-border bg-background p-2" />
          </label>
          <Button type="submit" size="sm" disabled={report.busy}>{report.busy ? 'Đang gửi…' : 'Báo không đi được'}</Button>
        </form>
        {report.error && <div className="mt-3"><Alert>{report.error}</Alert></div>}
      </Panel>

      {notice && <Alert tone="info">{notice}</Alert>}
      {swap.error && <Alert>{swap.error}</Alert>}
      {windows.error && <Alert>{windows.error}</Alert>}

      <Panel title="Cửa sổ đang mở">
        {(windows.data ?? []).length === 0 ? (
          <p className="text-sm text-muted-foreground">Không có cửa sổ nào đang mở.</p>
        ) : (
          <ul className="flex flex-col gap-3">
            {(windows.data ?? []).map((w) => {
              const left = secondsLeft(w, now)
              return (
                <li key={w.assignmentId} className="flex flex-wrap items-end gap-4 rounded-lg border border-border p-3 text-sm">
                  <div>
                    <div className="font-medium">Ca #{w.assignmentId} · đơn #{w.orderId}</div>
                    <div className="text-xs text-muted-foreground">Bắt đầu ca {formatDateTime(w.shiftStartUtc)} · {swapStateText(w.state)}</div>
                  </div>
                  <div className={left < 300 ? 'text-lg font-semibold tabular-nums text-destructive' : 'text-lg font-semibold tabular-nums'} aria-label="Thời gian còn lại">
                    {formatCountdown(left)}
                  </div>
                  <label className="flex flex-col gap-1">
                    Mã thợ thay thế
                    <input
                      value={newWorker[w.assignmentId] ?? ''}
                      onChange={(e) => setNewWorker({ ...newWorker, [w.assignmentId]: e.target.value })}
                      inputMode="numeric"
                      className="w-32 rounded-lg border border-border bg-background p-2"
                    />
                  </label>
                  <Button size="sm" disabled={swap.busy || left === 0} onClick={() => void doSwap(w.assignmentId)}>
                    Đổi thợ
                  </Button>
                </li>
              )
            })}
          </ul>
        )}
      </Panel>
    </div>
  )
}
