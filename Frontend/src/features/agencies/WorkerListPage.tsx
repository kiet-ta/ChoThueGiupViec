import { useState } from 'react'
import { DataTable, type Column } from '@/components/data-table'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { agenciesApi, PAGE_SIZE } from './api'
import { useAction } from './actions'
import { Alert, PageHeader, Panel } from './shared'
import type { AgencyWorkerItem, RetireWorkerRequest, FutureJobInfo } from './types'
import { workStatusLabel, canRetire, newWorkerProblems, buildRetireDialogState, type RetireDialogState } from './view'

type Row = AgencyWorkerItem & { statusText: string; canRetire: boolean }

/** WEB-M5-07: Partner Portal worker list, retire and replace (AGENCY-UAT-03). */
export function WorkerListPage() {
  const [page, setPage] = useState(1)
  const [statusFilter, setStatusFilter] = useState('')
  const workers = useResource(
    () => agenciesApi.workers(page, PAGE_SIZE, statusFilter || undefined),
    [page, statusFilter]
  )

  // Retire dialog state
  const [dialogState, setDialogState] = useState<RetireDialogState | null>(null)
  const [selectedWorker, setSelectedWorker] = useState<AgencyWorkerItem | null>(null)
  const [replacementType, setReplacementType] = useState<'existing' | 'new' | 'rescue'>('existing')
  const [existingWorkerId, setExistingWorkerId] = useState<number | null>(null)
  const [newWorkerForm, setNewWorkerForm] = useState({
    fullName: '',
    phoneNumber: '',
    nationalId: '',
    bankName: '',
    bankAccountNo: '',
  })
  const [newWorkerErrors, setNewWorkerErrors] = useState<string[]>([])
  const [confirmRescue, setConfirmRescue] = useState(false)

  const retire = useAction()

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
          <Button
            variant="destructive"
            size="sm"
            onClick={() => handleRetireClick(r)}
            disabled={retire.busy}
          >
            Ngừng
          </Button>
        ) : null,
    },
  ]

  const rows: Row[] = workers.data
    ? workers.data.items.map((w) => ({
        ...w,
        statusText: workStatusLabel(w.workStatus),
        canRetire: canRetire(w.workStatus),
      }))
    : []

  function handleRetireClick(worker: AgencyWorkerItem) {
    setSelectedWorker(worker)
    setDialogState(null)
    setReplacementType('existing')
    setExistingWorkerId(null)
    setNewWorkerForm({ fullName: '', phoneNumber: '', nationalId: '', bankName: '', bankAccountNo: '' })
    setNewWorkerErrors([])
    setConfirmRescue(false)
  }

  async function handleRetireSubmit() {
    if (!selectedWorker) return

    const body: RetireWorkerRequest = {}
    if (selectedWorker.futureAssignedJobs > 0) {
      if (replacementType === 'existing' && existingWorkerId) {
        body.replacement = { existingWorkerId }
      } else if (replacementType === 'new') {
        const problems = newWorkerProblems(newWorkerForm)
        if (problems.length > 0) {
          setNewWorkerErrors(problems)
          return
        }
        body.replacement = {
          newWorker: {
            fullName: newWorkerForm.fullName.trim(),
            phoneNumber: newWorkerForm.phoneNumber.trim(),
            nationalId: newWorkerForm.nationalId.trim(),
            bankName: newWorkerForm.bankName?.trim() || undefined,
            bankAccountNo: newWorkerForm.bankAccountNo?.trim() || undefined,
          },
        }
      } else {
        // No replacement, need confirmRescue
        body.confirmRescue = confirmRescue
      }
    }

    const response = await retire.run(() => agenciesApi.retireWorker(selectedWorker.workerId, body))
    if (response) {
      const state = buildRetireDialogState(response)
      setDialogState(state)
      workers.reload()
    }
  }

  function handleDialogConfirm() {
    if (!dialogState) return
    // If we need rescue confirmation, we set confirmRescue=true and resubmit
    if (dialogState.needsRescueConfirm) {
      setConfirmRescue(true)
      handleRetireSubmit()
    } else {
      // Success, close dialog
      setDialogState(null)
      setSelectedWorker(null)
    }
  }

  function handleDialogCancel() {
    setDialogState(null)
    setSelectedWorker(null)
  }

  // If dialogState has needsRescueConfirm, show the confirmation dialog
  // Otherwise if it has a code/message, show error dialog
  // Otherwise if it has warning, show success dialog
  const showDialog = dialogState !== null

  return (
    <div className="flex flex-col gap-6">
      <PageHeader eyebrow="Đối tác · Nhân sự" title="Danh sách thợ">
        Quản lý thợ của doanh nghiệp. Thợ đã ngừng không khôi phục; SĐT và CCCD vẫn bị giữ.
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

      {showDialog && selectedWorker && (
        <Panel title={dialogState?.needsRescueConfirm ? 'Xác nhận cứu hộ khẩn cấp' : dialogState?.response.code ? 'Không thể ngừng thợ' : 'Ngừng thợ thành công'}>
          <p className="mb-4">{dialogState!.message}</p>

          {dialogState?.response.futureJobs.length > 0 && (
            <div className="mb-4 space-y-2">
              <p className="font-medium text-sm">Ca sắp tới ({dialogState!.response.futureJobs.length}):</p>
              <ul className="list-disc pl-5 text-sm space-y-1">
                {dialogState!.response.futureJobs.map((job: FutureJobInfo) => (
                  <li key={job.assignmentId}>
                    #{job.assignmentId} — {job.slotDate} — {job.shiftCode}
                  </li>
                ))}
              </ul>
            </div>
          )}

          {dialogState?.needsRescueConfirm && !confirmRescue && (
            <>
              <div className="mb-4 space-y-3">
                <p className="font-medium text-sm">Chọn cách xử lý:</p>

                <div className="flex items-center gap-2">
                  <input
                    type="radio"
                    id="replacement-existing"
                    name="replacement-type"
                    value="existing"
                    checked={replacementType === 'existing'}
                    onChange={() => setReplacementType('existing')}
                    className="rounded border-border text-primary"
                  />
                  <label htmlFor="replacement-existing" className="text-sm">
                    Chọn thợ thay có sẵn
                  </label>
                </div>
                {replacementType === 'existing' && (
                  <div className="ml-6 space-y-2">
                    <label className="flex flex-col gap-1 text-sm">
                      Thợ thay (ID)
                      <select
                        value={existingWorkerId ?? ''}
                        onChange={(e) => setExistingWorkerId(e.target.value ? Number(e.target.value) : null)}
                        className="rounded-lg border border-border bg-background p-2"
                      >
                        <option value="">-- Chọn thợ --</option>
                        {rows
                          .filter((r) => r.workerId !== selectedWorker!.workerId && canRetire(r.workStatus))
                          .map((r) => (
                            <option key={r.workerId} value={r.workerId}>
                              #{r.workerId} — {r.fullName} ({r.statusText})
                            </option>
                          ))}
                      </select>
                    </label>
                  </div>
                )}

                <div className="flex items-center gap-2">
                  <input
                    type="radio"
                    id="replacement-new"
                    name="replacement-type"
                    value="new"
                    checked={replacementType === 'new'}
                    onChange={() => setReplacementType('new')}
                    className="rounded border-border text-primary"
                  />
                  <label htmlFor="replacement-new" className="text-sm">
                    Tạo thợ thay mới
                  </label>
                </div>
                {replacementType === 'new' && (
                  <div className="ml-6 space-y-2">
                    <label className="flex flex-col gap-1 text-sm">
                      Họ tên
                      <input
                        value={newWorkerForm.fullName}
                        onChange={(e) => setNewWorkerForm({ ...newWorkerForm, fullName: e.target.value })}
                        className="rounded-lg border border-border bg-background p-2"
                      />
                    </label>
                    <label className="flex flex-col gap-1 text-sm">
                      Số điện thoại (10 số, bắt đầu bằng 0)
                      <input
                        value={newWorkerForm.phoneNumber}
                        onChange={(e) => setNewWorkerForm({ ...newWorkerForm, phoneNumber: e.target.value })}
                        className="rounded-lg border border-border bg-background p-2"
                      />
                    </label>
                    <label className="flex flex-col gap-1 text-sm">
                      CCCD (12 số)
                      <input
                        value={newWorkerForm.nationalId}
                        onChange={(e) => setNewWorkerForm({ ...newWorkerForm, nationalId: e.target.value })}
                        className="rounded-lg border border-border bg-background p-2"
                      />
                    </label>
                    <label className="flex flex-col gap-1 text-sm">
                      Tên ngân hàng (tùy chọn)
                      <input
                        value={newWorkerForm.bankName}
                        onChange={(e) => setNewWorkerForm({ ...newWorkerForm, bankName: e.target.value })}
                        className="rounded-lg border border-border bg-background p-2"
                      />
                    </label>
                    <label className="flex flex-col gap-1 text-sm">
                      Số tài khoản (tùy chọn, 6-30 số)
                      <input
                        value={newWorkerForm.bankAccountNo}
                        onChange={(e) => setNewWorkerForm({ ...newWorkerForm, bankAccountNo: e.target.value })}
                        className="rounded-lg border border-border bg-background p-2"
                      />
                    </label>
                    {newWorkerErrors.length > 0 && (
                      <div className="text-sm text-destructive space-y-1">
                        {newWorkerErrors.map((err, i) => (
                          <div key={i}>{err}</div>
                        ))}
                      </div>
                    )}
                  </div>
                )}

                <div className="flex items-center gap-2">
                  <input
                    type="radio"
                    id="replacement-rescue"
                    name="replacement-type"
                    value="rescue"
                    checked={replacementType === 'rescue'}
                    onChange={() => setReplacementType('rescue')}
                    className="rounded border-border text-primary"
                  />
                  <label htmlFor="replacement-rescue" className="text-sm">
                    Không có thợ thay — xác nhận chuyển cứu hộ khẩn cấp (có phạt SLA)
                  </label>
                </div>
              </div>
            </>
          )}

          {dialogState?.response.code && !dialogState?.needsRescueConfirm && (
            <p className="text-sm text-destructive">{dialogState!.message}</p>
          )}

          {dialogState?.response.warning && !dialogState?.response.code && !dialogState?.needsRescueConfirm && (
            <Alert tone="info">{dialogState!.response.warning}</Alert>
          )}

          <div className="mt-4 flex gap-2">
            {dialogState?.needsRescueConfirm && !confirmRescue ? (
              <>
                <Button size="sm" onClick={() => handleDialogConfirm()} disabled={retire.busy}>
                  {retire.busy ? 'Đang xử lý…' : 'Tiếp tục'}
                </Button>
                <Button variant="ghost" size="sm" onClick={handleDialogCancel}>
                  Hủy
                </Button>
              </>
            ) : (
              <Button variant="ghost" size="sm" onClick={handleDialogCancel}>
                Đóng
              </Button>
            )}
          </div>

          {retire.error && <div className="mt-2"><Alert>{retire.error}</Alert></div>}
        </Panel>
      )}
    </div>
  )
}