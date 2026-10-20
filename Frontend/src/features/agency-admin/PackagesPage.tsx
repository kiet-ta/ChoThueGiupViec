import { useRef, useState } from 'react'
import { DataTable, type Column } from '@/components/data-table'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { cn } from '@/lib/utils'
import { ApiError } from '@/services/api'
import { agencyAdminApi, HISTORY_PAGE_SIZE } from './api'
import type { SubscriptionPackage } from './types'
import { buildEdit, historyRow, packageFormOf, packageRow, REASON_MAX_LENGTH, type PackageFormErrors } from './view'

type Row = ReturnType<typeof packageRow>

const COLUMNS: Column<Row>[] = [
  { id: 'code', header: 'Mã gói', cell: (r) => <span className="font-semibold">{r.code}</span> },
  { id: 'name', header: 'Tên', cell: (r) => r.name },
  { id: 'tier', header: 'Bậc', cell: (r) => r.tier },
  { id: 'price', header: 'Giá', align: 'right', cell: (r) => <span className="tabular-nums">{r.price}</span> },
  { id: 'quota', header: 'Hạn mức', align: 'right', cell: (r) => r.quota },
  { id: 'commission', header: 'Hoa hồng', align: 'right', cell: (r) => r.commission },
  { id: 'active', header: 'Trạng thái', cell: (r) => <StatusBadge tone={r.active ? 'success' : 'neutral'}>{r.active ? 'Đang bán' : 'Tạm ẩn'}</StatusBadge> },
]

/** WEB-M5-08: Admin edits a subscription package (price, quota, commission) with a mandatory reason and sees its history (decisions Q08, G-5, D9). */
export function PackagesPage() {
  const [version, setVersion] = useState(0)
  const [selected, setSelected] = useState<number | null>(null)
  const list = useResource(() => agencyAdminApi.packages(), [version])
  const packages = list.data ?? []
  const current = packages.find((p) => p.packageId === selected) ?? null

  return (
    <div className="flex flex-col gap-6">
      <header>
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Cấu hình · Gói thuê bao</div>
        <h1 className="text-2xl font-semibold">Gói thuê bao của đối tác</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Sửa giá, hạn mức thợ hoặc hoa hồng. Mọi lần sửa bắt buộc có lý do và được ghi vào lịch sử. Giá mới chỉ áp dụng cho lần mua sau; hoa hồng của
          ca đã tạo không đổi. Bấm vào một dòng để sửa.
        </p>
      </header>

      <DataTable
        columns={COLUMNS}
        rows={packages.map(packageRow)}
        rowKey={(r) => r.id}
        loading={list.loading && !list.data}
        error={list.error}
        onRetry={list.reload}
        emptyMessage="Chưa có gói nào. Dữ liệu mặc định được tạo khi khởi động máy chủ."
        onRowClick={(r) => setSelected(r.id)}
      />

      {current && (
        <EditPanel key={current.packageId} pkg={current} version={version} onSaved={() => setVersion((v) => v + 1)} onClose={() => setSelected(null)} />
      )}
    </div>
  )
}

function EditPanel({ pkg, version, onSaved, onClose }: { pkg: SubscriptionPackage; version: number; onSaved: () => void; onClose: () => void }) {
  const [form, setForm] = useState(() => packageFormOf(pkg))
  const [errors, setErrors] = useState<PackageFormErrors>({})
  const [message, setMessage] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [busy, setBusy] = useState(false)
  const inFlight = useRef(false)

  async function submit() {
    if (inFlight.current) return
    setSaved(false)
    setMessage(null)
    const { edit, errors: found } = buildEdit(pkg, form)
    setErrors(found)
    if (!edit) return

    inFlight.current = true
    setBusy(true)
    try {
      await agencyAdminApi.updatePackage(pkg.packageId, edit)
      setSaved(true)
      setForm({ ...form, reason: '' })
      onSaved()
    } catch (e) {
      if (e instanceof ApiError && e.status === 400) {
        const first = Object.values(e.fieldErrors)[0]?.[0]
        setMessage(first ?? e.message)
      } else {
        setMessage(e instanceof ApiError ? e.message || `Lỗi ${e.status}` : 'Đã xảy ra lỗi.')
      }
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  const field = (id: keyof PackageFormErrors, label: string, value: string, set: (v: string) => void, help: string) => (
    <div className="flex flex-col gap-1">
      <label htmlFor={`pkg-${id}`} className="font-medium">{label}</label>
      <input
        id={`pkg-${id}`}
        value={value}
        onChange={(e) => set(e.target.value)}
        inputMode="numeric"
        aria-invalid={Boolean(errors[id])}
        className={cn('rounded-lg border bg-background p-2 tabular-nums', errors[id] ? 'border-destructive' : 'border-border')}
      />
      <div className={cn('text-xs', errors[id] ? 'text-destructive' : 'text-muted-foreground')}>{errors[id] ?? help}</div>
    </div>
  )

  return (
    <section className="rounded-xl border border-border bg-card p-5 text-card-foreground" aria-label={`Sửa gói ${pkg.packageCode}`}>
      <div className="flex items-start justify-between gap-3">
        <h2 className="text-lg font-semibold">{pkg.packageName} ({pkg.packageCode})</h2>
        <Button variant="ghost" size="sm" onClick={onClose}>Đóng</Button>
      </div>

      <div className="mt-4 grid gap-6 lg:grid-cols-[360px_1fr]">
        <form
          className="flex flex-col gap-3 text-sm"
          noValidate
          onSubmit={(e) => {
            e.preventDefault()
            void submit()
          }}
        >
          {field('price', 'Giá (VND)', form.price, (v) => setForm({ ...form, price: v }), 'Số nguyên, 0 là miễn phí.')}
          {field('workerQuota', 'Hạn mức thợ', form.workerQuota, (v) => setForm({ ...form, workerQuota: v }), 'Số thợ tối đa của gói.')}
          {field('commissionPercent', 'Hoa hồng (%)', form.commissionPercent, (v) => setForm({ ...form, commissionPercent: v }), 'Từ 0 đến 100.')}

          <div className="flex flex-col gap-1">
            <label htmlFor="pkg-reason" className="font-medium">Lý do thay đổi</label>
            <textarea
              id="pkg-reason"
              value={form.reason}
              onChange={(e) => setForm({ ...form, reason: e.target.value })}
              rows={3}
              aria-invalid={Boolean(errors.reason)}
              className={cn('rounded-lg border bg-background p-2', errors.reason ? 'border-destructive' : 'border-border')}
            />
            <div className={cn('text-xs', errors.reason ? 'text-destructive' : 'text-muted-foreground')}>
              {errors.reason ?? `${form.reason.trim().length}/${REASON_MAX_LENGTH} ký tự. Bắt buộc.`}
            </div>
          </div>

          {errors.body && <div role="alert" className="rounded-lg border border-border bg-muted p-3">{errors.body}</div>}
          {message && <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/5 p-3">{message}</div>}
          {saved && <div role="status" className="rounded-lg border border-border bg-muted p-3">Đã lưu. Lịch sử đã được cập nhật.</div>}

          <div>
            <Button type="submit" disabled={busy}>{busy ? 'Đang lưu…' : 'Lưu thay đổi'}</Button>
          </div>
        </form>

        <History packageId={pkg.packageId} version={version} />
      </div>
    </section>
  )
}

function History({ packageId, version }: { packageId: number; version: number }) {
  const [page, setPage] = useState(1)
  const history = useResource(() => agencyAdminApi.packageHistory(packageId, page), [packageId, page, version])
  type HRow = ReturnType<typeof historyRow>
  const columns: Column<HRow>[] = [
    { id: 'when', header: 'Thời điểm', cell: (r) => r.when },
    { id: 'who', header: 'Người sửa', cell: (r) => r.who },
    { id: 'field', header: 'Trường', cell: (r) => r.field },
    { id: 'old', header: 'Cũ', align: 'right', cell: (r) => <span className="tabular-nums">{r.oldValue}</span> },
    { id: 'new', header: 'Mới', align: 'right', cell: (r) => <span className="tabular-nums">{r.newValue}</span> },
    { id: 'reason', header: 'Lý do', cell: (r) => r.reason },
  ]

  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-sm font-medium">Lịch sử thay đổi</h3>
      <DataTable
        columns={columns}
        rows={history.data ? history.data.items.map(historyRow) : []}
        rowKey={(r) => r.id}
        loading={history.loading && !history.data}
        error={history.error}
        onRetry={history.reload}
        emptyMessage="Gói này chưa từng được sửa."
        pagination={history.data ? { page: history.data.page, pageSize: HISTORY_PAGE_SIZE, total: history.data.total, onPageChange: setPage } : undefined}
      />
    </div>
  )
}
