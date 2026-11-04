import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { filterAdminUnits } from './view'

interface Props {
  label: string
  value: string
  options: string[]
  onChange: (value: string) => void
  onEdit: () => void
  disabled?: boolean
  invalid?: boolean
}

export function SearchSelect({ label, value, options, onChange, onEdit, disabled = false, invalid = false }: Props) {
  const id = useId()
  const ref = useRef<HTMLDivElement>(null)
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const matches = filterAdminUnits(options, query)

  useEffect(() => {
    const close = (event: PointerEvent) => {
      if (!ref.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', close)
    return () => document.removeEventListener('pointerdown', close)
  }, [])
  useEffect(() => {
    if (open) document.getElementById(`${id}-${active}`)?.scrollIntoView({ block: 'nearest' })
  }, [active, id, open])

  function choose(option: string) {
    onChange(option)
    setOpen(false)
  }
  function keyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      if (!open) { setQuery(''); setActive(0); setOpen(true) }
      else if (matches.length) setActive((current) => (current + (event.key === 'ArrowDown' ? 1 : -1) + matches.length) % matches.length)
    } else if (event.key === 'Enter' && open && matches[active]) {
      event.preventDefault()
      choose(matches[active])
    } else if (event.key === 'Escape' && open) {
      event.preventDefault()
      setOpen(false)
    }
  }

  return <div ref={ref} className="relative">
    <label htmlFor={id} className="block text-sm font-medium">{label}</label>
    <input id={id} className="mt-1 h-11 w-full rounded-lg border border-stone-300 bg-white px-3 disabled:bg-stone-100" role="combobox" aria-expanded={open} aria-controls={`${id}-list`} aria-activedescendant={open && matches[active] ? `${id}-${active}` : undefined} aria-autocomplete="list" aria-invalid={invalid} disabled={disabled} value={open ? query : value} placeholder="Chọn…" onFocus={() => { setQuery(''); setActive(0); setOpen(true) }} onClick={() => { if (!open) { setQuery(''); setActive(0); setOpen(true) } }} onBlur={() => setOpen(false)} onChange={(event) => { setQuery(event.target.value); setActive(0); setOpen(true); onEdit() }} onKeyDown={keyDown} />
    {open && <div id={`${id}-list`} role="listbox" className="absolute z-20 mt-1 max-h-60 w-full overflow-y-auto rounded-lg border border-stone-200 bg-white shadow-lg">
      {matches.length === 0 ? <p className="px-3 py-2 text-sm text-stone-500">Không tìm thấy</p> : matches.map((option, index) => <div id={`${id}-${index}`} key={option} role="option" aria-selected={option === value} onMouseDown={(event) => event.preventDefault()} onClick={() => choose(option)} onMouseMove={() => setActive(index)} className={`cursor-pointer px-3 py-2 text-sm ${index === active ? 'bg-emerald-50 text-emerald-900' : 'text-stone-800'}`}>{option}</div>)}
    </div>}
  </div>
}
