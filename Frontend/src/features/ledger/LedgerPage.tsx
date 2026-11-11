import { useState } from 'react'
import { DataTable, type Column, type SortState } from '@/components/data-table'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { errorText, useResource } from '@/hooks/use-resource'
import { ApiError } from '@/services/api'
import { ledgerApi, PAGE_SIZE } from './api'
import { downloadStatement } from './download'
import type { ChainResult, LedgerFilters } from './types'
import {
  ENTRY_TYPE_OPTIONS,
  EMPTY_FILTERS,
  LEDGER_DEFAULT_SORT,
  LEDGER_SORT,
  NO_DISCREPANCY,
  PARTY_TYPE_OPTIONS,
  entryRow,
  exportHint,
  filterErrors,
  reconciliationRows,
  summaryCards,
  verifyMessage,
  type EntryRow,
} from './view'

const COLUMNS: Column<EntryRow>[] = [
  { id: 'code', header: 'Mã GD', sortKey: LEDGER_SORT.code, sortLabel: 'mã giao dịch', cell: (r) => <span className="font-mono text-xs font-semibold">{r.code}</span> },
  { id: 'time', header: 'Thời gian', sortKey: LEDGER_SORT.time, sortLabel: 'thời gian', cell: (r) => <span className="whitespace-nowrap">{r.time}</span> },
  { id: 'from', header: 'Bên chuyển', cell: (r) => r.from },
  { id: 'to', header: 'Bên nhận', cell: (r) => r.to },
  { id: 'type', header: 'Loại', cell: (r) => r.type },
  { id: 'amount', header: 'Số tiền', align: 'right', sortKey: LEDGER_SORT.amount, sortLabel: 'số tiền', cell: (r) => <span className="tabular-nums">{r.amount}</span> },
  { id: 'reference', header: 'Tham chiếu', cell: (r) => r.reference },
  { id: 'hash', header: 'Hash', cell: (r) => <span title={r.fullHash} className="font-mono text-xs text-muted-foreground">{r.hash}</span> },
]

const FIELD = 'h-9 rounded-lg border border-input bg-background px-3 text-sm'

/** WEB-LEDGER-13: the system ledger for the Admin: totals, filtered history, chain check, statement and source reconciliation (contract ledger.md 9). */
export function LedgerPage() {
  const [draft, setDraft] = useState<LedgerFilters>(EMPTY_FILTERS)
  const [applied, setApplied] = useState<LedgerFilters>(EMPTY_FILTERS)
  const [page, setPage] = useState(1)
  const [sort, setSort] = useState<SortState | null>(LEDGER_DEFAULT_SORT)
  const [verify, setVerify] = useState<{ busy: boolean; result: ChainResult | null; error: string | null }>({ busy: false, result: null, error: null })
  const [exporting, setExporting] = useState(false)
  const [exportMessage, setExportMessage] = useState<string | null>(null)

  const errors = filterErrors(draft)
  const hasErrors = Object.keys(errors).length > 0

  const summary = useResource(() => ledgerApi.summary(), [])
  const list = useResource(() => ledgerApi.list(applied, page, sort), [applied, page, sort])
  const reconciliation = useResource(() => ledgerApi.reconciliation(), [])

  const cards = summaryCards(summary.data)
  const rows = list.data ? list.data.items.map(entryRow) : []
  const gaps = reconciliationRows(reconciliation.data)

  function apply() {
    if (hasErrors) return
    setPage(1)
    setApplied(draft)
  }

  function clear() {
    setDraft(EMPTY_FILTERS)
    setPage(1)
    setApplied(EMPTY_FILTERS)
  }

  async function checkChain() {
    setVerify({ busy: true, result: null, error: null })
    try {
      setVerify({ busy: false, result: await ledgerApi.verify(), error: null })
    } catch (e) {
      setVerify({ busy: false, result: null, error: errorText(e) })
    }
  }

  async function exportStatement() {
    if (hasErrors) return
    setExporting(true)
    setExportMessage(null)
    try {
      await downloadStatement(applied)
    } catch (e) {
      setExportMessage(e instanceof ApiError ? e.message : 'Đã xảy ra lỗi.')
    } finally {
      setExporting(false)
    }
  }

  const verified = verify.result ? verifyMessage(verify.result) : null

  return (
    <div className="flex flex-col gap-6">
      <header>
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Tài chính · Đối soát</div>
        <h1 className="text-2xl font-semibold">Sổ cái</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Mọi dòng tiền ảo của hệ thống, chỉ thêm không sửa. Mỗi bút toán nối với bút toán trước bằng chuỗi hash để phát hiện chỉnh sửa. Hệ thống không chuyển tiền thật.
        </p>
      </header>

      <section aria-label="Tổng quan" className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5" aria-busy={summary.loading}>
        {cards.map((c) => (
          <div key={c.key} className="rounded-xl border border-border bg-card p-5 text-card-foreground">
            <h2 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{c.title}</h2>
            <div className="mt-3 text-2xl font-semibold tabular-nums">{summary.loading && !summary.data ? '…' : c.value}</div>
            <div className="mt-1 text-xs text-muted-foreground">{c.hint}</div>
          </div>
        ))}
      </section>
      {summary.error && (
        <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm">
          Không tải được tổng quan: {summary.error}
        </div>
      )}

      <section aria-label="Bộ lọc" className="flex flex-wrap items-end gap-3 rounded-xl border border-border bg-card p-4">
        <label className="flex flex-col gap-1 text-sm font-medium">
          Từ ngày
          <input type="date" className={FIELD} value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} />
          {errors.from && <span className="text-xs font-normal text-destructive">{errors.from}</span>}
        </label>
        <label className="flex flex-col gap-1 text-sm font-medium">
          Đến ngày
          <input type="date" className={FIELD} value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} />
          {errors.to && <span className="text-xs font-normal text-destructive">{errors.to}</span>}
        </label>
        <label className="flex flex-col gap-1 text-sm font-medium">
          Loại bút toán
          <select className={FIELD} value={draft.type} onChange={(e) => setDraft({ ...draft, type: e.target.value })}>
            {ENTRY_TYPE_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-sm font-medium">
          Bên
          <select className={FIELD} value={draft.partyType} onChange={(e) => setDraft({ ...draft, partyType: e.target.value })}>
            {PARTY_TYPE_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
          {errors.partyType && <span className="text-xs font-normal text-destructive">{errors.partyType}</span>}
        </label>
        <label className="flex flex-col gap-1 text-sm font-medium">
          Mã bên
          <input
            inputMode="numeric"
            className={`${FIELD} w-28`}
            placeholder="Ví dụ 7"
            value={draft.partyId}
            onChange={(e) => setDraft({ ...draft, partyId: e.target.value.trim() })}
          />
          {errors.partyId && <span className="text-xs font-normal text-destructive">{errors.partyId}</span>}
        </label>
        <Button onClick={apply} disabled={hasErrors}>
          Lọc
        </Button>
        <Button variant="outline" onClick={clear}>
          Xoá lọc
        </Button>
      </section>

      <section aria-label="Công cụ sổ" className="flex flex-wrap items-center gap-3 rounded-xl border border-border bg-card p-4">
        <Button variant="outline" onClick={() => void checkChain()} disabled={verify.busy}>
          {verify.busy ? 'Đang kiểm tra…' : 'Kiểm tra chuỗi'}
        </Button>
        {verified && <StatusBadge tone={verified.tone}>{verified.text}</StatusBadge>}
        {verify.error && <StatusBadge tone="danger">{verify.error}</StatusBadge>}
        <span className="mx-2 hidden h-6 w-px bg-border sm:block" />
        <Button variant="outline" onClick={() => void exportStatement()} disabled={exporting || hasErrors}>
          {exporting ? 'Đang xuất…' : 'Xuất sao kê'}
        </Button>
        <span className="text-xs text-muted-foreground">{exportHint(applied)}</span>
        {exportMessage && (
          <div role="alert" className="basis-full rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm">
            {exportMessage}
          </div>
        )}
      </section>

      <DataTable
        columns={COLUMNS}
        sort={sort}
        onSortChange={(next) => {
          setPage(1)
          setSort(next)
        }}
        rows={rows}
        rowKey={(r) => r.entryId}
        loading={list.loading && !list.data}
        error={list.error}
        onRetry={list.reload}
        emptyMessage="Chưa có bút toán nào khớp bộ lọc."
        pagination={list.data ? { page: list.data.page, pageSize: PAGE_SIZE, total: list.data.total, onPageChange: setPage } : undefined}
      />

      <section aria-label="Đối soát nguồn" className="rounded-xl border border-border bg-card p-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-base font-semibold">Đối soát nguồn ↔ sổ</h2>
            <p className="text-xs text-muted-foreground">So thanh toán, ca hoàn thành, khiếu nại, ký quỹ với sổ. Khoản thiếu được hệ thống tự ghi bù mỗi 5 phút.</p>
          </div>
          <Button variant="outline" onClick={reconciliation.reload} disabled={reconciliation.loading}>
            {reconciliation.loading ? 'Đang kiểm tra…' : 'Kiểm tra lại'}
          </Button>
        </div>
        {reconciliation.error ? (
          <p role="alert" className="mt-3 text-sm text-destructive">
            Không đối soát được: {reconciliation.error}
          </p>
        ) : reconciliation.data && gaps.length === 0 ? (
          <p className="mt-3 text-sm font-medium text-emerald-700">{NO_DISCREPANCY}</p>
        ) : (
          gaps.length > 0 && (
            <table className="mt-3 w-full text-sm">
              <thead className="text-left text-xs text-muted-foreground">
                <tr>
                  <th className="py-1 font-medium">Loại</th>
                  <th className="py-1 text-right font-medium">Số dòng thiếu</th>
                  <th className="py-1 text-right font-medium">Số tiền thiếu</th>
                  <th className="py-1 text-right font-medium">Sổ ghi dư so với nguồn</th>
                </tr>
              </thead>
              <tbody>
                {gaps.map((g) => (
                  <tr key={g.type} className="border-t border-border">
                    <td className="py-1.5">{g.type}</td>
                    <td className="py-1.5 text-right tabular-nums">{g.missingCount}</td>
                    <td className="py-1.5 text-right tabular-nums">{g.missing}</td>
                    <td className="py-1.5 text-right tabular-nums">{g.excess}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )
        )}
      </section>
    </div>
  )
}
