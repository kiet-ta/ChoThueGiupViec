import { useState } from 'react'
import { DataTable, type Column } from '@/components/data-table'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { formatVnd } from '../../lib/format.ts'
import { agenciesApi, PAGE_SIZE } from './api'
import { useAction } from './actions'
import { Alert, Metric, PageHeader, PayBox, Panel } from './shared'
import type { DepositPurchase } from './types'
import { agencyStatusText, escrowAlerts, parseAmount, txnRow, type TxnRow } from './view'

/** WEB-M5-04: escrow balance, top-up by QR, the ledger and the 48-hour appeal (decisions Q09, D12). */
export function EscrowPage() {
  const [page, setPage] = useState(1)
  const state = useResource(() => agenciesApi.escrow(), [])
  const ledger = useResource(() => agenciesApi.escrowTransactions(page), [page])
  const [amount, setAmount] = useState('')
  const [deposit, setDeposit] = useState<DepositPurchase | null>(null)
  const [amountError, setAmountError] = useState<string | null>(null)
  const [appealFor, setAppealFor] = useState<number | null>(null)
  const [reason, setReason] = useState('')
  const [appealNotice, setAppealNotice] = useState<string | null>(null)
  const topUp = useAction()
  const appeal = useAction()

  async function startDeposit() {
    const value = parseAmount(amount)
    if (value === null) return setAmountError('Nhập số tiền nguyên dương (VND).')
    setAmountError(null)
    const result = await topUp.run(() => agenciesApi.deposit(value))
    if (result) setDeposit(result)
  }

  async function sendAppeal() {
    if (appealFor === null) return
    if (!reason.trim()) return appeal.setError('Hãy nêu lý do khiếu nại.')
    const result = await appeal.run(() => agenciesApi.appeal(appealFor, reason.trim()))
    if (result) {
      setAppealFor(null)
      setReason('')
      setAppealNotice('Đã gửi khiếu nại. Admin sẽ xem xét thủ công.')
      ledger.reload()
    }
  }

  const columns: Column<TxnRow>[] = [
    { id: 'when', header: 'Thời điểm', cell: (r) => r.when },
    { id: 'type', header: 'Loại', cell: (r) => r.typeText },
    { id: 'amount', header: 'Số tiền', align: 'right', cell: (r) => <span className="tabular-nums">{r.amountText}</span> },
    { id: 'balance', header: 'Số dư sau', align: 'right', cell: (r) => <span className="tabular-nums">{r.balanceText}</span> },
    { id: 'points', header: 'Điểm SLA', align: 'right', cell: (r) => r.pointsText },
    { id: 'reason', header: 'Lý do', cell: (r) => r.reason },
    {
      id: 'action',
      header: '',
      cell: (r) =>
        r.canAppeal ? (
          <Button variant="outline" size="sm" onClick={() => { setAppealFor(r.id); setAppealNotice(null) }}>
            Khiếu nại
          </Button>
        ) : null,
    },
  ]

  const s = state.data

  return (
    <div className="flex flex-col gap-6">
      <PageHeader eyebrow="Đối tác · Ký quỹ" title="Quỹ ký quỹ và điểm SLA">
        Ký quỹ tối thiểu để nhận đơn Premium. Khi vi phạm, hệ thống khấu trừ hoàn tiền khách và chi phí cứu hộ (không xuống dưới 0). Bạn được khiếu nại
        trong 48 giờ.
      </PageHeader>

      {state.error && <Alert>{state.error}</Alert>}
      {s && (
        <>
          <div className="grid gap-4 md:grid-cols-3">
            <Metric label="Số dư ký quỹ" value={formatVnd(s.escrowDepositBalance)} />
            <Metric label="Trạng thái" value={agencyStatusText(s.agencyStatus)} />
            <Metric label="Điểm SLA" value={String(s.slaScore)} />
          </div>
          {escrowAlerts(s).map((m) => (
            <Alert key={m}>{m}</Alert>
          ))}
        </>
      )}

      <Panel title="Nạp ký quỹ">
        <form
          className="flex flex-wrap items-end gap-3 text-sm"
          onSubmit={(e) => {
            e.preventDefault()
            void startDeposit()
          }}
        >
          <label className="flex flex-col gap-1">
            Số tiền (VND)
            <input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="numeric" placeholder="5.000.000" className="rounded-lg border border-border bg-background p-2 tabular-nums" />
          </label>
          <Button type="submit" size="sm" disabled={topUp.busy}>{topUp.busy ? 'Đang tạo mã…' : 'Nạp tiền'}</Button>
        </form>
        {amountError && <div className="mt-2 text-sm text-destructive">{amountError}</div>}
        {topUp.error && <div className="mt-2"><Alert>{topUp.error}</Alert></div>}
        {deposit && (
          <div className="mt-3 flex flex-col gap-2">
            <PayBox qrPayload={deposit.qrPayload} payUrl={deposit.payUrl} expiresAt={deposit.expiresAt} />
            <div>
              <Button variant="ghost" size="sm" onClick={() => { setDeposit(null); state.reload(); ledger.reload() }}>
                Tôi đã thanh toán, tải lại
              </Button>
            </div>
          </div>
        )}
      </Panel>

      {appealFor !== null && (
        <Panel title={`Khiếu nại khoản #${appealFor}`}>
          <textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={3} maxLength={500} className="w-full rounded-lg border border-border bg-background p-2 text-sm" aria-label="Lý do khiếu nại" />
          {appeal.error && <div className="mt-2"><Alert>{appeal.error}</Alert></div>}
          <div className="mt-3 flex gap-2">
            <Button size="sm" disabled={appeal.busy} onClick={() => void sendAppeal()}>{appeal.busy ? 'Đang gửi…' : 'Gửi khiếu nại'}</Button>
            <Button variant="ghost" size="sm" onClick={() => setAppealFor(null)}>Đóng</Button>
          </div>
        </Panel>
      )}
      {appealNotice && <Alert tone="info">{appealNotice}</Alert>}

      <DataTable
        columns={columns}
        rows={ledger.data ? ledger.data.items.map(txnRow) : []}
        rowKey={(r) => r.id}
        loading={ledger.loading && !ledger.data}
        error={ledger.error}
        onRetry={ledger.reload}
        emptyMessage="Chưa có giao dịch ký quỹ."
        pagination={ledger.data ? { page: ledger.data.page, pageSize: PAGE_SIZE, total: ledger.data.total, onPageChange: setPage } : undefined}
      />
    </div>
  )
}
