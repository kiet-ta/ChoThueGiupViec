import { useRef, useState } from 'react'
import { DataTable, type Column } from '@/components/data-table'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { cn } from '@/lib/utils'
import { ApiError } from '@/services/api'
import { workersApi } from './api'
import type { EkycFilterStatus, EkycQueueItemResponse } from './types'
import {
  AUTO_APPROVE_THRESHOLD,
  PAGE_SIZE,
  REASON_MAX_LENGTH,
  TABS,
  ekycQueueRow,
  parseActionError,
  validateRejectionReason,
  type ActionError,
  type EkycRow,
} from './view'

const COLUMNS: Column<EkycRow>[] = [
  {
    id: 'worker',
    header: 'Thợ / SĐT',
    cell: (r) => (
      <div>
        <div className="font-semibold text-foreground">{r.fullName}</div>
        <div className="text-xs text-muted-foreground">{r.phoneNumber}</div>
      </div>
    ),
  },
  {
    id: 'nationalId',
    header: 'Số CCCD',
    cell: (r) => <span className="font-mono text-sm">{r.nationalId}</span>,
  },
  {
    id: 'confidence',
    header: 'Điểm tin cậy eKYC',
    cell: (r) => <StatusBadge tone={r.confidenceTone}>{r.confidenceText}</StatusBadge>,
  },
  {
    id: 'submittedAt',
    header: 'Thời gian nộp',
    cell: (r) => <span className="text-sm text-muted-foreground">{r.submittedAtFormatted}</span>,
  },
  {
    id: 'actions',
    header: 'Thao tác',
    align: 'right',
    cell: () => (
      <Button size="sm" variant="outline">
        Xem & Hậu kiểm
      </Button>
    ),
  },
]

type ReviewMode = 'idle' | 'confirmApprove' | 'reject'

/** Admin eKYC Manual Review Queue Page (WEB-M4-01, contract workers.md §2.2). */
export function EkycAuditQueuePage() {
  const [statusTab, setStatusTab] = useState<EkycFilterStatus>('PENDING')
  const [page, setPage] = useState(1)
  const [selectedWorkerId, setSelectedWorkerId] = useState<number | null>(null)
  const [refreshVersion, setRefreshVersion] = useState(0)

  const queueResource = useResource(
    () => workersApi.listEkycQueue(page, PAGE_SIZE),
    [page, statusTab, refreshVersion]
  )

  const rawItems = queueResource.data?.items ?? []
  const rows = rawItems.map(ekycQueueRow)
  const selectedItem = rawItems.find((i) => i.workerId === selectedWorkerId)

  function handleTabChange(nextStatus: EkycFilterStatus) {
    setStatusTab(nextStatus)
    setPage(1)
    setSelectedWorkerId(null)
  }

  return (
    <div className="flex flex-col gap-6">
      <header>
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
          MVP5 · BR-06 / BR-07 · Q05 eKYC · Quản Lý Thợ
        </div>
        <h1 className="text-2xl font-semibold">Hàng đợi hậu kiểm eKYC Thợ</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Xem xét & phê duyệt thủ công các hồ sơ eKYC dưới ngưỡng tự động {AUTO_APPROVE_THRESHOLD}% hoặc có cờ nghi vấn.
        </p>
      </header>

      <div role="tablist" aria-label="Trạng thái hàng đợi eKYC" className="flex gap-2">
        {TABS.map((tab) => (
          <Button
            key={tab.status}
            role="tab"
            aria-selected={statusTab === tab.status}
            variant={statusTab === tab.status ? 'default' : 'outline'}
            onClick={() => handleTabChange(tab.status)}
          >
            {tab.label}
          </Button>
        ))}
      </div>

      <DataTable
        columns={COLUMNS}
        rows={rows}
        rowKey={(r) => r.workerId}
        loading={queueResource.loading && !queueResource.data}
        error={queueResource.error}
        onRetry={queueResource.reload}
        emptyMessage={
          statusTab === 'PENDING'
            ? 'Không có hồ sơ eKYC nào đang chờ hậu kiểm'
            : 'Chưa có dữ liệu trong mục này'
        }
        onRowClick={(r) => setSelectedWorkerId(r.workerId)}
        pagination={
          queueResource.data
            ? {
                page: queueResource.data.page,
                pageSize: queueResource.data.pageSize,
                total: queueResource.data.totalCount,
                onPageChange: setPage,
              }
            : undefined
        }
      />

      {selectedItem && (
        <EkycDetailPanel
          key={selectedItem.workerId}
          item={selectedItem}
          onDecided={() => {
            setRefreshVersion((v) => v + 1)
            setSelectedWorkerId(null)
          }}
          onClose={() => setSelectedWorkerId(null)}
        />
      )}
    </div>
  )
}

function EkycDetailPanel({
  item,
  onDecided,
  onClose,
}: {
  item: EkycQueueItemResponse
  onDecided: () => void
  onClose: () => void
}) {
  const [mode, setMode] = useState<ReviewMode>('idle')
  const [rejectionReason, setRejectionReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [actionErr, setActionErr] = useState<ActionError | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const inFlight = useRef(false)

  const isUnderThreshold = item.confidenceScore < AUTO_APPROVE_THRESHOLD

  async function submitReview(approved: boolean) {
    if (inFlight.current) return
    inFlight.current = true
    setIsSubmitting(true)
    setActionErr(null)

    try {
      await workersApi.reviewEkyc(item.workerId, {
        approved,
        rejectionReason: approved ? null : rejectionReason.trim(),
      })
      onDecided()
    } catch (e) {
      if (e instanceof ApiError) {
        const err = parseActionError(e)
        if (e.status === 400 && e.fieldErrors.rejectionReason) {
          setReasonError(err.message)
        } else {
          setActionErr(err)
        }
      } else {
        setActionErr({ message: 'Đã xảy ra lỗi khi duyệt eKYC.', reasons: [] })
      }
    } finally {
      inFlight.current = false
      setIsSubmitting(false)
    }
  }

  function handleRejectSubmit() {
    const error = validateRejectionReason(rejectionReason)
    setReasonError(error)
    if (error) return
    void submitReview(false)
  }

  return (
    <section
      className="rounded-xl border border-border bg-card p-5 text-card-foreground shadow-sm"
      aria-label={`Hồ sơ eKYC thợ ${item.fullName}`}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-lg font-semibold">{item.fullName}</h2>
            <StatusBadge tone="neutral">CCCD: {item.nationalId}</StatusBadge>
            <StatusBadge tone={isUnderThreshold ? 'warning' : 'success'}>
              Điểm tin cậy: {item.confidenceScore.toFixed(1)}%
            </StatusBadge>
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            SĐT: {item.phoneNumber} — Nộp eKYC lúc: {new Date(item.submittedAt).toLocaleString('vi-VN')}
          </p>
        </div>
        <Button variant="ghost" size="sm" onClick={onClose}>
          Đóng
        </Button>
      </div>

      <div className="mt-5 grid gap-6 lg:grid-cols-[1fr_320px]">
        {/* Photo Comparison View */}
        <div className="flex flex-col gap-3">
          <h3 className="text-sm font-semibold text-foreground">Đối chiếu 3 ảnh hồ sơ eKYC</h3>
          <div className="grid gap-4 sm:grid-cols-3">
            <figure className="flex flex-col gap-2">
              <figcaption className="text-xs font-medium text-muted-foreground">1. Mặt trước CCCD</figcaption>
              <div className="relative aspect-4/3 overflow-hidden rounded-lg border border-border bg-muted">
                <img
                  src={item.frontCccdUrl}
                  alt="Mặt trước CCCD"
                  className="size-full object-cover"
                />
              </div>
            </figure>
            <figure className="flex flex-col gap-2">
              <figcaption className="text-xs font-medium text-muted-foreground">2. Mặt sau CCCD</figcaption>
              <div className="relative aspect-4/3 overflow-hidden rounded-lg border border-border bg-muted">
                <img
                  src={item.backCccdUrl}
                  alt="Mặt sau CCCD"
                  className="size-full object-cover"
                />
              </div>
            </figure>
            <figure className="flex flex-col gap-2">
              <figcaption className="text-xs font-medium text-muted-foreground">3. Chân dung Selfie</figcaption>
              <div className="relative aspect-4/3 overflow-hidden rounded-lg border border-border bg-muted">
                <img
                  src={item.selfieUrl}
                  alt="Chân dung selfie"
                  className="size-full object-cover"
                />
              </div>
            </figure>
          </div>
        </div>

        {/* Action Panel */}
        <div className="flex flex-col gap-4">
          <div className="rounded-lg bg-muted p-4 text-sm">
            <div className="font-medium text-foreground">Đánh giá hệ thống:</div>
            <div className="mt-1 text-xs text-muted-foreground">
              {isUnderThreshold
                ? `Điểm tin cậy (${item.confidenceScore.toFixed(1)}%) thấp hơn ngưỡng tự động (${AUTO_APPROVE_THRESHOLD}%). Cần Admin đối chiếu khuôn mặt trên selfie với ảnh trên CCCD.`
                : `Điểm tin cậy đạt ${item.confidenceScore.toFixed(1)}%. Hồ sơ hợp lệ.`}
            </div>
          </div>

          {actionErr && (
            <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive">
              {actionErr.message}
            </div>
          )}

          {mode === 'idle' && (
            <div className="flex flex-col gap-2">
              <Button size="lg" disabled={isSubmitting} onClick={() => setMode('confirmApprove')}>
                Duyệt eKYC (Phê duyệt tài khoản)
              </Button>
              <Button size="lg" variant="outline" disabled={isSubmitting} onClick={() => setMode('reject')}>
                Từ chối / Yêu cầu nộp lại
              </Button>
            </div>
          )}

          {mode === 'confirmApprove' && (
            <div className="flex flex-col gap-3 rounded-lg border border-border p-4 text-sm">
              <div className="font-medium">Xác nhận duyệt eKYC?</div>
              <div className="text-xs text-muted-foreground">
                Tài khoản thợ {item.fullName} sẽ được kích hoạt sang trạng thái Sẵn Sàng (IDLE) và có thể bắt đầu nhận ca làm.
              </div>
              <div className="flex gap-2">
                <Button disabled={isSubmitting} onClick={() => void submitReview(true)}>
                  {isSubmitting ? 'Đang duyệt…' : 'Xác nhận duyệt'}
                </Button>
                <Button variant="outline" disabled={isSubmitting} onClick={() => setMode('idle')}>
                  Hủy
                </Button>
              </div>
            </div>
          )}

          {mode === 'reject' && (
            <div className="flex flex-col gap-3 rounded-lg border border-border p-4 text-sm">
              <label htmlFor="reject-reason" className="font-medium">
                Lý do từ chối hồ sơ
              </label>
              <textarea
                id="reject-reason"
                value={rejectionReason}
                onChange={(e) => setRejectionReason(e.target.value)}
                rows={3}
                placeholder="VD: Ảnh CCCD bị mờ, không rõ số giấy tờ hoặc gương mặt selfie không trùng khớp."
                aria-invalid={reasonError !== null}
                className={cn(
                  'rounded-lg border bg-background p-2 text-sm',
                  reasonError ? 'border-destructive' : 'border-border'
                )}
              />
              <div className={cn('text-xs', reasonError ? 'text-destructive' : 'text-muted-foreground')}>
                {reasonError ?? `${rejectionReason.trim().length}/${REASON_MAX_LENGTH} ký tự.`}
              </div>
              <div className="flex gap-2">
                <Button variant="destructive" disabled={isSubmitting} onClick={handleRejectSubmit}>
                  {isSubmitting ? 'Đang gửi…' : 'Từ chối eKYC'}
                </Button>
                <Button
                  variant="outline"
                  disabled={isSubmitting}
                  onClick={() => {
                    setMode('idle')
                    setReasonError(null)
                  }}
                >
                  Hủy
                </Button>
              </div>
            </div>
          )}
        </div>
      </div>
    </section>
  )
}
