import { useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { BeforeAfterViewer } from '@/components/photo-compare'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { formatDateTime, formatSlaRemaining, workerTypeLabel } from '@/lib/format'
import { ApiError } from '@/services/api'
import { disputesApi } from './api'
import type { CaseFile } from './types'
import { VerdictPanel, VerdictSummary } from './VerdictPanel'
import { categoryLabel, faultLabel, photoAssignmentIds, priorityInfo, safeEvidenceUrl, statusInfo, timelineRows, toComparePhotos, verdictError } from './view'

const IMAGE = /\.(png|jpe?g|gif|webp|avif)(\?.*)?$/i

/** WEB-M6-01 case page (Figma 66:2): the shift history, the evidence, the photos and the verdict of one dispute. */
export function DisputeCasePage() {
  const { disputeId: raw } = useParams()
  const disputeId = Number(raw)
  const valid = Number.isInteger(disputeId) && disputeId > 0
  const [version, setVersion] = useState(0)
  const file = useResource(() => (valid ? disputesApi.get(disputeId) : Promise.reject(new ApiError(404, 'Not found'))), [disputeId, version])
  const reload = () => setVersion((v) => v + 1)

  if (file.error && !file.data) {
    return (
      <div className="flex flex-col gap-4">
        <Link to="/admin/disputes" className="text-sm underline-offset-4 hover:underline">
          ← Về hàng đợi
        </Link>
        <div role="alert" className="flex items-center justify-between rounded-xl border border-destructive/40 bg-destructive/5 p-5 text-sm">
          <span>{valid ? `Không tải được hồ sơ khiếu nại: ${file.error}` : 'Mã khiếu nại không hợp lệ.'}</span>
          {valid && (
            <Button variant="outline" size="sm" onClick={file.reload}>
              Thử lại
            </Button>
          )}
        </div>
      </div>
    )
  }
  if (!file.data) {
    return (
      <div aria-busy="true" className="flex flex-col gap-4">
        <div className="h-8 w-80 animate-pulse rounded bg-muted" />
        <div className="h-40 animate-pulse rounded-xl bg-muted" />
        <div className="h-64 animate-pulse rounded-xl bg-muted" />
      </div>
    )
  }

  return <CaseView data={file.data} reload={reload} />
}

function CaseView({ data, reload }: { data: CaseFile; reload: () => void }) {
  const { dispute: d, summary: s } = data
  const open = d.disputeStatus === 'OPEN' || d.disputeStatus === 'IN_REVIEW'
  const status = statusInfo(d.disputeStatus)
  const priority = priorityInfo(s.priority)
  const timeline = timelineRows(data.shiftTimeline)
  const assignments = photoAssignmentIds(data.photos)
  const [assignment, setAssignment] = useState<number | null>(assignments[0] ?? null)
  const [taking, setTaking] = useState(false)
  const [takeError, setTakeError] = useState<string | null>(null)
  const inFlight = useRef(false)

  async function take() {
    if (inFlight.current) return
    inFlight.current = true
    setTaking(true)
    setTakeError(null)
    try {
      await disputesApi.take(d.disputeId)
      reload()
    } catch (e) {
      setTakeError(e instanceof ApiError ? verdictError(e).message : 'Đã xảy ra lỗi.')
      if (e instanceof ApiError && e.status === 409) reload()
    } finally {
      inFlight.current = false
      setTaking(false)
    }
  }

  const photos = assignment === null ? { before: [], after: [] } : toComparePhotos(data.photos, assignment)

  return (
    <div className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <Link to="/admin/disputes" className="text-sm underline-offset-4 hover:underline">
          ← Về hàng đợi
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-semibold">Khiếu nại #TC-{d.disputeId}</h1>
          <StatusBadge tone={status.tone}>{status.label}</StatusBadge>
          {open && <StatusBadge tone={priority.tone}>Ưu tiên {priority.label}</StatusBadge>}
          <StatusBadge tone="info">{categoryLabel(d.category)}</StatusBadge>
          {s.autoCancelled && <StatusBadge tone="warning">Auto-Cancelled Dispute</StatusBadge>}
        </div>
        <p className="text-sm text-muted-foreground">
          Đơn {s.orderCode} · Khách {s.customerName} · {d.raisedBy === 'CUSTOMER' ? 'Khách khiếu nại' : 'Thợ khiếu nại'} lúc {formatDateTime(d.createdAt)}
          {open && ` · SLA còn ${formatSlaRemaining(s.slaSecondsRemaining)}`}
        </p>
        <ul className="flex flex-wrap gap-x-6 gap-y-1 text-sm">
          {s.workers.map((w) => (
            <li key={w.workerId}>
              {w.fullName} <span className="text-muted-foreground">({workerTypeLabel(w.workerType, w.agencyName)})</span>
            </li>
          ))}
        </ul>
        {d.disputeStatus === 'OPEN' && (
          <div className="flex items-center gap-3">
            <Button onClick={() => void take()} disabled={taking}>
              {taking ? 'Đang nhận…' : 'Nhận xử lý'}
            </Button>
            {takeError && (
              <span role="alert" className="text-sm text-destructive">
                {takeError}
              </span>
            )}
          </div>
        )}
      </header>

      <section aria-label="Lịch sử ca làm" className="flex flex-col gap-3">
        <div>
          <h2 className="text-lg font-semibold">Toàn bộ lịch sử ca làm</h2>
          <p className="text-sm text-muted-foreground">Admin đối soát GPS, chỉ số VoL và checklist hoàn thành trước khi ra quyết định.</p>
        </div>
        <div className="grid gap-4 lg:grid-cols-[3fr_2fr]">
          <div className="rounded-xl border border-border bg-card p-4">
            <h3 className="mb-2 text-sm font-medium">Dòng thời gian ca làm</h3>
            {timeline.length === 0 ? (
              <div className="text-sm text-muted-foreground">Chưa có sự kiện nào của ca làm.</div>
            ) : (
              <ol className="flex flex-col gap-2 text-sm">
                {timeline.map((t) => (
                  <li key={t.key} className="flex items-start justify-between gap-3">
                    <span>
                      <span className="tabular-nums text-muted-foreground">{t.time}</span> · <span className="font-medium">{t.label}</span>
                      {t.detail && <span className="text-muted-foreground"> — {t.detail}</span>}
                    </span>
                    {t.badge && <StatusBadge tone={t.badge.tone}>{t.badge.text}</StatusBadge>}
                  </li>
                ))}
              </ol>
            )}
          </div>
          <div className="rounded-xl border border-border bg-card p-4">
            <h3 className="mb-2 text-sm font-medium">Checklist hoàn thành</h3>
            <div className="text-sm text-muted-foreground">Chưa có dữ liệu checklist cho ca này (hệ thống chưa lưu checklist).</div>
          </div>
        </div>
      </section>

      <section aria-label="Bằng chứng khiếu nại" className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">Bằng chứng khiếu nại</h2>
        <blockquote className="rounded-xl border border-border bg-muted/40 p-4 text-sm whitespace-pre-wrap">{d.description}</blockquote>
        {d.evidenceUrls.length > 0 && (
          <ul className="flex flex-wrap gap-3">
            {d.evidenceUrls.map((raw, i) => {
              const url = safeEvidenceUrl(raw)
              return (
                <li key={`${raw}-${i}`}>
                  {url === null ? (
                    <span className="inline-flex h-9 items-center rounded-lg border border-dashed border-border px-3 text-sm text-muted-foreground" title="Địa chỉ không hợp lệ nên không mở được">
                      Bằng chứng không mở được
                    </span>
                  ) : (
                    <a href={url} target="_blank" rel="noreferrer noopener" className="block">
                      {IMAGE.test(url) ? (
                        <img src={url} alt="Bằng chứng do người khiếu nại gửi" className="h-28 w-28 rounded-lg border border-border object-cover" />
                      ) : (
                        <span className="inline-flex h-9 items-center rounded-lg border border-border px-3 text-sm underline-offset-4 hover:underline">Mở tệp bằng chứng</span>
                      )}
                    </a>
                  )}
                </li>
              )
            })}
          </ul>
        )}

        <h3 className="mt-2 text-sm font-medium">Ảnh Before / After của ca</h3>
        {assignments.length === 0 ? (
          <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">Ca này chưa có ảnh Before/After.</div>
        ) : (
          <>
            {assignments.length > 1 && (
              <div role="tablist" aria-label="Ca làm" className="flex gap-2">
                {assignments.map((id, i) => (
                  <Button key={id} role="tab" aria-selected={assignment === id} size="sm" variant={assignment === id ? 'default' : 'outline'} onClick={() => setAssignment(id)}>
                    Thợ {i + 1} (ca #{id})
                  </Button>
                ))}
              </div>
            )}
            <BeforeAfterViewer key={assignment} before={photos.before} after={photos.after} />
          </>
        )}
      </section>

      {open ? (
        d.disputeStatus === 'OPEN' ? (
          <div className="rounded-xl border border-border bg-muted/40 p-5 text-sm">Nhận xử lý khiếu nại này (nút ở đầu trang) trước khi phán quyết. Admin khác sẽ thấy khiếu nại đang được xử lý.</div>
        ) : (
          <VerdictPanel disputeId={d.disputeId} workers={s.workers} onDone={reload} />
        )
      ) : (
        <VerdictSummary faultText={faultLabel(d.faultParty)} amount={d.compensationAmount} resolvedAt={formatDateTime(d.resolvedAt)} />
      )}
    </div>
  )
}
