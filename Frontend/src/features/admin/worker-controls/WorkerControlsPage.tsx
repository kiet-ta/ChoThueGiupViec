import { useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { ApiError } from '@/services/api'
import { workerControlsApi } from './api'
import type { WorkerAction } from './types'
import {
  ACTIONS,
  REASON_MAX_LENGTH,
  actionError,
  actionInfo,
  parseWorkerId,
  successText,
  validateReason,
  type ActionError,
} from './view'

type Step = 'form' | 'confirm'

/** ADMIN-UAT-11: the Super-Freelancer flag (contract admin.md 2.5) and the unlock of a locked freelancer (2.6, decisions Q12 and Q15). */
export function WorkerControlsPage() {
  const [action, setAction] = useState<WorkerAction>('UNLOCK')
  const [workerText, setWorkerText] = useState('')
  const [reason, setReason] = useState('')
  const [step, setStep] = useState<Step>('form')
  const [idError, setIdError] = useState<string | null>(null)
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [failure, setFailure] = useState<ActionError | null>(null)
  const [done, setDone] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const inFlight = useRef(false)

  const info = actionInfo(action)

  function review() {
    const parsed = parseWorkerId(workerText)
    const idProblem = 'error' in parsed ? parsed.error : null
    const reasonProblem = validateReason(reason)
    setIdError(idProblem)
    setReasonError(reasonProblem)
    setFailure(null)
    setDone(null)
    if (idProblem === null && reasonProblem === null) setStep('confirm')
  }

  async function submit() {
    const parsed = parseWorkerId(workerText)
    if ('error' in parsed || inFlight.current) return
    inFlight.current = true
    setBusy(true)
    setFailure(null)
    try {
      await workerControlsApi.run(action, parsed.id, reason)
      setDone(successText(action, parsed.id))
      setStep('form')
      setReason('')
    } catch (e) {
      if (e instanceof ApiError) {
        if (e.status === 400 && e.fieldErrors.reason) {
          setReasonError(e.fieldErrors.reason[0])
          setStep('form')
        } else {
          setFailure(actionError(action, e))
          setStep('form')
        }
      } else {
        setFailure({ message: 'Đã xảy ra lỗi. Chưa có gì thay đổi, có thể thử lại.', reasons: [] })
        setStep('form')
      }
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  return (
    <div className="flex max-w-2xl flex-col gap-6">
      <header>
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">QUẢN LÝ NHÂN SỰ · Q12 · Q15</div>
        <h1 className="text-2xl font-semibold">Cờ Super-Freelancer và mở khoá thợ</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Mỗi thao tác cần lý do và được ghi vào nhật ký kiểm toán. Mã số thợ xem ở trang khiếu nại hoặc hậu kiểm eKYC.
        </p>
      </header>

      <fieldset className="flex flex-col gap-2" disabled={busy || step === 'confirm'}>
        <legend className="mb-1 text-sm font-medium">Thao tác</legend>
        {ACTIONS.map((a) => (
          <label key={a.value} className={cn('flex cursor-pointer gap-2 rounded-lg border p-3 text-sm', action === a.value ? 'border-primary' : 'border-border')}>
            <input type="radio" name="worker-action" checked={action === a.value} onChange={() => setAction(a.value)} className="mt-1" />
            <span>
              <span className="block font-medium">{a.label}</span>
              <span className="block text-muted-foreground">{a.hint}</span>
            </span>
          </label>
        ))}
      </fieldset>

      <div className="flex flex-col gap-1">
        <label htmlFor="worker-id" className="text-sm font-medium">
          Mã số thợ
        </label>
        <input
          id="worker-id"
          inputMode="numeric"
          value={workerText}
          onChange={(e) => setWorkerText(e.target.value)}
          disabled={busy || step === 'confirm'}
          aria-invalid={idError !== null}
          placeholder="VD: 12"
          className={cn('w-40 rounded-lg border bg-background p-2 text-sm', idError ? 'border-destructive' : 'border-border')}
        />
        {idError && <div className="text-xs text-destructive">{idError}</div>}
      </div>

      <div className="flex flex-col gap-1">
        <label htmlFor="worker-reason" className="text-sm font-medium">
          Lý do
        </label>
        <textarea
          id="worker-reason"
          rows={3}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          disabled={busy || step === 'confirm'}
          aria-invalid={reasonError !== null}
          className={cn('rounded-lg border bg-background p-2 text-sm', reasonError ? 'border-destructive' : 'border-border')}
        />
        <div className={cn('text-xs', reasonError ? 'text-destructive' : 'text-muted-foreground')}>
          {reasonError ?? `${reason.trim().length}/${REASON_MAX_LENGTH} ký tự. Ghi vào nhật ký kiểm toán.`}
        </div>
      </div>

      {failure && (
        <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive">
          <div>{failure.message}</div>
          {failure.reasons.length > 0 && (
            <ul className="mt-1 list-disc pl-5">
              {failure.reasons.map((r) => (
                <li key={r}>{r}</li>
              ))}
            </ul>
          )}
        </div>
      )}
      {done && (
        <div role="status" className="rounded-lg border border-border bg-muted p-3 text-sm">
          {done}
        </div>
      )}

      {step === 'form' ? (
        <div>
          <Button onClick={review} disabled={busy}>
            Xem lại và thực hiện
          </Button>
        </div>
      ) : (
        <div className="flex flex-col gap-3 rounded-lg border border-border p-4 text-sm">
          <div className="font-medium">
            {info.label} cho thợ #{workerText.trim()}?
          </div>
          <div className="text-xs text-muted-foreground">{info.effect}</div>
          <div className="flex gap-2">
            <Button disabled={busy} onClick={() => void submit()}>
              {busy ? 'Đang gửi…' : 'Xác nhận'}
            </Button>
            <Button variant="outline" disabled={busy} onClick={() => setStep('form')}>
              Quay lại
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}
