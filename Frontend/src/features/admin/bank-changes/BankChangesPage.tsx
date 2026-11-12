import { useRef, useState } from 'react'
import { DataTable, type Column } from '@/components/data-table'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { ApiError } from '@/services/api'
import { REASON_MAX, reasonError, shouldReloadAfter } from '../../ledger/view'
import { bankChangesApi, PAGE_SIZE } from './api'
import type { BankChangeStatus } from './types'
import { BANK_CHANGE_TABS, approveConfirmText, bankChangeActionError, bankChangeRow, type BankChangeRow } from './view'

type Action = { kind: 'approve' | 'reject'; row: BankChangeRow }

/** LEDGER-29: the Admin's queue of requests to change a worker's payout account: approve or reject with a reason (contract admin.md 2.6). */
export function BankChangesPage() {
  const [status, setStatus] = useState<BankChangeStatus>('PENDING')
  const [page, setPage] = useState(1)
  const [version, setVersion] = useState(0)
  const [action, setAction] = useState<Action | null>(null)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)
  const [done, setDone] = useState<string | null>(null)
  const inFlight = useRef(false) // a second click while a decision runs must not send another one

  const list = useResource(() => bankChangesApi.list(status, page), [status, page, version])
  const rows = list.data ? list.data.items.map(bankChangeRow) : []

  function open(kind: Action['kind'], row: BankChangeRow) {
    setAction({ kind, row })
    setReason('')
    setFailure(null)
    setDone(null)
  }

  async function submit() {
    if (!action || inFlight.current) return
    if (action.kind === 'reject') {
      const problem = reasonError(reason)
      if (problem) {
        setFailure(problem)
        return
      }
    }

    inFlight.current = true
    setBusy(true)
    setFailure(null)
    try {
      if (action.kind === 'approve') await bankChangesApi.approve(action.row.requestId)
      else await bankChangesApi.reject(action.row.requestId, reason.trim())
      setDone(action.kind === 'approve' ? `Đã duyệt đổi tài khoản cho ${action.row.worker}.` : `Đã từ chối đơn đổi tài khoản của ${action.row.worker}.`)
      setAction(null)
      setVersion((v) => v + 1)
    } catch (e) {
      if (e instanceof ApiError) {
        setFailure(bankChangeActionError(e))
        if (shouldReloadAfter(e)) setVersion((v) => v + 1)
      } else {
        setFailure('Đã xảy ra lỗi.')
      }
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  const columns: Column<BankChangeRow>[] = [
    {
      id: 'worker',
      header: 'Thợ',
      cell: (r) => (
        <div>
          <div className="font-medium">{r.worker}</div>
          <div className="text-xs text-muted-foreground">{r.phone}</div>
        </div>
      ),
    },
    { id: 'previous', header: 'Tài khoản cũ', cell: (r) => r.previous },
    { id: 'next', header: 'Tài khoản mới', cell: (r) => <span className="font-medium">{r.next}</span> },
    { id: 'requestedAt', header: 'Gửi lúc', cell: (r) => <span className="whitespace-nowrap">{r.requestedAt}</span> },
    { id: 'status', header: 'Trạng thái', cell: (r) => <StatusBadge tone={r.status.tone}>{r.status.label}</StatusBadge> },
    { id: 'detail', header: 'Ghi chú', cell: (r) => <span className="text-sm text-muted-foreground">{r.detail || '—'}</span> },
    {
      id: 'action',
      header: 'Hành động',
      cell: (r) =>
        r.pending ? (
          <div className="flex gap-2">
            <Button size="sm" disabled={busy} onClick={() => open('approve', r)}>
              Duyệt
            </Button>
            <Button size="sm" variant="outline" disabled={busy} onClick={() => open('reject', r)}>
              Từ chối
            </Button>
          </div>
        ) : (
          '—'
        ),
    },
  ]

  return (
    <div className="flex flex-col gap-6">
      <header>
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Quản lý nhân sự · Tài khoản nhận tiền</div>
        <h1 className="text-2xl font-semibold">Duyệt đổi tài khoản</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Thợ tự do có một tài khoản nhận tiền tại một thời điểm. Lần thêm đầu tiên không cần duyệt; muốn đổi, thợ gửi đơn và bạn duyệt. Sau khi gửi đơn, thợ không gửi được đơn khác trong 15 ngày, kể cả khi đơn bị từ chối. Trong lúc chờ duyệt, các lệnh rút và kỳ lương vẫn dùng tài khoản cũ.
        </p>
      </header>

      <div role="group" aria-label="Trạng thái" className="flex flex-wrap gap-2">
        {BANK_CHANGE_TABS.map((t) => (
          <Button
            key={t.value}
            size="sm"
            aria-pressed={status === t.value}
            variant={status === t.value ? 'default' : 'outline'}
            onClick={() => {
              setStatus(t.value)
              setPage(1)
              setAction(null)
              setFailure(null)
            }}
          >
            {t.label}
          </Button>
        ))}
      </div>

      {done && (
        <div role="status" className="rounded-lg border border-emerald-300 bg-emerald-50 p-3 text-sm text-emerald-900">
          {done}
        </div>
      )}

      {action && (
        <section aria-label={action.kind === 'approve' ? 'Duyệt đơn' : 'Từ chối đơn'} className="flex flex-col gap-3 rounded-xl border border-border bg-card p-4">
          {action.kind === 'approve' ? (
            <p className="text-sm font-medium">{approveConfirmText(action.row)}</p>
          ) : (
            <>
              <p className="text-sm font-medium">Từ chối đơn đổi tài khoản của {action.row.worker}</p>
              <label className="flex flex-col gap-1 text-sm">
                Lý do (bắt buộc, tối đa {REASON_MAX} ký tự)
                <textarea
                  className="min-h-20 rounded-lg border border-input bg-background px-3 py-2 text-sm"
                  value={reason}
                  maxLength={REASON_MAX + 50}
                  onChange={(e) => setReason(e.target.value)}
                />
              </label>
            </>
          )}
          {failure && (
            <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm">
              {failure}
            </div>
          )}
          <div className="flex gap-2">
            <Button disabled={busy} onClick={() => void submit()}>
              {busy ? 'Đang xử lý…' : action.kind === 'approve' ? 'Xác nhận duyệt' : 'Xác nhận từ chối'}
            </Button>
            <Button variant="outline" disabled={busy} onClick={() => setAction(null)}>
              Quay lại
            </Button>
          </div>
        </section>
      )}
      {!action && failure && (
        <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm">
          {failure}
        </div>
      )}

      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(r) => r.requestId}
        loading={list.loading && !list.data}
        error={list.error}
        onRetry={list.reload}
        emptyMessage={status === 'PENDING' ? 'Không có đơn nào đang chờ duyệt.' : 'Chưa có đơn nào ở trạng thái này.'}
        pagination={list.data ? { page: list.data.page, pageSize: PAGE_SIZE, total: list.data.total, onPageChange: setPage } : undefined}
      />
    </div>
  )
}
