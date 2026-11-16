import { useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { ApiError } from '@/services/api'
import { workerControlsApi } from './api'
import type { AdminWorker, WorkerAction } from './types'
import {
  ACTIONS,
  REASON_MAX_LENGTH,
  SEARCH_DEBOUNCE_MS,
  SEARCH_MAX_LENGTH,
  actionError,
  actionInfo,
  parseWorkerId,
  resultsSummary,
  searchError,
  searchTextProblem,
  selectionMatches,
  shouldSearch,
  successText,
  validateReason,
  workerDetails,
  workerIdFromSearch,
  workerTitle,
  workStatusText,
  type ActionError,
} from './view'

type Step = 'form' | 'confirm'

/** ADMIN-UAT-11: the Super-Freelancer flag (contract admin.md 2.5) and the unlock of a locked freelancer (2.6, decisions Q12 and Q15). */
export function WorkerControlsPage() {
  const [params] = useSearchParams()
  const [action, setAction] = useState<WorkerAction>('UNLOCK')
  const [workerText, setWorkerText] = useState(() => workerIdFromSearch(params.toString()))
  const [reason, setReason] = useState('')
  const [step, setStep] = useState<Step>('form')
  const [idError, setIdError] = useState<string | null>(null)
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [failure, setFailure] = useState<ActionError | null>(null)
  const [done, setDone] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const inFlight = useRef(false)

  // ADMIN-UAT-14: the picker. `searchSeq` lets an answer that arrives late be ignored when a newer search has started.
  const [searchText, setSearchText] = useState('')
  const [results, setResults] = useState<{ items: AdminWorker[]; total: number } | null>(null)
  const [searching, setSearching] = useState(false)
  const [searchFailure, setSearchFailure] = useState<string | null>(null)
  const [selected, setSelected] = useState<AdminWorker | null>(null)
  const searchTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const searchSeq = useRef(0)

  const info = actionInfo(action)
  const locked = busy || step === 'confirm'

  /** The worker of this number, from the lookup, or null when it is not found or the call fails (the form keeps working without it). */
  async function findWorker(id: number): Promise<AdminWorker | null> {
    try {
      const page = await workerControlsApi.search(String(id))
      return page.items.find((w) => w.workerId === id) ?? null
    } catch {
      return null
    }
  }

  // `?workerId=12` preselects that worker: its card shows once the lookup answers.
  useEffect(() => {
    const preset = parseWorkerId(workerIdFromSearch(params.toString()))
    if (!('id' in preset)) return
    void findWorker(preset.id).then((worker) => setSelected(worker))
    // eslint-disable-next-line react-hooks/exhaustive-deps -- once, for the address the page was opened with
  }, [])

  function onSearchChange(text: string) {
    setSearchText(text)
    if (searchTimer.current) clearTimeout(searchTimer.current)
    searchSeq.current += 1
    if (!shouldSearch(text)) {
      setResults(null)
      setSearching(false)
      setSearchFailure(null)
      return
    }
    setSearching(true)
    const seq = searchSeq.current
    searchTimer.current = setTimeout(() => void runSearch(text, seq), SEARCH_DEBOUNCE_MS)
  }

  async function runSearch(text: string, seq: number) {
    try {
      const page = await workerControlsApi.search(text)
      if (seq !== searchSeq.current) return
      setResults({ items: page.items, total: page.total })
      setSearchFailure(null)
    } catch (e) {
      if (seq !== searchSeq.current) return
      setResults(null)
      setSearchFailure(
        e instanceof ApiError
          ? searchError({ status: e.status, message: e.message, data: e.data, fieldErrors: e.fieldErrors })
          : 'Đã xảy ra lỗi khi tìm thợ. Có thể thử lại.',
      )
    } finally {
      if (seq === searchSeq.current) setSearching(false)
    }
  }

  function pick(worker: AdminWorker) {
    setSelected(worker)
    setWorkerText(String(worker.workerId))
    setIdError(null)
    setResults(null)
    setSearchText('')
    setSearchFailure(null)
  }

  function onWorkerTextChange(text: string) {
    setWorkerText(text)
    if (!selectionMatches(selected, text)) setSelected(null)
  }

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
      // The card shows the state the action left behind (unlocked, flag on or off), not the one before it.
      const refreshed = await findWorker(parsed.id)
      if (refreshed) setSelected(refreshed)
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

  const searchProblem = searchTextProblem(searchText)

  return (
    <div className="flex max-w-2xl flex-col gap-6">
      <header>
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">QUẢN LÝ NHÂN SỰ · Q12 · Q15</div>
        <h1 className="text-2xl font-semibold">Cờ Super-Freelancer và mở khoá thợ</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Mỗi thao tác cần lý do và được ghi vào nhật ký kiểm toán. Tìm thợ theo tên, số điện thoại, CCCD hoặc mã số rồi chọn, hoặc nhập thẳng mã số thợ.
        </p>
      </header>

      <fieldset className="flex flex-col gap-2" disabled={locked}>
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

      <div className="flex flex-col gap-2">
        <label htmlFor="worker-search" className="text-sm font-medium">
          Tìm thợ
        </label>
        <input
          id="worker-search"
          type="search"
          autoComplete="off"
          value={searchText}
          onChange={(e) => onSearchChange(e.target.value)}
          disabled={locked}
          aria-invalid={searchProblem !== null}
          placeholder="Tên, số điện thoại, CCCD hoặc mã số"
          className={cn('rounded-lg border bg-background p-2 text-sm', searchProblem ? 'border-destructive' : 'border-border')}
        />
        <div className={cn('text-xs', searchProblem ? 'text-destructive' : 'text-muted-foreground')}>
          {searchProblem ?? `Tối đa ${SEARCH_MAX_LENGTH} ký tự.`}
        </div>
        {searching && (
          <div role="status" className="text-sm text-muted-foreground">
            Đang tìm…
          </div>
        )}
        {searchFailure && (
          <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive">
            {searchFailure}
          </div>
        )}
        {results && !searching && (
          <div className="flex flex-col gap-1">
            {results.items.length > 0 && (
              <ul className="flex flex-col divide-y divide-border rounded-lg border border-border">
                {results.items.map((w) => (
                  <li key={w.workerId}>
                    <button
                      type="button"
                      onClick={() => pick(w)}
                      disabled={locked}
                      className="flex w-full flex-col gap-0.5 p-2 text-left text-sm hover:bg-muted disabled:opacity-60"
                    >
                      <span className="font-medium">{workerTitle(w)}</span>
                      <span className="text-xs text-muted-foreground">
                        {w.phoneNumber} · CCCD {w.nationalId} · {workStatusText(w.workStatus)}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
            <div className="text-xs text-muted-foreground">{resultsSummary(results.items.length, results.total)}</div>
          </div>
        )}
      </div>

      {selected && (
        <section aria-label="Thợ đã chọn" className="rounded-lg border border-primary/40 bg-muted/40 p-3 text-sm">
          <div className="mb-2 font-medium">{workerTitle(selected)}</div>
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
            {workerDetails(selected).map((line) => (
              <div key={line.label} className="contents">
                <dt className="text-muted-foreground">{line.label}</dt>
                <dd>{line.value}</dd>
              </div>
            ))}
          </dl>
        </section>
      )}

      <div className="flex flex-col gap-1">
        <label htmlFor="worker-id" className="text-sm font-medium">
          Mã số thợ
        </label>
        <input
          id="worker-id"
          inputMode="numeric"
          value={workerText}
          onChange={(e) => onWorkerTextChange(e.target.value)}
          disabled={locked}
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
          disabled={locked}
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
            {info.label} cho {selected ? workerTitle(selected) : `thợ #${workerText.trim()}`}?
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
