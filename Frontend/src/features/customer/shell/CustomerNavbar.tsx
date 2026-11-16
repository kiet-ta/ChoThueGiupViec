import { useEffect, useRef, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { CalendarPlus, ChevronDown, ClipboardList, Heart, House, LogOut, Menu, UserRound, X, type LucideIcon } from 'lucide-react'
import { NAV_GROUPS, groupOf, type NavGroup } from './nav'

const ICONS: Record<NavGroup['key'], LucideIcon> = { home: House, orders: ClipboardList, favorites: Heart, account: UserRound }

/**
 * WEB-CUST-UI-01: a translucent sticky navbar (stronger glass and a shadow once the page scrolls). Each menu opens a
 * dropdown of direct links (hover or click; Enter/Space open it, Esc closes it). Below `md` the menus fold into a panel.
 */
export function CustomerNavbar({ onLogout }: { onLogout: () => void }) {
  const { pathname } = useLocation()
  const active = groupOf(pathname)
  const [open, setOpen] = useState<NavGroup['key'] | null>(null)
  const [mobile, setMobile] = useState(false)
  const [scrolled, setScrolled] = useState(false)
  const closeTimer = useRef<number | undefined>(undefined)
  const navRef = useRef<HTMLElement>(null)

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 8)
    onScroll()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setOpen(null)
        setMobile(false)
      }
    }
    const onClick = (e: MouseEvent) => {
      if (navRef.current && !navRef.current.contains(e.target as Node)) setOpen(null)
    }
    window.addEventListener('keydown', onKey)
    window.addEventListener('mousedown', onClick)
    return () => {
      window.removeEventListener('keydown', onKey)
      window.removeEventListener('mousedown', onClick)
    }
  }, [])

  const enter = (key: NavGroup['key']) => {
    window.clearTimeout(closeTimer.current)
    setOpen(key)
  }
  const leave = () => {
    closeTimer.current = window.setTimeout(() => setOpen(null), 140)
  }
  const close = () => {
    setOpen(null)
    setMobile(false)
  }

  return <header className={`sticky top-0 z-30 transition-[background-color,box-shadow,border-color] duration-300 ${scrolled
    ? 'border-b border-white/70 bg-white/80 shadow-[0_10px_30px_-18px_rgba(26,47,37,0.45)] backdrop-blur-xl'
    : 'border-b border-transparent bg-white/45 backdrop-blur-md'}`}>
    <div className="mx-auto flex h-16 max-w-[1200px] items-center justify-between gap-4 px-5">
      <Link to="/app" onClick={close} className="group flex items-center gap-2.5 text-lg font-bold tracking-tight">
        <span className="grid h-9 w-9 place-items-center rounded-xl bg-gradient-to-br from-[#416448] to-[#1a2f25] text-white shadow-[0_6px_16px_-6px_rgba(26,47,37,0.6)] transition-transform duration-300 motion-safe:group-hover:-rotate-6"><House size={18} /></span>
        TỔ ẤM
      </Link>

      <nav ref={navRef} aria-label="Điều hướng khách hàng" className="hidden items-center gap-1 md:flex">
        {NAV_GROUPS.map((g) => {
          const Icon = ICONS[g.key]
          const isOpen = open === g.key
          return <div key={g.key} className="relative" onMouseEnter={() => enter(g.key)} onMouseLeave={leave}>
            <button type="button" aria-haspopup="true" aria-expanded={isOpen} onClick={() => setOpen(isOpen ? null : g.key)}
              className={`flex h-10 items-center gap-1.5 rounded-full px-3.5 text-sm transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#416448] ${active === g.key ? 'bg-[#1a2f25] font-semibold text-white' : 'text-stone-600 hover:bg-white/80 hover:text-[#1a2f25]'}`}>
              <Icon size={15} />{g.label}<ChevronDown size={14} className={`transition-transform duration-200 ${isOpen ? 'rotate-180' : ''}`} />
            </button>
            {isOpen && <div role="menu" aria-label={g.label} className="cust-drop-in absolute left-1/2 top-full w-[320px] -translate-x-1/2 pt-2">
              <div className="overflow-hidden rounded-2xl border border-stone-200/80 bg-white/95 p-2 shadow-[0_24px_60px_-24px_rgba(26,47,37,0.45)] backdrop-blur-xl">
                {g.items.map((item) => <Link key={item.label} role="menuitem" to={item.to} onClick={close}
                  className="flex flex-col rounded-xl px-3 py-2.5 transition-colors hover:bg-[#416448]/10 focus-visible:bg-[#416448]/10 focus-visible:outline-none">
                  <span className="text-sm font-semibold text-[#1a2f25]">{item.label}</span>
                  <span className="text-xs text-stone-500">{item.hint}</span>
                </Link>)}
              </div>
            </div>}
          </div>
        })}
      </nav>

      <div className="flex items-center gap-2">
        <Link to="/app/booking" onClick={close} className="hidden items-center gap-2 rounded-full bg-[#1a2f25] px-4 py-2 text-sm font-semibold text-white shadow-[0_8px_20px_-10px_rgba(26,47,37,0.8)] transition-transform hover:bg-[#416448] motion-safe:hover:-translate-y-0.5 sm:inline-flex"><CalendarPlus size={16} />Đặt ca</Link>
        <button type="button" onClick={onLogout} className="hidden h-10 items-center gap-1 rounded-full px-3 text-sm text-stone-600 transition-colors hover:bg-white/80 md:flex" aria-label="Đăng xuất"><LogOut size={16} /><span className="hidden lg:inline">Đăng xuất</span></button>
        <button type="button" className="grid h-10 w-10 place-items-center rounded-full text-[#1a2f25] transition-colors hover:bg-white/80 md:hidden" aria-label={mobile ? 'Đóng menu' : 'Mở menu'} aria-expanded={mobile} onClick={() => setMobile(!mobile)}>
          {mobile ? <X size={20} /> : <Menu size={20} />}
        </button>
      </div>
    </div>

    {mobile && <div className="cust-drop-in border-t border-white/70 bg-white/95 backdrop-blur-xl md:hidden">
      <div className="mx-auto max-h-[calc(100vh-4rem)] max-w-[1200px] overflow-y-auto px-5 py-4">
        <Link to="/app/booking" onClick={close} className="mb-4 flex items-center justify-center gap-2 rounded-xl bg-[#1a2f25] py-3 font-semibold text-white"><CalendarPlus size={16} />Đặt ca</Link>
        {NAV_GROUPS.map((g) => {
          const Icon = ICONS[g.key]
          return <div key={g.key} className="mb-4">
            <p className="mb-1 flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-stone-500"><Icon size={14} />{g.label}</p>
            <div className="grid grid-cols-2 gap-1">
              {g.items.map((item) => <Link key={item.label} to={item.to} onClick={close} className="rounded-lg px-3 py-2 text-sm text-[#1a2f25] hover:bg-[#416448]/10">{item.label}</Link>)}
            </div>
          </div>
        })}
        <button type="button" onClick={() => { close(); onLogout() }} className="flex w-full items-center justify-center gap-2 rounded-xl border border-stone-300 py-2.5 text-sm font-semibold text-stone-700"><LogOut size={16} />Đăng xuất</button>
      </div>
    </div>}
  </header>
}
