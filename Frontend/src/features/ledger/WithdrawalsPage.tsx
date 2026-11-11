import { useRef, useState } from 'react'
import { DataTable, type Column, type SortState } from '@/components/data-table'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { ApiError } from '@/services/api'
import { withdrawalsApi, PAGE_SIZE } from './api'
import type { WithdrawalStatus } from './types'
import {
  REASON_MAX,
  WITHDRAWAL_SORT,
  WITHDRAWAL_TABS,
  approveConfirmText,
  reasonError,
  shouldReloadAfter,
  withdrawalActionError,
  withdrawalRow,
  type WithdrawalRow,
} from './view'

type Action = { kind: 'approve' | 'reject'; row: WithdrawalRow }

/** WEB-LEDGER-14: the Admin's queue of advance requests: approve (the ledger writes the entry) or reject with a reason (contract ledger.md 6). */
export function WithdrawalsPage() {
  const [status, setStatus] = useState<WithdrawalStatus>('PENDING')
  const [page, setPage] = useState(1)
  // Until a header is clicked the server keeps its queue order (the pending ones oldest first); the order stays when the tab changes.
  const [sort, setSort] = useState<SortState | null>(null)
  const [version, setVersion] = useState(0)
  const [action, setAction] = useState<Action | null>(null)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)
  const [done, setDone] = useState<string | null>(null)
  const inFlight = useRef(false) // a second click while a decision runs must not send another one

  const list = useResource(() => withdrawalsApi.list(status, page, sort), [status, page, sort, version])
  const rows = list.data ? list.data.items.map(withdrawalRow) : []

  function open(kind: Action['kind'], row: WithdrawalRow) {
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
      if (action.kind === 'approve') await withdrawalsApi.approve(action.row.withdrawalId)
      else await withdrawalsApi.reject(action.row.withdrawalId, reason.trim())
      setDone(action.kind === 'approve' ? `Đã duyệt chi ${action.row.amount} cho ${action.row.worker}.` : `Đã từ chối yêu cầu của ${action.row.worker}.`)
      setAction(null)
      setVersion((v) => v + 1)
    } catch (e) {
      if (e instanceof ApiError) {
        setFailure(withdrawalActionError(e))
        if (shouldReloadAfter(e)) setVersion((v) => v + 1)
      } else {
        setFailure('Đã xảy ra lỗi.')
      }
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  const columns: Column<WithdrawalRow>[] = [
    {
      id: 'worker',
      header: 'Thợ',
      sortKey: WITHDRAWAL_SORT.worker,
      sortLabel: 'thợ',
      cell: (r) => (
        <div>
          <div className="font-medium">{r.worker}</div>
          <div className="text-xs text-muted-foreground">{r.phone}</div>
        </div>
      ),
    },
    { id: 'amount', header: 'Số tiền', align: 'right', sortKey: WITHDRAWAL_SORT.amount, sortLabel: 'số tiền', cell: (r) => <span className="font-semibold tabular-nums">{r.amount}</span> },
    { id: 'bank', header: 'Tài khoản nhận', sortKey: WITHDRAWAL_SORT.bank, sortLabel: 'tài khoản nhận', cell: (r) => r.bank },
    { id: 'requestedAt', header: 'Gửi lúc', sortKey: WITHDRAWAL_SORT.sent, sortLabel: 'thời gian gửi', cell: (r) => <span className="whitespace-nowrap">{r.requestedAt}</span> },
    { id: 'status', header: 'Trạng thái', sortKey: WITHDRAWAL_SORT.status, sortLabel: 'trạng thái', cell: (r) => <StatusBadge tone={r.status.tone}>{r.status.label}</StatusBadge> },
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
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Tài chính · Ứng lương</div>
        <h1 className="text-2xl font-semibold">Duyệt rút tiền</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Thợ tự do được ứng một phần tiền đã qua 24 giờ khiếu nại, mỗi lần một yêu cầu. Duyệt thì sổ cái ghi bút toán trừ ví và số tiền được trừ vào lương tháng; hệ thống không chuyển tiền thật.
        </p>
      </header>

      <div role="group" aria-label="Trạng thái" className="flex flex-wrap gap-2">
        {WITHDRAWAL_TABS.map((t) => (
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
        <section aria-label={action.kind === 'approve' ? 'Duyệt yêu cầu' : 'Từ chối yêu cầu'} className="flex flex-col gap-3 rounded-xl border border-border bg-card p-4">
          {action.kind === 'approve' ? (
            <p className="text-sm font-medium">{approveConfirmText(action.row)}</p>
          ) : (
            <>
              <p className="text-sm font-medium">
                Từ chối yêu cầu rút {action.row.amount} của {action.row.worker}
              </p>
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
              {busy ? 'Đang xử lý…' : action.kind === 'approve' ? 'Xác nhận duyệt chi' : 'Xác nhận từ chối'}
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
        sort={sort}
        onSortChange={(next) => {
          setPage(1)
          setSort(next)
        }}
        rows={rows}
        rowKey={(r) => r.withdrawalId}
        loading={list.loading && !list.data}
        error={list.error}
        onRetry={list.reload}
        emptyMessage={status === 'PENDING' ? 'Không có yêu cầu nào đang chờ duyệt.' : 'Chưa có yêu cầu nào ở trạng thái này.'}
        pagination={list.data ? { page: list.data.page, pageSize: PAGE_SIZE, total: list.data.total, onPageChange: setPage } : undefined}
      />
    </div>
  )
}
