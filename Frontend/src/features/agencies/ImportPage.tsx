import { useRef, useState } from 'react'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { agenciesApi } from './api'
import { useAction } from './actions'
import { Alert, PageHeader, Panel } from './shared'
import type { ImportResult } from './types'
import { groupImportErrors, isPdf, isXlsx } from './view'

/** WEB-M5-02: guarantee, template, dry-run with the errors by row, all-or-nothing commit (decisions Q18, Q19, D10). */
export function ImportPage() {
  const [guaranteeDone, setGuaranteeDone] = useState<string | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [file, setFile] = useState<File | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)
  const guarantee = useAction()
  const check = useAction()
  const commit = useAction()
  const template = useAction()
  const [problem, setProblem] = useState<string | null>(null)

  async function downloadTemplate() {
    const blob = await template.run(() => agenciesApi.importTemplate())
    if (!blob) return
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = 'worker-import-template.xlsx'
    a.click()
    URL.revokeObjectURL(url)
  }

  async function uploadGuarantee(f: File | undefined) {
    if (!f) return
    if (!isPdf(f)) return setProblem('Cam kết bảo lãnh phải là file PDF.')
    setProblem(null)
    const done = await guarantee.run(() => agenciesApi.signGuarantee(f))
    if (done) setGuaranteeDone(done.guaranteeSignedAt)
  }

  function choose(f: File | undefined) {
    setResult(null)
    if (!f) return setFile(null)
    if (!isXlsx(f)) {
      setFile(null)
      return setProblem('Chỉ nhận file .xlsx (tải mẫu ở trên).')
    }
    setProblem(null)
    setFile(f)
  }

  async function dryRun() {
    if (!file) return
    const r = await check.run(() => agenciesApi.importDryRun(file))
    if (r) setResult(r)
  }

  async function doCommit() {
    if (!file) return
    const r = await commit.run(() => agenciesApi.importCommit(file))
    if (r) setResult(r)
  }

  const groups = result ? groupImportErrors(result.errors) : []

  return (
    <div className="flex flex-col gap-6">
      <PageHeader eyebrow="Đối tác · Nhân sự" title="Thêm thợ hàng loạt">
        Bước 1: tải cam kết bảo lãnh đã ký (PDF). Bước 2: điền file mẫu, kiểm tra thử để xem lỗi từng dòng, rồi mới tạo thợ. Một dòng lỗi hoặc vượt
        hạn mức gói thì không tạo thợ nào.
      </PageHeader>

      {problem && <Alert>{problem}</Alert>}

      <Panel title="1. Cam kết bảo lãnh">
        <div className="flex flex-wrap items-center gap-3 text-sm">
          <input type="file" accept="application/pdf,.pdf" onChange={(e) => void uploadGuarantee(e.target.files?.[0])} aria-label="Tệp cam kết PDF" />
          {guarantee.busy && <span>Đang tải lên…</span>}
          {guaranteeDone && <StatusBadge tone="success">Đã ghi nhận lúc {new Date(guaranteeDone).toLocaleString('vi-VN')}</StatusBadge>}
        </div>
        {guarantee.error && <div className="mt-2"><Alert>{guarantee.error}</Alert></div>}
      </Panel>

      <Panel title="2. File thợ (.xlsx)">
        <div className="flex flex-wrap items-center gap-3">
          <Button variant="outline" size="sm" onClick={() => void downloadTemplate()} disabled={template.busy}>
            Tải file mẫu
          </Button>
          <input ref={fileInput} type="file" accept=".xlsx" onChange={(e) => choose(e.target.files?.[0])} aria-label="Tệp thợ .xlsx" />
        </div>
        {template.error && <div className="mt-2"><Alert>{template.error}</Alert></div>}
        <p className="mt-2 text-xs text-muted-foreground">Cột: full_name, phone_number, national_id (12 số), bank_name, bank_account_no.</p>

        <div className="mt-4 flex gap-3">
          <Button size="sm" disabled={!file || check.busy} onClick={() => void dryRun()}>
            {check.busy ? 'Đang kiểm tra…' : 'Kiểm tra thử'}
          </Button>
          <Button size="sm" variant="default" disabled={!file || !result?.isValid || commit.busy} onClick={() => void doCommit()}>
            {commit.busy ? 'Đang tạo…' : 'Tạo thợ'}
          </Button>
        </div>
        {(check.error || commit.error) && <div className="mt-3"><Alert>{check.error ?? commit.error}</Alert></div>}
      </Panel>

      {result && (
        <Panel title={result.committed ? 'Đã tạo thợ' : result.isValid ? 'File hợp lệ' : 'File có lỗi'}>
          {result.committed ? (
            <Alert tone="info">Đã tạo {result.createdWorkers} thợ. Họ có thể đăng nhập bằng số điện thoại.</Alert>
          ) : (
            <p className="text-sm">
              {result.validRows}/{result.totalRows} dòng hợp lệ.
              {result.isValid && ' Bấm "Tạo thợ" để hoàn tất.'}
            </p>
          )}
          {groups.length > 0 && (
            <ul className="mt-3 flex flex-col gap-2 text-sm">
              {groups.map((g) => (
                <li key={g.row} className="rounded-lg border border-destructive/30 bg-destructive/5 p-3">
                  <div className="font-medium">{g.title}</div>
                  <ul className="list-disc pl-5">
                    {g.items.map((item, i) => (
                      <li key={i}>
                        {item.column ? <b>{item.column}: </b> : null}
                        {item.text}
                      </li>
                    ))}
                  </ul>
                </li>
              ))}
            </ul>
          )}
        </Panel>
      )}
    </div>
  )
}
