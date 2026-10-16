import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { agenciesApi } from './api'
import { useAction } from './actions'
import { Alert, PageHeader, Panel } from './shared'
import { bankProblems } from './view'

/** The account the platform transfers the agency's payout to (MAIN-FLOW-FIX, decisions D18). */
export function BankPage() {
  const bank = useResource(() => agenciesApi.bank(), [])
  const [name, setName] = useState<string | null>(null)
  const [account, setAccount] = useState<string | null>(null)
  const [problems, setProblems] = useState<string[]>([])
  const [saved, setSaved] = useState(false)
  const save = useAction()

  const current = bank.data
  // Until the agency types, the fields show what is stored.
  const nameValue = name ?? current?.bankName ?? ''
  const accountValue = account ?? current?.bankAccountNo ?? ''

  async function submit() {
    const found = bankProblems(nameValue, accountValue)
    setProblems(found)
    setSaved(false)
    if (found.length > 0) return
    const result = await save.run(() => agenciesApi.saveBank(nameValue.trim(), accountValue.replace(/\s+/g, '')))
    if (result) {
      setName(null)
      setAccount(null)
      setSaved(true)
      bank.reload()
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader eyebrow="Đối tác · Tài khoản" title="Tài khoản nhận tiền">
        Nền tảng chuyển tiền công của các ca đã hoàn thành vào tài khoản này (mỗi kỳ payout). Chưa có tài khoản thì dòng chuyển khoản của công ty bị để trống.
      </PageHeader>

      {bank.error && <Alert>{bank.error}</Alert>}
      {current && !current.isSet && <Alert tone="info">Công ty chưa khai báo tài khoản nhận tiền.</Alert>}

      <Panel title="Thông tin ngân hàng">
        <div className="flex max-w-md flex-col gap-4 text-sm">
          <label className="flex flex-col gap-1">
            Ngân hàng
            <input
              value={nameValue}
              onChange={(e) => setName(e.target.value)}
              maxLength={100}
              className="rounded-lg border border-border bg-background p-2"
              placeholder="Ví dụ: Vietcombank"
            />
          </label>
          <label className="flex flex-col gap-1">
            Số tài khoản
            <input
              value={accountValue}
              onChange={(e) => setAccount(e.target.value)}
              inputMode="numeric"
              maxLength={40}
              className="rounded-lg border border-border bg-background p-2 tabular-nums"
              placeholder="6 đến 30 chữ số"
            />
          </label>
          {problems.length > 0 && (
            <Alert>
              <ul className="list-disc pl-5">
                {problems.map((p) => (
                  <li key={p}>{p}</li>
                ))}
              </ul>
            </Alert>
          )}
          {save.error && <Alert>{save.error}</Alert>}
          {saved && <Alert tone="info">Đã lưu tài khoản nhận tiền.</Alert>}
          <div>
            <Button size="sm" disabled={save.busy} onClick={() => void submit()}>
              {save.busy ? 'Đang lưu…' : 'Lưu'}
            </Button>
          </div>
        </div>
      </Panel>
    </div>
  )
}
