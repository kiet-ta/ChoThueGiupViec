import { useRef, useState } from 'react'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { agenciesApi } from './api'
import { useAction } from './actions'
import { Alert, PageHeader, Panel } from './shared'
import type { ImportResult } from './types'
import { buildGuaranteeHtml, guaranteeProblems as guaranteeFormProblems, type AgencyGuaranteeErrors, type AgencyGuaranteeForm } from './guarantee'
import { groupImportErrors, isPdf, isXlsx } from './view'

/** WEB-M5-02: guarantee, template, dry-run with the errors by row, all-or-nothing commit (decisions Q18, Q19, D10). */
export function ImportPage() {
  const [guaranteeDone, setGuaranteeDone] = useState<string | null>(null)
  const [guaranteeForm, setGuaranteeForm] = useState<AgencyGuaranteeForm>({ legalName: '', taxCode: '', legalRepresentative: '', signedDate: '' })
  const [guaranteeErrors, setGuaranteeErrors] = useState<AgencyGuaranteeErrors>({})
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

  function updateGuaranteeField(field: keyof AgencyGuaranteeForm, value: string) {
    setGuaranteeForm((current) => ({ ...current, [field]: value }))
    setGuaranteeErrors((current) => {
      const next = { ...current }
      delete next[field]
      return next
    })
  }

  function prepareGuarantee() {
    setProblem(null)
    const errors = guaranteeFormProblems(guaranteeForm)
    setGuaranteeErrors(errors)
    if (Object.keys(errors).length > 0) return

    const printWindow = window.open('', '_blank')
    if (!printWindow) {
      setProblem('Trình duyệt đang chặn cửa sổ mẫu. Hãy cho phép cửa sổ bật lên rồi thử lại.')
      return
    }

    printWindow.opener = null
    printWindow.document.open()
    printWindow.document.write(buildGuaranteeHtml(guaranteeForm))
    printWindow.document.close()
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
        Bước 1: điền và in mẫu cam kết, ký rồi tải PDF lên. Cam kết áp dụng cho Agency và các đợt nhập thợ sau. Bước 2: điền file Excel, kiểm tra thử rồi tạo thợ. Một dòng lỗi hoặc vượt hạn mức gói thì không tạo thợ nào.
      </PageHeader>

      {problem && <Alert>{problem}</Alert>}

      <Panel title="1. Mẫu và cam kết bảo lãnh">
        <div className="max-w-3xl">
          <p className="mb-4 text-sm text-muted-foreground">Nhập tên pháp lý, mã số thuế và người đại diện đúng theo hồ sơ Agency đã đăng ký. Mẫu không liệt kê từng thợ; hệ thống không tự đối chiếu thông tin hoặc xác minh chữ ký.</p>
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="flex flex-col gap-1 text-sm sm:col-span-2">
              Tên pháp lý Agency
              <input id="guarantee-legal-name" value={guaranteeForm.legalName} onChange={(e) => updateGuaranteeField('legalName', e.target.value)} maxLength={200} className="rounded-lg border border-border bg-background p-2" aria-required="true" aria-invalid={!!guaranteeErrors.legalName} aria-describedby={guaranteeErrors.legalName ? 'guarantee-legal-name-error' : undefined} />
              {guaranteeErrors.legalName && <span id="guarantee-legal-name-error" className="text-destructive">{guaranteeErrors.legalName}</span>}
            </label>
            <label className="flex flex-col gap-1 text-sm">
              Mã số thuế
              <input id="guarantee-tax-code" value={guaranteeForm.taxCode} onChange={(e) => updateGuaranteeField('taxCode', e.target.value)} maxLength={14} className="rounded-lg border border-border bg-background p-2 tabular-nums" aria-required="true" aria-invalid={!!guaranteeErrors.taxCode} aria-describedby={guaranteeErrors.taxCode ? 'guarantee-tax-code-error' : undefined} />
              {guaranteeErrors.taxCode && <span id="guarantee-tax-code-error" className="text-destructive">{guaranteeErrors.taxCode}</span>}
            </label>
            <label className="flex flex-col gap-1 text-sm">
              Người đại diện pháp luật
              <input id="guarantee-representative" value={guaranteeForm.legalRepresentative} onChange={(e) => updateGuaranteeField('legalRepresentative', e.target.value)} maxLength={100} className="rounded-lg border border-border bg-background p-2" aria-required="true" aria-invalid={!!guaranteeErrors.legalRepresentative} aria-describedby={guaranteeErrors.legalRepresentative ? 'guarantee-representative-error' : undefined} />
              {guaranteeErrors.legalRepresentative && <span id="guarantee-representative-error" className="text-destructive">{guaranteeErrors.legalRepresentative}</span>}
            </label>
            <label className="flex flex-col gap-1 text-sm">
              Ngày ký dự kiến
              <input type="date" value={guaranteeForm.signedDate} onChange={(e) => updateGuaranteeField('signedDate', e.target.value)} className="rounded-lg border border-border bg-background p-2 tabular-nums" />
            </label>
          </div>
          <div className="mt-4 flex flex-wrap items-center gap-3">
            <Button size="sm" variant="outline" onClick={prepareGuarantee}>Mở mẫu để in / lưu PDF</Button>
            <span className="text-xs text-muted-foreground">Trong cửa sổ mẫu, chọn “In / Lưu PDF”; sau đó ký bản PDF trước khi nộp.</span>
          </div>
          <div className="mt-5 flex flex-wrap items-center gap-3 text-sm">
            <label className="font-medium" htmlFor="guarantee-file">Tải PDF đã ký</label>
            <input id="guarantee-file" type="file" accept="application/pdf,.pdf" disabled={guarantee.busy} onChange={(e) => void uploadGuarantee(e.target.files?.[0])} aria-label="Tệp cam kết PDF đã ký" />
            {guarantee.busy && <span>Đang tải lên…</span>}
            {guaranteeDone && <StatusBadge tone="success">Đã nhận file lúc {new Date(guaranteeDone).toLocaleString('vi-VN')}; chữ ký chưa được xác minh.</StatusBadge>}
          </div>
          <p className="mt-2 text-xs text-muted-foreground">Chỉ cần nộp một lần cho Agency; các lần nhập sau không cần tải lại nếu cam kết chưa bị thay thế.</p>
          {guarantee.error && <div className="mt-2"><Alert>{guarantee.error}</Alert></div>}
        </div>
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
