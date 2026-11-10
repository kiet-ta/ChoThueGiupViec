import { useState } from 'react'
import { DataTable, type Column } from '@/components/data-table'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { ApiError, apiClient } from '@/services/api'
import { cn } from '@/lib/utils'
import { ENTRY_TYPE_OPTIONS, EMPTY_FILTERS, filterErrors, toQuery } from '../ledger/view'
import type { LedgerFilters } from '../ledger/types'
import { PAGE_SIZE } from './api'
import { downloadAgencyStatement } from './ledger-download'
import { Alert, PageHeader, Panel } from './shared'
import {
  WALLET_OPTIONS,
  balanceBlocks,
  historyRow,
  visibleEntries,
  walletNote,
  type AgencyLedgerPage,
  type HistoryRow,
  type Wallet,
} from './ledger-view'

const FIELD = 'h-9 rounded-lg border border-input bg-background px-3 text-sm'

const COLUMNS: Column<HistoryRow>[] = [
  { id: 'time', header: 'Thời gian', cell: (r) => <span className="whitespace-nowrap">{r.time}</span> },
  { id: 'code', header: 'Mã GD', cell: (r) => <span className="font-mono text-xs font-semibold">{r.code}</span> },
  { id: 'account', header: 'Ví', cell: (r) => r.account },
  {
    id: 'type',
    header: 'Loại',
    cell: (r) => (r.settlement ? <StatusBadge tone="accent">{r.type}</StatusBadge> : r.type),
  },
  { id: 'counterparty', header: 'Đối tác', cell: (r) => r.counterparty },
  {
    id: 'amount',
    header: 'Số tiền',
    align: 'right',
    cell: (r) => (
      <span className={cn('font-semibold tabular-nums', r.tone === 'in' && 'text-emerald-700', r.tone === 'out' && 'text-destructive')}>{r.amount}</span>
    ),
  },
  { id: 'reference', header: 'Tham chiếu', cell: (r) => r.reference },
  { id: 'hash', header: 'Hash', cell: (r) => <span title={r.fullHash} className="font-mono text-xs text-muted-foreground">{r.hash}</span> },
]

/** WEB-LEDGER-15: the Partner Portal's transaction history: the agency's earnings wallet and escrow account with their balances (contract ledger.md 9). */
export function LedgerHistoryPage() {
  const [draft, setDraft] = useState<LedgerFilters>(EMPTY_FILTERS)
  const [applied, setApplied] = useState<LedgerFilters>(EMPTY_FILTERS)
  const [wallet, setWallet] = useState<Wallet>('ALL')
  const [page, setPage] = useState(1)
  const [exporting, setExporting] = useState(false)
  const [exportMessage, setExportMessage] = useState<string | null>(null)

  const errors = filterErrors({ ...draft, partyType: '', partyId: '' })
  const hasErrors = Object.keys(errors).length > 0

  const history = useResource(
    () => apiClient.get<AgencyLedgerPage>('/agencies/me/ledger', { query: toQuery({ ...applied, partyType: '', partyId: '' }, page, PAGE_SIZE) }),
    [applied, page],
  )
  const rows = history.data ? visibleEntries(history.data.items, wallet).map(historyRow) : []
  const blocks = balanceBlocks(history.data)
  const note = walletNote(wallet)

  function apply() {
    if (hasErrors) return
    setPage(1)
    setApplied(draft)
  }

  function clear() {
    setDraft(EMPTY_FILTERS)
    setApplied(EMPTY_FILTERS)
    setWallet('ALL')
    setPage(1)
  }

  async function exportStatement() {
    setExporting(true)
    setExportMessage(null)
    try {
      await downloadAgencyStatement(applied)
    } catch (e) {
      setExportMessage(e instanceof ApiError ? e.message : 'Đã xảy ra lỗi.')
    } finally {
      setExporting(false)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader eyebrow="Tài khoản" title="Lịch sử giao dịch">
        Mọi khoản tiền vào ra của ví tiền công và quỹ ký quỹ của Agency, kèm mã giao dịch để đối chiếu. Cuối mỗi tháng hệ thống tự tất toán tiền công và ghi một dòng "Tất toán lương tháng".
      </PageHeader>

      <section aria-label="Số dư" className="grid gap-4 sm:grid-cols-2" aria-busy={history.loading}>
        {blocks.map((b) => (
          <Panel key={b.key} title={b.title}>
            <dl className="grid grid-cols-3 gap-3 text-sm">
              <div>
                <dt className="text-xs text-muted-foreground">Đầu kỳ</dt>
                <dd className="mt-1 text-lg font-semibold tabular-nums">{b.opening}</dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground">Cuối kỳ</dt>
                <dd className="mt-1 text-lg font-semibold tabular-nums">{b.closing}</dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground">Biến động</dt>
                <dd className="mt-1 text-lg font-semibold tabular-nums">{b.change}</dd>
              </div>
            </dl>
          </Panel>
        ))}
      </section>

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
          Loại giao dịch
          <select className={FIELD} value={draft.type} onChange={(e) => setDraft({ ...draft, type: e.target.value })}>
            {ENTRY_TYPE_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
        </label>
        <div role="group" aria-label="Ví" className="flex flex-col gap-1 text-sm font-medium">
          Ví
          <div className="flex gap-2">
            {WALLET_OPTIONS.map((o) => (
              <Button key={o.value} size="sm" aria-pressed={wallet === o.value} variant={wallet === o.value ? 'default' : 'outline'} onClick={() => setWallet(o.value)}>
                {o.label}
              </Button>
            ))}
          </div>
        </div>
        <Button onClick={apply} disabled={hasErrors}>
          Lọc
        </Button>
        <Button variant="outline" onClick={clear}>
          Xoá lọc
        </Button>
        <Button variant="outline" onClick={() => void exportStatement()} disabled={exporting}>
          {exporting ? 'Đang xuất…' : 'Xuất sao kê (.xlsx)'}
        </Button>
        {note && <p className="basis-full text-xs text-muted-foreground">{note}</p>}
        {exportMessage && (
          <div className="basis-full">
            <Alert>{exportMessage}</Alert>
          </div>
        )}
      </section>

      <DataTable
        columns={COLUMNS}
        rows={rows}
        rowKey={(r) => r.entryId}
        loading={history.loading && !history.data}
        error={history.error}
        onRetry={history.reload}
        emptyMessage="Chưa có giao dịch nào khớp bộ lọc."
        pagination={history.data ? { page: history.data.page, pageSize: PAGE_SIZE, total: history.data.total, onPageChange: setPage } : undefined}
      />
    </div>
  )
}
