import { useRef, useState } from 'react'
import { DataTable, type Column } from '@/components/data-table'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { cn } from '@/lib/utils'
import { ApiError } from '@/services/api'
import { HISTORY_PAGE_SIZE, priceRulesApi } from './api'
import type { PriceRule } from './types'
import {
  historyRow,
  parseUnitPrice,
  REASON_MAX_LENGTH,
  ruleRow,
  saveError,
  sortRules,
  validateReason,
  type HistoryRow,
  type RuleRow,
  type SaveError,
} from './view'

const COLUMNS: Column<RuleRow>[] = [
  { id: 'tier', header: 'Phân khúc', cell: (r) => <span className="font-semibold">{r.tier}</span> },
  { id: 'bracket', header: 'Nhóm diện tích', cell: (r) => r.bracket },
  { id: 'price', header: 'Đơn giá / thợ / ca', align: 'right', cell: (r) => <span className="tabular-nums">{r.priceText}</span> },
  { id: 'updated', header: 'Cập nhật lúc', cell: (r) => r.updatedText },
  {
    id: 'active',
    header: 'Trạng thái',
    cell: (r) => <StatusBadge tone={r.active ? 'success' : 'neutral'}>{r.active ? 'Đang áp dụng' : 'Tạm tắt'}</StatusBadge>,
  },
]

const HISTORY_COLUMNS: Column<HistoryRow>[] = [
  { id: 'when', header: 'Thời điểm', cell: (r) => r.when },
  { id: 'who', header: 'Người sửa', cell: (r) => r.who },
  { id: 'old', header: 'Giá cũ', align: 'right', cell: (r) => <span className="tabular-nums">{r.oldPrice}</span> },
  { id: 'new', header: 'Giá mới', align: 'right', cell: (r) => <span className="tabular-nums">{r.newPrice}</span> },
  { id: 'reason', header: 'Lý do', cell: (r) => r.reason },
]

/** WEB-M2-01: Admin price table and its change history (contract booking.md 4.1-4.3, decisions Q01, G-5). */
export function PriceRulesPage() {
  const [selected, setSelected] = useState<number | null>(null)
  const [version, setVersion] = useState(0) // bumped after a save so the table and the history load again

  const list = useResource(() => priceRulesApi.list(), [version])
  const rules = list.data ? sortRules(list.data) : []
  const selectedRule = rules.find((r) => r.ruleId === selected) ?? null

  return (
    <div className="flex flex-col gap-6">
      <header>
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">MVP5 · Q01 · Bảng giá</div>
        <h1 className="text-2xl font-semibold">Bảng giá dịch vụ</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Đơn giá cho một thợ, một ca (tối đa 4 giờ). Giá mới chỉ áp dụng cho đơn tạo sau khi sửa; đơn đã tạo giữ nguyên giá đã chốt.
          Mọi lần sửa đều phải có lý do và được ghi vào lịch sử.
        </p>
      </header>

      <DataTable
        columns={COLUMNS}
        rows={rules.map(ruleRow)}
        rowKey={(r) => r.ruleId}
        loading={list.loading && !list.data}
        error={list.error}
        onRetry={list.reload}
        emptyMessage="Chưa có dòng giá nào. Dữ liệu mặc định được tạo khi khởi động máy chủ."
        onRowClick={(r) => setSelected(r.ruleId)}
      />

      {selectedRule && (
        <RulePanel
          key={selectedRule.ruleId}
          rule={selectedRule}
          version={version}
          onSaved={() => setVersion((v) => v + 1)}
          onClose={() => setSelected(null)}
        />
      )}
    </div>
  )
}

function RulePanel({
  rule,
  version,
  onSaved,
  onClose,
}: {
  rule: PriceRule
  version: number
  onSaved: () => void
  onClose: () => void
}) {
  const row = ruleRow(rule)
  const [priceText, setPriceText] = useState('')
  const [reason, setReason] = useState('')
  const [errors, setErrors] = useState<SaveError>({ message: null, unitPrice: null, reason: null })
  const [saved, setSaved] = useState(false)
  const [busy, setBusy] = useState(false)
  const inFlight = useRef(false) // a second click while a request runs must not send another one

  async function submit() {
    if (inFlight.current) return
    const price = parseUnitPrice(priceText, rule.unitPrice)
    const reasonProblem = validateReason(reason)
    setErrors({ message: null, unitPrice: price.error, reason: reasonProblem })
    setSaved(false)
    if (price.value === null || reasonProblem) return

    inFlight.current = true
    setBusy(true)
    try {
      await priceRulesApi.update(rule.ruleId, price.value, reason.trim())
      setPriceText('')
      setReason('')
      setSaved(true)
      onSaved()
    } catch (e) {
      setErrors(e instanceof ApiError ? saveError(e) : { message: 'Đã xảy ra lỗi.', unitPrice: null, reason: null })
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  return (
    <section className="rounded-xl border border-border bg-card p-5 text-card-foreground" aria-label={`Dòng giá ${row.tier}, ${row.bracket}`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold">
            {row.tier} · {row.bracket}
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Đơn giá hiện tại: <span className="font-medium text-foreground tabular-nums">{row.priceText}</span>
          </p>
        </div>
        <Button variant="ghost" size="sm" onClick={onClose}>
          Đóng
        </Button>
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
          <div className="flex flex-col gap-1">
            <label htmlFor="price-new" className="font-medium">
              Đơn giá mới (VND)
            </label>
            <input
              id="price-new"
              inputMode="numeric"
              autoComplete="off"
              value={priceText}
              onChange={(e) => setPriceText(e.target.value)}
              placeholder="Ví dụ: 270.000"
              aria-invalid={errors.unitPrice !== null}
              aria-describedby="price-new-help"
              className={cn('rounded-lg border bg-background p-2 tabular-nums', errors.unitPrice ? 'border-destructive' : 'border-border')}
            />
            <div id="price-new-help" className={cn('text-xs', errors.unitPrice ? 'text-destructive' : 'text-muted-foreground')}>
              {errors.unitPrice ?? 'Số nguyên, lớn hơn 0, không có phần lẻ.'}
            </div>
          </div>

          <div className="flex flex-col gap-1">
            <label htmlFor="price-reason" className="font-medium">
              Lý do thay đổi
            </label>
            <textarea
              id="price-reason"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              rows={3}
              aria-invalid={errors.reason !== null}
              aria-describedby="price-reason-help"
              className={cn('rounded-lg border bg-background p-2', errors.reason ? 'border-destructive' : 'border-border')}
            />
            <div id="price-reason-help" className={cn('text-xs', errors.reason ? 'text-destructive' : 'text-muted-foreground')}>
              {errors.reason ?? `${reason.trim().length}/${REASON_MAX_LENGTH} ký tự. Bắt buộc, được lưu vào lịch sử.`}
            </div>
          </div>

          {errors.message && (
            <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/5 p-3">
              {errors.message}
            </div>
          )}
          {saved && (
            <div role="status" className="rounded-lg border border-border bg-muted p-3">
              Đã lưu đơn giá mới. Chỉ các đơn tạo từ bây giờ dùng giá này.
            </div>
          )}

          <div>
            <Button type="submit" disabled={busy}>
              {busy ? 'Đang lưu…' : 'Lưu đơn giá'}
            </Button>
          </div>
        </form>

        <RuleHistory ruleId={rule.ruleId} version={version} />
      </div>
    </section>
  )
}

function RuleHistory({ ruleId, version }: { ruleId: number; version: number }) {
  const [page, setPage] = useState(1)
  const history = useResource(() => priceRulesApi.history(ruleId, page), [ruleId, page, version])

  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-sm font-medium">Lịch sử thay đổi</h3>
      <DataTable
        columns={HISTORY_COLUMNS}
        rows={history.data ? history.data.items.map(historyRow) : []}
        rowKey={(r) => r.logId}
        loading={history.loading && !history.data}
        error={history.error}
        onRetry={history.reload}
        emptyMessage="Dòng giá này chưa từng được sửa."
        pagination={
          history.data
            ? { page: history.data.page, pageSize: HISTORY_PAGE_SIZE, total: history.data.total, onPageChange: setPage }
            : undefined
        }
      />
    </div>
  )
}
