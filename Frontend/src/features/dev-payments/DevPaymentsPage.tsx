import { useRef, useState } from 'react'
import { DataTable, type Column } from '@/components/data-table'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { ApiError } from '@/services/api'
import { devPaymentsApi } from './api'
import { paymentRow, problemText } from './view'

type Row = ReturnType<typeof paymentRow>

/** UAT-PAY: confirm a sandbox payment of the Fake gateway as if its callback had arrived (Development backend only). */
export function DevPaymentsPage() {
  const [version, setVersion] = useState(0)
  const [message, setMessage] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<number | null>(null)
  const inFlight = useRef(false)
  const list = useResource(() => devPaymentsApi.list(), [version])
  const rows = (list.data ?? []).map(paymentRow)

  async function confirm(id: number) {
    if (inFlight.current) return
    inFlight.current = true
    setBusyId(id)
    setMessage(null)
    try {
      const result = await devPaymentsApi.confirm(id)
      setMessage(result.result === 'CONFIRMED' ? `Đã xác nhận thanh toán #${id}.` : `Thanh toán #${id} đã được xử lý trước đó.`)
      setVersion((v) => v + 1)
    } catch (e) {
      setMessage(e instanceof ApiError ? problemText(e.status, e.message) : 'Đã xảy ra lỗi.')
    } finally {
      inFlight.current = false
      setBusyId(null)
    }
  }

  const columns: Column<Row>[] = [
    { id: 'id', header: 'Mã', cell: (r) => `#${r.id}` },
    { id: 'subject', header: 'Thanh toán cho', cell: (r) => r.subject },
    { id: 'amount', header: 'Số tiền', cell: (r) => r.amount },
    { id: 'ref', header: 'Mã giao dịch', cell: (r) => r.reference },
    { id: 'created', header: 'Tạo lúc', cell: (r) => r.created },
    {
      id: 'action',
      header: '',
      cell: (r) => (
        <Button size="sm" disabled={busyId !== null || !r.isFake} onClick={() => void confirm(r.id)}>
          Xác nhận đã thanh toán
        </Button>
      ),
    },
  ]

  return (
    <div className="flex flex-col gap-6">
      <header>
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Công cụ dev · Thanh toán sandbox</div>
        <h1 className="text-2xl font-semibold">Xác nhận thanh toán giả lập</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Chỉ dùng khi backend chạy ở Development với cổng thanh toán giả. Nút này gửi đúng callback IPN của cổng giả, nên đơn, ký quỹ hoặc gói thuê bao được
          xử lý như khi khách thật trả tiền. Không có tiền thật.
        </p>
      </header>

      {message && <div role="status" className="rounded-lg border border-border bg-muted p-3 text-sm">{message}</div>}

      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(r) => r.id}
        loading={list.loading && !list.data}
        error={list.error}
        onRetry={list.reload}
        emptyMessage="Không có giao dịch nào đang chờ thanh toán."
        toolbar={<Button size="sm" variant="outline" onClick={() => setVersion((v) => v + 1)}>Làm mới</Button>}
      />
    </div>
  )
}
