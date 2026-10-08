import { useRef, useState } from 'react'
import { DataTable, type Column } from '@/components/data-table'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { cn } from '@/lib/utils'
import { formatDateTime } from '@/lib/format'
import { ApiError } from '@/services/api'
import { absenceApi, PAGE_SIZE } from './api'
import type { AbsenceReport, AbsenceStatus } from './types'
import {
  actionError,
  blockReasonText,
  moneySplitText,
  reportRow,
  statusInfo,
  TABS,
  validateReason,
  REASON_MAX_LENGTH,
  type ActionError,
  type ReportRow,
} from './view'

const COLUMNS: Column<ReportRow>[] = [
  { id: 'order', header: 'Đơn', cell: (r) => <span className="font-semibold">{r.orderCode}</span> },
  {
    id: 'worker',
    header: 'Thợ',
    cell: (r) => (
      <div>
        <div>{r.workerName}</div>
        <div className="text-xs text-muted-foreground">{r.workerKind}</div>
      </div>
    ),
  },
  { id: 'customer', header: 'Khách hàng', cell: (r) => r.customerName },
  { id: 'at', header: 'Báo vắng lúc', cell: (r) => r.reportedAt },
  {
    id: 'gps',
    header: 'GPS',
    cell: (r) => <StatusBadge tone={r.gpsOk ? 'success' : 'danger'}>{r.gpsText}</StatusBadge>,
  },
  { id: 'calls', header: 'Cuộc gọi', align: 'center', cell: (r) => r.calls },
  { id: 'wait', header: 'Đã chờ', cell: (r) => r.waitedText },
  { id: 'fee', header: 'Bồi hoàn 40%', align: 'right', cell: (r) => <span className="tabular-nums">{r.feeText}</span> },
  { id: 'status', header: 'Trạng thái', cell: (r) => <StatusBadge tone={r.status.tone}>{r.status.label}</StatusBadge> },
]

type Mode = 'idle' | 'confirmApprove' | 'reject'

/** WEB-M6-02: customer-absence approval (Figma 19:3, contract admin.md 2.2). */
export function AbsencePage() {
  const [status, setStatus] = useState<AbsenceStatus>('PENDING')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<number | null>(null)
  const [version, setVersion] = useState(0) // bumped after a decision so the list and the panel load again

  const list = useResource(() => absenceApi.list(status, page), [status, page, version])
  const rows = list.data ? list.data.items.map(reportRow) : []

  function changeTab(next: AbsenceStatus) {
    setStatus(next)
    setPage(1)
    setSelected(null)
  }

  return (
    <div className="flex flex-col gap-6">
      <header>
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">MVP5 · BR-05 · Vắng mặt</div>
        <h1 className="text-2xl font-semibold">Biên bản vắng mặt 15 phút</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Thợ báo khách vắng mặt sau khi chờ đủ 15 phút và gọi 2 lần. Duyệt thì thợ nhận 40% giá trị ca, hoàn 60% cho khách.
        </p>
      </header>

      <div role="tablist" aria-label="Trạng thái biên bản" className="flex gap-2">
        {TABS.map((t) => (
          <Button
            key={t.status}
            role="tab"
            aria-selected={status === t.status}
            variant={status === t.status ? 'default' : 'outline'}
            onClick={() => changeTab(t.status)}
          >
            {t.label}
          </Button>
        ))}
      </div>

      <DataTable
        columns={COLUMNS}
        rows={rows}
        rowKey={(r) => r.assignmentId}
        loading={list.loading && !list.data}
        error={list.error}
        onRetry={list.reload}
        emptyMessage={status === 'PENDING' ? 'Không có biên bản nào đang chờ duyệt' : 'Chưa có biên bản trong mục này'}
        onRowClick={(r) => setSelected(r.assignmentId)}
        pagination={
          list.data
            ? { page: list.data.page, pageSize: PAGE_SIZE, total: list.data.total, onPageChange: setPage }
            : undefined
        }
      />

      {selected !== null && (
        <DetailPanel
          key={selected}
          assignmentId={selected}
          version={version}
          onDecided={() => setVersion((v) => v + 1)}
          onClose={() => setSelected(null)}
        />
      )}
    </div>
  )
}

function DetailPanel({
  assignmentId,
  version,
  onDecided,
  onClose,
}: {
  assignmentId: number
  version: number
  onDecided: () => void
  onClose: () => void
}) {
  const detail = useResource(() => absenceApi.get(assignmentId), [assignmentId, version])
  const [mode, setMode] = useState<Mode>('idle')
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [failure, setFailure] = useState<ActionError | null>(null)
  const [busy, setBusy] = useState(false)
  const inFlight = useRef(false) // a second click while a request runs must not send another one

  async function run(call: () => Promise<AbsenceReport>) {
    if (inFlight.current) return
    inFlight.current = true
    setBusy(true)
    setFailure(null)
    try {
      await call()
      setMode('idle')
      setReason('')
      onDecided()
    } catch (e) {
      if (e instanceof ApiError) {
        const err = actionError(e)
        if (e.status === 400 && e.fieldErrors.reason) setReasonError(err.message)
        else setFailure(err)
        if (e.status === 409) onDecided() // the report changed under us: show its real state
      } else {
        setFailure({ message: 'Đã xảy ra lỗi.', reasons: [] })
      }
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  function submitReject() {
    const problem = validateReason(reason)
    setReasonError(problem)
    if (problem) return
    void run(() => absenceApi.reject(assignmentId, reason.trim()))
  }

  if (detail.error && !detail.data) {
    return (
      <section role="alert" className="flex items-center justify-between rounded-xl border border-destructive/40 bg-destructive/5 p-5 text-sm">
        <span>Không tải được biên bản: {detail.error}</span>
        <Button variant="outline" size="sm" onClick={detail.reload}>
          Thử lại
        </Button>
      </section>
    )
  }
  if (!detail.data) {
    return (
      <section aria-busy="true" className="rounded-xl border border-border p-5">
        <div className="h-6 w-64 animate-pulse rounded bg-muted" />
        <div className="mt-4 h-24 animate-pulse rounded bg-muted" />
      </section>
    )
  }

  const r = detail.data
  const info = statusInfo(r.status)
  const pending = r.status === 'PENDING'

  return (
    <section className="rounded-xl border border-border bg-card p-5 text-card-foreground" aria-label={`Biên bản đơn ${r.orderCode}`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-lg font-semibold">
              Thợ: {r.workerName}
            </h2>
            <StatusBadge tone={r.agencyName ? 'accent' : 'info'}>{r.agencyName ? `Agency · ${r.agencyName}` : 'Freelancer'}</StatusBadge>
            <StatusBadge tone={info.tone}>{info.label}</StatusBadge>
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            Khách: {r.customerName} — Đơn {r.orderCode}
          </p>
        </div>
        <Button variant="ghost" size="sm" onClick={onClose}>
          Đóng
        </Button>
      </div>

      <div className="mt-4 grid gap-6 lg:grid-cols-[200px_1fr_320px]">
        <div>
          {r.photoUrl ? (
            <img src={r.photoUrl} alt={`Ảnh chụp cửa nhà, đơn ${r.orderCode}`} className="aspect-square w-full rounded-lg border border-border object-cover" />
          ) : (
            <div className="flex aspect-square w-full items-center justify-center rounded-lg border border-dashed border-border p-3 text-center text-sm text-muted-foreground">
              Thợ không gửi ảnh cửa nhà
            </div>
          )}
        </div>

        <dl className="grid grid-cols-[auto_1fr] items-baseline gap-x-6 gap-y-2 text-sm">
          <dt className="text-muted-foreground">Check-in</dt>
          <dd>{formatDateTime(r.checkedInAt)}</dd>
          <dt className="text-muted-foreground">Báo vắng mặt</dt>
          <dd>{formatDateTime(r.customerAbsentAt)}</dd>
          <dt className="text-muted-foreground">Đã chờ</dt>
          <dd>{r.waitedMinutes} phút</dd>
          <dt className="text-muted-foreground">GPS của thợ</dt>
          <dd className="flex flex-wrap items-center gap-2">
            <StatusBadge tone={r.gpsVerified ? 'success' : 'danger'}>{r.gpsVerified ? 'Đúng vị trí đơn' : 'Chưa xác minh'}</StatusBadge>
            <span>cách địa chỉ {r.distanceM} m</span>
            <span className="text-muted-foreground tabular-nums">
              ({r.deviceLat}, {r.deviceLng})
            </span>
          </dd>
          <dt className="text-muted-foreground">Cuộc gọi xác minh</dt>
          <dd>{r.callAttempts} cuộc</dd>
        </dl>

        <div className="flex flex-col gap-3">
          <div className="rounded-lg bg-muted p-3 text-sm">
            <div className="font-medium">Tác động khi duyệt bồi hoàn</div>
            <div className="mt-1 text-muted-foreground">{moneySplitText(r)}</div>
          </div>

          {pending && !r.canApprove && (
            <div role="status" className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">
              <div className="font-medium">Chưa thể duyệt:</div>
              <ul className="mt-1 list-disc pl-5">
                {r.blockReasons.map((code) => (
                  <li key={code}>{blockReasonText(code)}</li>
                ))}
              </ul>
            </div>
          )}

          {failure && (
            <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm">
              <div>{failure.message}</div>
              {failure.reasons.length > 0 && (
                <ul className="mt-1 list-disc pl-5">
                  {failure.reasons.map((t) => (
                    <li key={t}>{t}</li>
                  ))}
                </ul>
              )}
            </div>
          )}

          {pending && mode === 'idle' && (
            <div className="flex flex-col gap-2">
              <Button size="lg" disabled={!r.canApprove || busy} onClick={() => setMode('confirmApprove')}>
                Duyệt bồi hoàn 40%
              </Button>
              <Button size="lg" variant="outline" disabled={busy} onClick={() => setMode('reject')}>
                Bác yêu cầu
              </Button>
            </div>
          )}

          {pending && mode === 'confirmApprove' && (
            <div className="flex flex-col gap-2 rounded-lg border border-border p-3 text-sm">
              <div className="font-medium">Xác nhận duyệt?</div>
              <div>{moneySplitText(r)}. Ca chuyển sang vắng mặt; không hoàn tác được.</div>
              <div className="flex gap-2">
                <Button disabled={busy} onClick={() => void run(() => absenceApi.approve(assignmentId))}>
                  {busy ? 'Đang duyệt…' : 'Xác nhận'}
                </Button>
                <Button variant="outline" disabled={busy} onClick={() => setMode('idle')}>
                  Huỷ
                </Button>
              </div>
            </div>
          )}

          {pending && mode === 'reject' && (
            <div className="flex flex-col gap-2 rounded-lg border border-border p-3 text-sm">
              <label htmlFor="reject-reason" className="font-medium">
                Lý do từ chối
              </label>
              <textarea
                id="reject-reason"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                rows={3}
                aria-invalid={reasonError !== null}
                aria-describedby="reject-reason-help"
                className={cn('rounded-lg border bg-background p-2', reasonError ? 'border-destructive' : 'border-border')}
              />
              <div id="reject-reason-help" className={cn('text-xs', reasonError ? 'text-destructive' : 'text-muted-foreground')}>
                {reasonError ?? `${reason.trim().length}/${REASON_MAX_LENGTH} ký tự. Chỉ ghi nhận lý do, ca làm giữ nguyên.`}
              </div>
              <div className="flex gap-2">
                <Button variant="destructive" disabled={busy} onClick={submitReject}>
                  {busy ? 'Đang gửi…' : 'Từ chối biên bản'}
                </Button>
                <Button variant="outline" disabled={busy} onClick={() => { setMode('idle'); setReasonError(null) }}>
                  Huỷ
                </Button>
              </div>
            </div>
          )}

          {!pending && (
            <div className="text-sm text-muted-foreground">Biên bản này đã được xử lý, không thể thay đổi.</div>
          )}
        </div>
      </div>
    </section>
  )
}
