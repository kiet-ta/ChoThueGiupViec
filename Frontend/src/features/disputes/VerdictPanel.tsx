import { useRef, useState } from 'react'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { ApiError } from '@/services/api'
import { disputesApi } from './api'
import type { DisputeWorker } from './types'
import {
  EMPTY_FORM,
  effectText,
  faultOptions,
  NOTE_MAX_LENGTH,
  parseAmount,
  resolveRequest,
  validateResolve,
  verdictError,
  type ResolveErrors,
  type ResolveForm,
} from './view'
import { formatVnd } from '@/lib/format'

/** The verdict tool of the case page (Figma 66:2 "Bộ công cụ phán quyết"; contract disputes.md 2.3). */
export function VerdictPanel({ disputeId, workers, onDone }: { disputeId: number; workers: DisputeWorker[]; onDone: () => void }) {
  const [form, setForm] = useState<ResolveForm>(EMPTY_FORM)
  const [errors, setErrors] = useState<ResolveErrors>({})
  const [failure, setFailure] = useState<string | null>(null)
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const inFlight = useRef(false) // a second click while a request runs must not send another one

  const options = faultOptions(workers)
  const amount = parseAmount(form.amount)
  const canHaveMoney = form.fault === 'FREELANCER' || form.fault === 'AGENCY'

  function patch(next: Partial<ResolveForm>) {
    setForm((f) => ({ ...f, ...next }))
    setConfirming(false)
  }

  function review() {
    const problems = validateResolve(form, workers)
    setErrors(problems)
    setFailure(null)
    if (Object.keys(problems).length === 0) setConfirming(true)
  }

  async function submit() {
    if (inFlight.current) return
    inFlight.current = true
    setBusy(true)
    setFailure(null)
    try {
      await disputesApi.resolve(disputeId, resolveRequest(form))
      onDone()
    } catch (e) {
      if (e instanceof ApiError) {
        const err = verdictError(e)
        setErrors(err.fields)
        setFailure(err.message)
        setConfirming(false)
        if (e.status === 409) onDone() // somebody decided it first: show the real state
      } else {
        setFailure('Đã xảy ra lỗi.')
      }
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  return (
    <section className="rounded-xl border border-border bg-card p-5 text-card-foreground" aria-label="Bộ công cụ phán quyết">
      <h2 className="text-lg font-semibold">Bộ công cụ phán quyết</h2>
      <p className="mt-1 text-sm text-muted-foreground">
        Khách được bồi hoàn trước (nguyên tắc 0); chi phí chuyển sang bên có lỗi. Phán quyết không hoàn tác được.
      </p>

      <fieldset className="mt-4 flex flex-col gap-2" aria-describedby={errors.faultParty ? 'fault-error' : undefined}>
        <legend className="text-sm font-medium">Quyết định</legend>
        {options.map((o) => (
          <label key={o.value} className={cn('flex cursor-pointer items-start gap-3 rounded-lg border p-3 text-sm', form.fault === o.value ? 'border-primary bg-primary/5' : 'border-border')}>
            <input
              type="radio"
              name="fault"
              value={o.value}
              checked={form.fault === o.value}
              onChange={() => patch({ fault: o.value, lockWorker: o.value === 'FREELANCER' ? form.lockWorker : false })}
              className="mt-1"
            />
            <span>
              <span className="font-medium">{o.label}</span>
              <span className="block text-muted-foreground">{o.hint}</span>
            </span>
          </label>
        ))}
        {errors.faultParty && (
          <div id="fault-error" role="alert" className="text-sm text-destructive">
            {errors.faultParty}
          </div>
        )}
      </fieldset>

      {canHaveMoney && (
        <div className="mt-4 flex flex-col gap-1">
          <label htmlFor="compensation" className="text-sm font-medium">
            Số tiền bồi hoàn cho khách (VND)
          </label>
          <input
            id="compensation"
            inputMode="numeric"
            value={form.amount}
            onChange={(e) => patch({ amount: e.target.value })}
            placeholder="0"
            aria-invalid={errors.compensationAmount !== undefined}
            className={cn('h-9 rounded-lg border bg-background px-3 text-sm tabular-nums', errors.compensationAmount ? 'border-destructive' : 'border-input')}
          />
          <div className={cn('text-xs', errors.compensationAmount ? 'text-destructive' : 'text-muted-foreground')}>
            {errors.compensationAmount ??
              (amount !== null && amount > 0
                ? `Hoàn ${formatVnd(amount)} cho khách. Không vượt quá tổng giá trị các ca của đơn (máy chủ kiểm tra).`
                : 'Để trống nếu chỉ ghi nhận lỗi, không hoàn tiền.')}
          </div>
        </div>
      )}

      {form.fault === 'FREELANCER' && (
        <div className="mt-4 flex flex-col gap-1">
          <label className="flex items-start gap-2 text-sm">
            <input type="checkbox" checked={form.lockWorker} onChange={(e) => patch({ lockWorker: e.target.checked })} className="mt-1" />
            <span>
              <span className="font-medium">Khoá tài khoản thợ (cảnh cáo)</span>
              <span className="block text-muted-foreground">
                Hệ thống ghi nhận yêu cầu khoá vào nhật ký; việc khoá thật do module Thợ thực hiện và chưa nối, nên tài khoản chưa tự bị khoá.
              </span>
            </span>
          </label>
          {errors.lockWorker && (
            <div role="alert" className="text-sm text-destructive">
              {errors.lockWorker}
            </div>
          )}
        </div>
      )}

      <div className="mt-4 flex flex-col gap-1">
        <label htmlFor="verdict-note" className="text-sm font-medium">
          Ghi chú phán quyết
        </label>
        <textarea
          id="verdict-note"
          value={form.note}
          onChange={(e) => patch({ note: e.target.value })}
          rows={3}
          aria-invalid={errors.note !== undefined}
          className={cn('rounded-lg border bg-background p-2 text-sm', errors.note ? 'border-destructive' : 'border-input')}
        />
        <div className={cn('text-xs', errors.note ? 'text-destructive' : 'text-muted-foreground')}>
          {errors.note ?? `${form.note.trim().length}/${NOTE_MAX_LENGTH} ký tự. Ghi vào nhật ký kiểm toán.`}
        </div>
      </div>

      {failure && (
        <div role="alert" className="mt-4 rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm">
          {failure}
        </div>
      )}

      {!confirming ? (
        <div className="mt-4">
          <Button size="lg" onClick={review} disabled={busy}>
            Xem lại và phán quyết
          </Button>
        </div>
      ) : (
        <div className="mt-4 flex flex-col gap-2 rounded-lg border border-border p-3 text-sm">
          <div className="font-medium">Xác nhận phán quyết?</div>
          <ul className="list-disc pl-5">
            {effectText(form).map((line) => (
              <li key={line}>{line}</li>
            ))}
          </ul>
          <div className="flex gap-2">
            <Button disabled={busy} onClick={() => void submit()}>
              {busy ? 'Đang gửi…' : 'Xác nhận phán quyết'}
            </Button>
            <Button variant="outline" disabled={busy} onClick={() => setConfirming(false)}>
              Quay lại
            </Button>
          </div>
        </div>
      )}
    </section>
  )
}

/** A decided ticket: its verdict, read-only. */
export function VerdictSummary({ faultText, amount, resolvedAt }: { faultText: string; amount: number | null; resolvedAt: string }) {
  return (
    <section className="rounded-xl border border-border bg-card p-5 text-sm" aria-label="Phán quyết">
      <h2 className="text-lg font-semibold">Phán quyết</h2>
      <div className="mt-2 flex flex-wrap items-center gap-2">
        <StatusBadge tone="success">{faultText}</StatusBadge>
        {amount !== null && amount > 0 && <span>Bồi hoàn cho khách {formatVnd(amount)}</span>}
        <span className="text-muted-foreground">lúc {resolvedAt}</span>
      </div>
      <p className="mt-2 text-muted-foreground">Khiếu nại đã đóng, không thể thay đổi.</p>
    </section>
  )
}
