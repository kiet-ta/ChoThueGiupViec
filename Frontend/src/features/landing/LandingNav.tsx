import { useEffect, useRef } from 'react'
import { ArrowRight, House } from 'lucide-react'
import { NAV_LINKS } from './data'
import { gsap, ScrollTrigger, prefersReducedMotion } from './motion'

export function LandingNav() {
  const progressRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (prefersReducedMotion() || !progressRef.current) return
    const el = progressRef.current
    const trigger = ScrollTrigger.create({
      start: 'top top',
      end: 'bottom bottom',
      onUpdate: (self) => {
        gsap.to(el, { scaleX: self.progress, duration: 0.1, ease: 'none', overwrite: 'auto' })
      },
    })
    return () => trigger.kill()
  }, [])

  return (
    <>
      <div
        ref={progressRef}
        aria-hidden="true"
        className="fixed inset-x-0 top-0 z-60 h-[2.5px] origin-left scale-x-0 bg-linear-to-r from-(--lp-mist) via-(--lp-amber) to-(--lp-mist) shadow-[0_1px_6px_rgba(197,236,201,0.6)]"
      />
      <header className="fixed left-1/2 top-4 z-50 w-[calc(100%-1.5rem)] max-w-5xl -translate-x-1/2">
        <div className="flex items-center justify-between rounded-xl border border-white/60 bg-(--lp-ivory)/85 py-2 pl-3 pr-2 shadow-[0_10px_40px_-12px_rgba(26,47,37,0.25)] backdrop-blur-xl">
          <a href="#top" className="group flex items-center gap-2.5 pl-1" aria-label="TỔ ẤM, về đầu trang">
            <span className="grid h-9 w-9 place-items-center rounded-lg bg-(--lp-forest) text-(--lp-mist) transition-transform duration-300 ease-out group-hover:-rotate-6 group-hover:scale-105">
              <House className="h-5 w-5" strokeWidth={1.75} />
            </span>
            <span className="font-bold tracking-tight text-(--lp-forest)">TỔ ẤM</span>
          </a>
          <nav className="hidden items-center gap-1 text-sm font-medium text-black md:flex" aria-label="Điều hướng chính">
            {NAV_LINKS.map((l) => (
              <a key={l.href} href={l.href} className="rounded-lg px-3.5 py-1.5 transition-colors duration-200 hover:bg-white hover:text-(--lp-forest)">
                {l.label}
              </a>
            ))}
          </nav>
          <div className="flex items-center gap-2">
            <a href="/login" className="rounded-lg px-3 py-2 text-sm font-semibold text-(--lp-forest) transition hover:bg-black/5">Đăng nhập</a>
            <a href="#bang-gia" className="lp-btn lp-btn-primary !px-5 !py-2.5 text-sm group">
              Đặt lịch <ArrowRight className="h-4 w-4 transition-transform duration-200 group-hover:translate-x-0.5" />
            </a>
          </div>
        </div>
      </header>
    </>
  )
}
