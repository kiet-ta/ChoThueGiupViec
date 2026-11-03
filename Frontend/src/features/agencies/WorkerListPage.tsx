import { useState } from 'react'
import { DataTable, type Column } from '@/components/data-table'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { agenciesApi, PAGE_SIZE } from './api'
import { useAction } from './actions'
import { Alert, PageHeader, Panel } from './shared'
import type { AgencyWorkerItem } from './types'
import {
  buildRetireDialogState,
  canRetire,
  firstRetireRequest,
  futureJobText,
  newWorkerProblems,
  replacementCandidates,
  retireFailureText,
  retireRequestFor,
  workStatusLabel,
  type NewWorkerForm,
  type RetireChoice,
  type RetireDialogState,
} from './view'

type Row = AgencyWorkerItem & { statusText: string; canRetire: boolean }

/** `pageSize` of the replacement picker (contract 9.1 allows 1-100). */
const PICKER_SIZE = 100

const EMPTY_FORM: NewWorkerForm = { fullName: '', phoneNumber: '', nationalId: '', bankName: '', bankAccountNo: '' }

/** What the dialog is on: `ask` (before any call) or the state built from the server's answer. */
type Dialog = 'ask' | RetireDialogState

/** WEB-M5-07 / AGENCY-FIX-02: Partner Portal worker list, retire and replace (contract agencies.md 9.1-9.2). */
export function WorkerListPage() {
  const [page, setPage] = useState(1)
  const [statusFilter, setStatusFilter] = useState('')
  const workers = useResource(
    () => agenciesApi.workers(page, PAGE_SIZE, statusFilter || undefined),
    [page, statusFilter]
  )

  const [target, setTarget] = useState<AgencyWorkerItem | null>(null)
  const [dialog, setDialog] = useState<Dialog | null>(null)
  const [choiceType, setChoiceType] = useState<RetireChoice['type']>('existing')
  const [existingId, setExistingId] = useState<number | null>(null)
  const [form, setForm] = useState<NewWorkerForm>(EMPTY_FORM)
  const [formProblems, setFormProblems] = useState<string[]>([])
  const [candidates, setCandidates] = useState<AgencyWorkerItem[]>([])

  const retire = useAction(retireFailureText)
  const picker = useAction()

  const rows: Row[] = workers.data
    ? workers.data.items.map((w) => ({ ...w, statusText: workStatusLabel(w.workStatus), canRetire: canRetire(w.workStatus) }))
    : []

  const columns: Column<Row>[] = [
    { id: 'workerId', header: 'ID', cell: (r) => <span className="tabular-nums">#{r.workerId}</span> },
    { id: 'fullName', header: 'Họ tên', cell: (r) => r.fullName },
    { id: 'workStatus', header: 'Trạng thái', cell: (r) => <span className="capitalize">{r.statusText}</span> },
    {
      id: 'futureAssignedJobs',
      header: 'Ca sắp tới',
      align: 'right',
      cell: (r) => <span className="tabular-nums">{r.futureAssignedJobs}</span>,
    },
    {
      id: 'action',
      header: '',
      cell: (r) =>
        r.canRetire ? (
          <Button variant="destructive" size="sm" onClick={() => openFor(r)} disabled={retire.busy}>
            Ngừng
          </Button>
        ) : null,
    },
  ]

  function openFor(worker: AgencyWorkerItem) {
    setTarget(worker)
    setDialog('ask')
    setChoiceType('existing')
    setExistingId(null)
    setForm(EMPTY_FORM)
    setFormProblems([])
    retire.setError(null)
  }

  function close() {
    setTarget(null)
    setDialog(null)
  }

  /** Shows the server's answer; the preview also loads the workers that can take over. */
  async function show(response: Awaited<ReturnType<typeof agenciesApi.retireWorker>> | undefined) {
    if (!response) return
    const state = buildRetireDialogState(response)
    setDialog(state)
    if (state.kind === 'choose') {
      const list = await picker.run(() => agenciesApi.workers(1, PICKER_SIZE))
      setCandidates(list ? replacementCandidates(list.items, response.workerId) : [])
    } else {
      workers.reload()
    }
  }

  /** The first call carries no replacement and no `confirmRescue`. */
  async function sendFirst() {
    if (!target) return
    await show(await retire.run(() => agenciesApi.retireWorker(target.workerId, firstRetireRequest())))
  }

  async function sendChoice() {
    if (!target) return
    const choice: RetireChoice =
      choiceType === 'existing' ? { type: 'existing', workerId: existingId } : choiceType === 'new' ? { type: 'new', form } : { type: 'rescue' }
    const body = retireRequestFor(choice)
    if (!body) {
      setFormProblems(choiceType === 'new' ? newWorkerProblems(form) : ['Hãy chọn thợ thay.'])
      return
    }
    setFormProblems([])
    await show(await retire.run(() => agenciesApi.retireWorker(target.workerId, body)))
  }

  const field = (label: string, key: keyof NewWorkerForm) => (
    <label className="flex flex-col gap-1 text-sm">
      {label}
      <input
        value={form[key]}
        onChange={(e) => setForm({ ...form, [key]: e.target.value })}
        className="rounded-lg border border-border bg-background p-2"
      />
    </label>
  )

  const radio = (type: RetireChoice['type'], text: string) => (
    <label className="flex items-center gap-2 text-sm">
      <input type="radio" name="retire-choice" checked={choiceType === type} onChange={() => setChoiceType(type)} />
      {text}
    </label>
  )

  return (
    <div className="flex flex-col gap-6">
      <PageHeader eyebrow="Đối tác · Nhân sự" title="Danh sách thợ">
        Quản lý thợ của doanh nghiệp. Thợ đã ngừng không khôi phục được; số điện thoại và CCCD vẫn được giữ lại.
      </PageHeader>

      {workers.error && <Alert>{workers.error}</Alert>}

      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(r) => r.workerId}
        loading={workers.loading && !workers.data}
        error={workers.error}
        onRetry={workers.reload}
        emptyMessage="Chưa có thợ nào."
        toolbar={
          <label className="flex items-center gap-2 text-sm">
            Trạng thái
            <select
              value={statusFilter}
              onChange={(e) => {
                setStatusFilter(e.target.value)
                setPage(1)
              }}
              className="rounded-lg border border-border bg-background p-2"
            >
              <option value="">Tất cả</option>
              <option value="IDLE">Đang làm việc</option>
              <option value="BUSY">Đang làm việc</option>
              <option value="LOCKED">Bị khóa</option>
              <option value="RETIRING">Đang chờ nghỉ</option>
              <option value="RETIRED">Đã nghỉ</option>
            </select>
          </label>
        }
        pagination={
          workers.data
            ? { page: workers.data.page, pageSize: PAGE_SIZE, total: workers.data.total, onPageChange: setPage }
            : undefined
        }
      />

      {target && dialog === 'ask' && (
        <Panel title={`Ngừng thợ #${target.workerId} — ${target.fullName}`}>
          <p className="mb-4 text-sm">
            Thợ đã ngừng không khôi phục được; số điện thoại và CCCD vẫn được giữ lại. Nếu thợ còn ca sắp tới, bước sau sẽ cho bạn chọn thợ thay.
          </p>
          <div className="flex gap-2">
            <Button size="sm" variant="destructive" onClick={sendFirst} disabled={retire.busy}>
              {retire.busy ? 'Đang xử lý…' : 'Tiếp tục'}
            </Button>
            <Button size="sm" variant="ghost" onClick={close} disabled={retire.busy}>
              Hủy
            </Button>
          </div>
          {retire.error && (
            <div className="mt-3">
              <Alert>{retire.error}</Alert>
            </div>
          )}
        </Panel>
      )}

      {target && dialog && dialog !== 'ask' && dialog.kind === 'choose' && (
        <Panel title="Thợ còn ca sắp tới">
          <p className="mb-3 text-sm">{dialog.message}</p>
          <ul className="mb-4 list-disc space-y-1 pl-5 text-sm">
            {dialog.jobs.map((job) => (
              <li key={job.assignmentId}>{futureJobText(job)}</li>
            ))}
          </ul>

          <div className="mb-4 space-y-3">
            {radio('existing', 'Chọn thợ thay có sẵn')}
            {choiceType === 'existing' && (
              <label className="ml-6 flex flex-col gap-1 text-sm">
                Thợ thay
                <select
                  value={existingId ?? ''}
                  onChange={(e) => setExistingId(e.target.value ? Number(e.target.value) : null)}
                  className="rounded-lg border border-border bg-background p-2"
                >
                  <option value="">-- Chọn thợ --</option>
                  {candidates.map((w) => (
                    <option key={w.workerId} value={w.workerId}>
                      #{w.workerId} — {w.fullName} ({workStatusLabel(w.workStatus)})
                    </option>
                  ))}
                </select>
              </label>
            )}

            {radio('new', 'Tạo thợ thay mới')}
            {choiceType === 'new' && (
              <div className="ml-6 space-y-2">
                {field('Họ tên', 'fullName')}
                {field('Số điện thoại (10 số, bắt đầu bằng 0)', 'phoneNumber')}
                {field('CCCD (12 số)', 'nationalId')}
                {field('Tên ngân hàng (tùy chọn)', 'bankName')}
                {field('Số tài khoản (tùy chọn, 6-30 số)', 'bankAccountNo')}
              </div>
            )}

            {radio('rescue', 'Không có thợ thay: chuyển cứu hộ khẩn cấp, doanh nghiệp bị phạt SLA')}
          </div>

          {formProblems.length > 0 && (
            <div className="mb-3 space-y-1 text-sm text-destructive">
              {formProblems.map((p) => (
                <div key={p}>{p}</div>
              ))}
            </div>
          )}

          <div className="flex gap-2">
            <Button size="sm" variant={choiceType === 'rescue' ? 'destructive' : 'default'} onClick={sendChoice} disabled={retire.busy}>
              {retire.busy ? 'Đang xử lý…' : choiceType === 'rescue' ? 'Xác nhận cứu hộ khẩn cấp' : 'Ngừng thợ và giao ca'}
            </Button>
            <Button size="sm" variant="ghost" onClick={close} disabled={retire.busy}>
              Hủy
            </Button>
          </div>
          {retire.error && (
            <div className="mt-3">
              <Alert>{retire.error}</Alert>
            </div>
          )}
          {picker.error && (
            <div className="mt-3">
              <Alert>{picker.error}</Alert>
            </div>
          )}
        </Panel>
      )}

      {target && dialog && dialog !== 'ask' && dialog.kind === 'done' && (
        <Panel title={`Thợ #${target.workerId}: ${dialog.statusText}`}>
          <p className="mb-3 text-sm">{dialog.message}</p>
          {dialog.replacementWorkerId && <p className="mb-3 text-sm">Thợ thay: #{dialog.replacementWorkerId}</p>}
          {dialog.jobs.length > 0 && (
            <ul className="mb-3 list-disc space-y-1 pl-5 text-sm">
              {dialog.jobs.map((job) => (
                <li key={job.assignmentId}>{futureJobText(job)}</li>
              ))}
            </ul>
          )}
          <Button size="sm" variant="ghost" onClick={close}>
            Đóng
          </Button>
        </Panel>
      )}
    </div>
  )
}
