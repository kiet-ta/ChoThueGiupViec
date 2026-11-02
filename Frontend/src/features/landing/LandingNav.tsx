import { ArrowRight, House } from 'lucide-react'
import { NAV_LINKS } from './data'

export function LandingNav() {
  return (
    <header className="fixed left-1/2 top-4 z-50 w-[calc(100%-1.5rem)] max-w-5xl -translate-x-1/2">
      <div className="flex items-center justify-between rounded-xl border border-white/60 bg-(--lp-ivory)/85 py-2 pl-3 pr-2 shadow-[0_10px_40px_-12px_rgba(26,47,37,0.25)] backdrop-blur-xl">
        <a href="#top" className="flex items-center gap-2.5 pl-1" aria-label="TỔ ẤM, về đầu trang">
          <span className="grid h-9 w-9 place-items-center rounded-lg bg-(--lp-forest) text-(--lp-mist)">
            <House className="h-5 w-5" strokeWidth={1.75} />
          </span>
          <span className="font-bold tracking-tight text-(--lp-forest)">TỔ ẤM</span>
        </a>
        <nav className="hidden items-center gap-1 text-sm font-medium text-black md:flex" aria-label="Điều hướng chính">
          {NAV_LINKS.map((l) => (
            <a key={l.href} href={l.href} className="rounded-lg px-4 py-2 transition hover:bg-white">
              {l.label}
            </a>
          ))}
        </nav>
        <a href="/login" className="rounded-lg px-3 py-2 text-sm font-semibold text-(--lp-forest)">Đăng nhập</a>
        <a href="#bang-gia" className="lp-btn lp-btn-primary !px-5 !py-2.5 text-sm">
          Đặt lịch <ArrowRight className="h-4 w-4" />
        </a>
      </div>
    </header>
  )
}
