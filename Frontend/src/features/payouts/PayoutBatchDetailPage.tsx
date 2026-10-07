import { useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { DataTable, type Column } from '@/components/data-table'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { formatDateTime, formatVnd } from '@/lib/format'
import { cn } from '@/lib/utils'
import { ApiError } from '@/services/api'
import { PAGE_SIZE, payoutsApi } from './api'
import { downloadExport } from './download'
import type { ExportType, PayoutBatch } from './types'
import {
  batchStatusInfo,
  buildError,
  confirmError,
  confirmLines,
  EXPORTS,
  exportFallbackName,
  itemRow,
  PAYEE_CHIPS,
  periodLabel,
  warningText,
  type ItemRow,
} from './view'

const COLUMNS: Column<ItemRow>[] = [
  {
    id: 'payee',
    header: 'Người nhận',
    cell: (r) => (
      <div>
        <div className="font-medium">{r.name}</div>
        <div className="text-xs text-muted-foreground">{r.type}</div>
      </div>
    ),
  },
  { id: 'jobs', header: 'Số ca', align: 'right', cell: (r) => r.jobs },
  { id: 'gross', header: 'Tổng thu', align: 'right', cell: (r) => <span className="tabular-nums">{r.gross}</span> },
  { id: 'commission', header: 'Hoa hồng', align: 'right', cell: (r) => <span className="tabular-nums">{r.commission}</span> },
  { id: 'penalty', header: 'Khấu trừ', align: 'right', cell: (r) => <span className="tabular-nums">{r.penalty}</span> },
  { id: 'net', header: 'Thực nhận', align: 'right', cell: (r) => <span className="font-semibold tabular-nums">{r.net}</span> },
  { id: 'bank', header: 'Ngân hàng', cell: (r) => r.bank },
  {
    id: 'account',
    header: 'Số tài khoản',
    cell: (r) => (r.missingAccount ? <StatusBadge tone="danger">Chưa có</StatusBadge> : <span className="tabular-nums">{r.account}</span>),
  },
  { id: 'status', header: 'Trạng thái', cell: (r) => <StatusBadge tone={r.status.tone}>{r.status.label}</StatusBadge> },
]

/** WEB-M6-03 detail: items, bank files, rebuild and the disbursement confirmation of one batch (contract payouts.md 2.1-2.4). */
export function PayoutBatchDetailPage() {
  const { batchId: raw } = useParams()
  const batchId = Number(raw)
  const valid = Number.isInteger(batchId) && batchId > 0
  const [payeeType, setPayeeType] = useState('')
  const [page, setPage] = useState(1)
  const [version, setVersion] = useState(0)

  const detail = useResource(
    () => (valid ? payoutsApi.detail(batchId, payeeType, page) : Promise.reject(new ApiError(404, 'Not found'))),
    [batchId, payeeType, page, version],
  )
  const reload = () => setVersion((v) => v + 1)

  if (detail.error && !detail.data) {
    return (
      <div className="flex flex-col gap-4">
        <Link to="/admin/payout-batches" className="text-sm underline-offset-4 hover:underline">
          ← Về danh sách kỳ
        </Link>
        <div role="alert" className="flex items-center justify-between rounded-xl border border-destructive/40 bg-destructive/5 p-5 text-sm">
          <span>{valid ? `Không tải được kỳ payout: ${detail.error}` : 'Mã kỳ không hợp lệ.'}</span>
          {valid && (
            <Button variant="outline" size="sm" onClick={detail.reload}>
              Thử lại
            </Button>
          )}
        </div>
      </div>
    )
  }
  if (!detail.data) {
    return (
      <div aria-busy="true" className="flex flex-col gap-4">
        <div className="h-8 w-72 animate-pulse rounded bg-muted" />
        <div className="h-24 animate-pulse rounded-xl bg-muted" />
        <div className="h-64 animate-pulse rounded-xl bg-muted" />
      </div>
    )
  }

  const { batch, items, warnings } = detail.data
  const rows = items.map(itemRow)

  return (
    <div className="flex flex-col gap-6">
      <header className="flex flex-col gap-2">
        <Link to="/admin/payout-batches" className="text-sm underline-offset-4 hover:underline">
          ← Về danh sách kỳ
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-semibold">Kỳ payout {periodLabel(batch.periodMonth)}</h1>
          <StatusBadge tone={batchStatusInfo(batch.batchStatus).tone}>{batchStatusInfo(batch.batchStatus).label}</StatusBadge>
        </div>
        <p className="text-sm text-muted-foreground">
          {batch.itemCount} người nhận · tổng thực nhận <span className="font-medium text-foreground">{formatVnd(batch.totalAmount)}</span>
          {batch.confirmedAt && ` · giải ngân lúc ${formatDateTime(batch.confirmedAt)} bởi Admin #${batch.confirmedBy}`}
        </p>
      </header>

      {warnings.length > 0 && (
        <div role="status" className="rounded-xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900">
          <div className="font-medium">Thiếu thông tin ngân hàng ({warnings.length}):</div>
          <ul className="mt-1 list-disc pl-5">
            {warnings.map((w) => (
              <li key={w}>{warningText(w)}</li>
            ))}
          </ul>
          {batch.batchStatus === 'DRAFT' && <p className="mt-1">Họ vẫn nằm trong kỳ; bổ sung số tài khoản trước khi chuyển khoản.</p>}
        </div>
      )}

      <ExportBar batch={batch} />
      {batch.batchStatus === 'DRAFT' && <DraftActions batch={batch} onChanged={reload} />}

      <div role="group" aria-label="Loại người nhận" className="flex gap-2">
        {PAYEE_CHIPS.map((c) => (
          <Button key={c.value} size="sm" aria-pressed={payeeType === c.value} variant={payeeType === c.value ? 'default' : 'outline'} onClick={() => { setPayeeType(c.value); setPage(1) }}>
            {c.label}
          </Button>
        ))}
      </div>

      <DataTable
        columns={COLUMNS}
        rows={rows}
        rowKey={(r) => r.itemId}
        loading={detail.loading && rows.length === 0}
        emptyMessage={payeeType ? 'Kỳ này không có người nhận loại này' : 'Kỳ này chưa có người nhận nào (tháng không có ca hoàn tất)'}
        pagination={{ page: detail.data.page, pageSize: PAGE_SIZE, total: detail.data.total, onPageChange: setPage }}
      />
    </div>
  )
}

function ExportBar({ batch }: { batch: PayoutBatch }) {
  const [busy, setBusy] = useState<ExportType | null>(null)
  const [message, setMessage] = useState<{ text: string; ok: boolean } | null>(null)
  const inFlight = useRef(false)

  async function run(type: ExportType) {
    if (inFlight.current) return
    inFlight.current = true
    setBusy(type)
    setMessage(null)
    try {
      const name = await downloadExport(batch.batchId, type, exportFallbackName(batch.periodMonth, type))
      setMessage({ text: `Đã tải ${name}.`, ok: true })
    } catch (e) {
      setMessage({ text: e instanceof ApiError ? e.message : 'Đã xảy ra lỗi.', ok: false })
    } finally {
      inFlight.current = false
      setBusy(null)
    }
  }

  return (
    <section aria-label="Xuất file chuyển khoản" className="rounded-xl border border-border bg-card p-4">
      <h2 className="text-sm font-medium">Xuất file chuyển khoản (.xlsx)</h2>
      <p className="mt-1 text-xs text-muted-foreground">Người nhận có số dư 0 không có trong file. Mỗi lần xuất thay file đã lưu trước đó của kỳ.</p>
      <div className="mt-3 flex flex-wrap gap-2">
        {EXPORTS.map((x) => (
          <Button key={x.type} variant="outline" disabled={busy !== null} onClick={() => void run(x.type)}>
            {busy === x.type ? 'Đang tải…' : x.label}
          </Button>
        ))}
      </div>
      {message && (
        <div role={message.ok ? 'status' : 'alert'} className={cn('mt-3 text-sm', message.ok ? 'text-emerald-700' : 'text-destructive')}>
          {message.text}
        </div>
      )}
    </section>
  )
}

function DraftActions({ batch, onChanged }: { batch: PayoutBatch; onChanged: () => void }) {
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState<'build' | 'confirm' | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  const inFlight = useRef(false)

  async function run(kind: 'build' | 'confirm') {
    if (inFlight.current) return
    inFlight.current = true
    setBusy(kind)
    setFailure(null)
    try {
      if (kind === 'build') await payoutsApi.build(batch.periodMonth)
      else await payoutsApi.confirm(batch.batchId)
      setConfirming(false)
      onChanged()
    } catch (e) {
      setFailure(e instanceof ApiError ? (kind === 'build' ? buildError(e) : confirmError(e)) : 'Đã xảy ra lỗi.')
      setConfirming(false)
      if (e instanceof ApiError && e.status === 409) onChanged() // the batch changed under us: show its real state
    } finally {
      inFlight.current = false
      setBusy(null)
    }
  }

  return (
    <section aria-label="Giải ngân" className="rounded-xl border border-border bg-card p-4">
      <h2 className="text-sm font-medium">Giải ngân kỳ này</h2>
      <p className="mt-1 text-xs text-muted-foreground">Quy trình: xuất file, chuyển khoản tại ngân hàng, rồi xác nhận ở đây. Chưa xác nhận thì kỳ vẫn dựng lại được.</p>

      {failure && (
        <div role="alert" className="mt-3 rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm">
          {failure}
        </div>
      )}

      {!confirming ? (
        <div className="mt-3 flex flex-wrap gap-2">
          <Button variant="outline" disabled={busy !== null} onClick={() => void run('build')}>
            {busy === 'build' ? 'Đang dựng lại…' : 'Dựng lại từ dữ liệu hiện tại'}
          </Button>
          <Button disabled={busy !== null || batch.itemCount === 0} onClick={() => setConfirming(true)}>
            Xác nhận giải ngân
          </Button>
          {batch.itemCount === 0 && <span className="self-center text-xs text-muted-foreground">Kỳ chưa có người nhận nên chưa thể giải ngân.</span>}
        </div>
      ) : (
        <div className="mt-3 flex flex-col gap-2 rounded-lg border border-border p-3 text-sm">
          <div className="font-medium">Xác nhận giải ngân?</div>
          <ul className="list-disc pl-5">
            {confirmLines(batch).map((line) => (
              <li key={line}>{line}</li>
            ))}
          </ul>
          <div className="flex gap-2">
            <Button disabled={busy !== null} onClick={() => void run('confirm')}>
              {busy === 'confirm' ? 'Đang xác nhận…' : 'Xác nhận đã chuyển khoản'}
            </Button>
            <Button variant="outline" disabled={busy !== null} onClick={() => setConfirming(false)}>
              Quay lại
            </Button>
          </div>
        </div>
      )}
    </section>
  )
}
