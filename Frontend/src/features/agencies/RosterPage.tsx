import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { cn } from '@/lib/utils'
import { agenciesApi } from './api'
import { useAction } from './actions'
import { Alert, PageHeader, Panel } from './shared'
import type { RosterEntryError } from './types'
import { addDays, dayLabel, mondayOf, rosterGrid, SHIFTS, shiftLabel, slotStatusText, weekDays } from './view'

function todayIso(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** WEB-M5-03: the weekly shift roster (contract agencies.md 5, decisions D11). All-or-nothing writes with errors by entry. */
export function RosterPage() {
  const [monday, setMonday] = useState(() => mondayOf(todayIso()))
  const roster = useResource(() => agenciesApi.roster(monday), [monday])
  const [workerId, setWorkerId] = useState('')
  const [date, setDate] = useState(() => todayIso())
  const [shift, setShift] = useState<string>(SHIFTS[0].code)
  const [errors, setErrors] = useState<RosterEntryError[]>([])
  const [notice, setNotice] = useState<string | null>(null)
  const add = useAction()
  const remove = useAction()

  const days = weekDays(monday)
  const grid = rosterGrid(roster.data ?? [])

  async function addEntry() {
    const id = Number(workerId)
    if (!Number.isInteger(id) || id <= 0) return setNotice('Nhập mã thợ (số nguyên dương).')
    setNotice(null)
    setErrors([])
    const outcome = await add.run(() => agenciesApi.putRoster([{ workerId: id, slotDate: date, shiftCode: shift }]))
    if (!outcome) return
    if (outcome.ok) {
      setNotice(outcome.result.created > 0 ? 'Đã xếp ca.' : 'Ca này đã có trong lịch.')
      roster.reload()
    } else {
      setErrors(outcome.errors)
    }
  }

  async function removeSlot(slotId: number) {
    const done = await remove.run(() => agenciesApi.removeSlot(slotId))
    if (done !== undefined) roster.reload()
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader eyebrow="Đối tác · Lịch ca" title="Phân ca theo tuần">
        Xếp thợ vào ca để nhận đơn Premium. Ca đang được giữ cho đơn hoặc đã có đơn không sửa được. Một thợ chỉ có một ca ở mỗi khung giờ.
      </PageHeader>

      <Panel title="Thêm ca">
        <form
          className="flex flex-wrap items-end gap-3 text-sm"
          onSubmit={(e) => {
            e.preventDefault()
            void addEntry()
          }}
        >
          <label className="flex flex-col gap-1">
            Mã thợ
            <input value={workerId} onChange={(e) => setWorkerId(e.target.value)} inputMode="numeric" className="w-28 rounded-lg border border-border bg-background p-2" />
          </label>
          <label className="flex flex-col gap-1">
            Ngày
            <input type="date" value={date} onChange={(e) => setDate(e.target.value)} className="rounded-lg border border-border bg-background p-2" />
          </label>
          <label className="flex flex-col gap-1">
            Ca
            <select value={shift} onChange={(e) => setShift(e.target.value)} className="rounded-lg border border-border bg-background p-2">
              {SHIFTS.map((s) => (
                <option key={s.code} value={s.code}>
                  {s.label} ({s.time})
                </option>
              ))}
            </select>
          </label>
          <Button type="submit" size="sm" disabled={add.busy}>
            {add.busy ? 'Đang lưu…' : 'Xếp ca'}
          </Button>
        </form>
        {notice && <div className="mt-3"><Alert tone="info">{notice}</Alert></div>}
        {add.error && <div className="mt-3"><Alert>{add.error}</Alert></div>}
        {errors.length > 0 && (
          <ul className="mt-3 list-disc pl-5 text-sm text-destructive">
            {errors.map((e) => (
              <li key={`${e.index}-${e.code}`}>{e.message}</li>
            ))}
          </ul>
        )}
      </Panel>

      <Panel title="Lịch tuần">
        <div className="mb-3 flex items-center gap-2 text-sm">
          <Button variant="outline" size="sm" onClick={() => setMonday(addDays(monday, -7))}>Tuần trước</Button>
          <span className="font-medium tabular-nums">{days[0].split('-').reverse().join('/')} – {days[6].split('-').reverse().join('/')}</span>
          <Button variant="outline" size="sm" onClick={() => setMonday(addDays(monday, 7))}>Tuần sau</Button>
        </div>
        {roster.error && <Alert>{roster.error}</Alert>}
        {remove.error && <Alert>{remove.error}</Alert>}
        <div className="overflow-x-auto">
          <table className="w-full min-w-[900px] border-collapse text-sm">
            <thead>
              <tr>
                <th className="border border-border p-2 text-left">Ca</th>
                {days.map((d) => (
                  <th key={d} className="border border-border p-2 text-left">{dayLabel(d, monday)}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {SHIFTS.map((s) => (
                <tr key={s.code}>
                  <th scope="row" className="border border-border p-2 text-left font-medium">
                    {shiftLabel(s.code)}
                    <div className="text-xs font-normal text-muted-foreground">{s.time}</div>
                  </th>
                  {days.map((d) => {
                    const cells = grid.get(`${d}|${s.code}`) ?? []
                    return (
                      <td key={d} className="align-top border border-border p-2">
                        <div className="flex flex-col gap-1">
                          {cells.map((c) => (
                            <div
                              key={c.slotId}
                              className={cn('flex items-center justify-between gap-2 rounded-md px-2 py-1', c.removable ? 'bg-muted' : 'bg-amber-100 text-amber-900')}
                              title={slotStatusText(c.status)}
                            >
                              <span>{c.workerName}</span>
                              {c.removable ? (
                                <button type="button" className="text-xs text-destructive underline" onClick={() => void removeSlot(c.slotId)}>
                                  Bỏ
                                </button>
                              ) : (
                                <span className="text-xs">{slotStatusText(c.status)}</span>
                              )}
                            </div>
                          ))}
                        </div>
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Panel>
    </div>
  )
}
