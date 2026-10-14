import { useRef, useState } from 'react'
import { DataTable, type Column } from '@/components/data-table'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { ApiError } from '@/services/api'
import { agencyAdminApi } from './api'
import { appealRow, noteProblem } from './view'

type Row = ReturnType<typeof appealRow>

const FILTERS = [
  { value: 'PENDING', label: 'Chờ xử lý' },
  { value: 'ACCEPTED', label: 'Đã chấp nhận' },
  { value: 'REJECTED', label: 'Đã bác bỏ' },
]

/** WEB-M5-08: Admin decides escrow appeals; accepting appends a REVERSAL row (decisions Q09, D12). */
export function AppealsPage() {
  const [status, setStatus] = useState('PENDING')
  const [version, setVersion] = useState(0)
  const [selected, setSelected] = useState<number | null>(null)
  const [note, setNote] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const inFlight = useRef(false)
  const list = useResource(() => agencyAdminApi.appeals(status), [status, version])
  const rows = (list.data ?? []).map(appealRow)
  const current = rows.find((r) => r.id === selected) ?? null

  async function decide(accept: boolean) {
    if (!current || inFlight.current) return
    const problem = noteProblem(note)
    if (problem) return setMessage(problem)
    inFlight.current = true
    setBusy(true)
    setMessage(null)
    try {
      await agencyAdminApi.decideAppeal(current.id, accept, note.trim())
      setSelected(null)
      setNote('')
      setVersion((v) => v + 1)
    } catch (e) {
      setMessage(e instanceof ApiError ? (e.status === 409 ? 'Khiếu nại này đã được xử lý hoặc khoản phạt đã hoàn lại.' : e.message || `Lỗi ${e.status}`) : 'Đã xảy ra lỗi.')
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  const columns: Column<Row>[] = [
    { id: 'id', header: 'Mã', cell: (r) => `#${r.id}` },
    { id: 'agency', header: 'Doanh nghiệp', cell: (r) => r.agency },
    { id: 'txn', header: 'Khoản phạt', cell: (r) => r.txn },
    { id: 'reason', header: 'Lý do khiếu nại', cell: (r) => r.reason },
    { id: 'created', header: 'Gửi lúc', cell: (r) => r.created },
    { id: 'status', header: 'Trạng thái', cell: (r) => <StatusBadge tone={r.pending ? 'warning' : 'neutral'}>{r.status}</StatusBadge> },
  ]

  return (
    <div className="flex flex-col gap-6">
      <header>
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Xử lý · Ký quỹ</div>
        <h1 className="text-2xl font-semibold">Khiếu nại khoản phạt ký quỹ</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Chấp nhận sẽ hoàn lại tiền và điểm SLA bằng một dòng đảo (REVERSAL) và có thể đưa doanh nghiệp về trạng thái hoạt động. Quyết định được ghi vào
          nhật ký.
        </p>
      </header>

      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(r) => r.id}
        loading={list.loading && !list.data}
        error={list.error}
        onRetry={list.reload}
        emptyMessage="Không có khiếu nại nào ở trạng thái này."
        onRowClick={(r) => { setSelected(r.id); setMessage(null) }}
        toolbar={
          <label className="flex items-center gap-2 text-sm">
            Trạng thái
            <select value={status} onChange={(e) => { setStatus(e.target.value); setSelected(null) }} className="rounded-lg border border-border bg-background p-2">
              {FILTERS.map((f) => (
                <option key={f.value} value={f.value}>{f.label}</option>
              ))}
            </select>
          </label>
        }
      />

      {current && (
        <section className="rounded-xl border border-border bg-card p-5 text-card-foreground" aria-label={`Khiếu nại ${current.id}`}>
          <h2 className="text-lg font-semibold">Khiếu nại #{current.id} · khoản {current.txn}</h2>
          <p className="mt-2 text-sm">{current.reason}</p>
          {current.pending ? (
            <div className="mt-4 flex flex-col gap-3 text-sm">
              <label className="flex flex-col gap-1">
                Ghi chú quyết định (tuỳ chọn)
                <textarea value={note} onChange={(e) => setNote(e.target.value)} rows={2} className="rounded-lg border border-border bg-background p-2" />
              </label>
              {message && <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/5 p-3">{message}</div>}
              <div className="flex gap-2">
                <Button size="sm" disabled={busy} onClick={() => void decide(true)}>Chấp nhận và hoàn lại</Button>
                <Button size="sm" variant="outline" disabled={busy} onClick={() => void decide(false)}>Bác bỏ</Button>
              </div>
            </div>
          ) : (
            <p className="mt-3 text-sm text-muted-foreground">
              {current.status} lúc {current.decided}. {current.note && `Ghi chú: ${current.note}`}
            </p>
          )}
        </section>
      )}
    </div>
  )
}
