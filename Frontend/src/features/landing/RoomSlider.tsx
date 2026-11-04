import { useCallback, useEffect, useRef, useState, type PointerEvent } from 'react'
import { ArrowLeft, ArrowRight, CheckCircle2, ShieldCheck, Sparkles } from 'lucide-react'
import { ROOMS, ROOM_SLIDE_MS } from './data'
import { gsap, prefersReducedMotion } from './motion'

export function RoomSlider() {
  const [idx, setIdx] = useState(0)
  const [hovering, setHovering] = useState(false)
  const [inView, setInView] = useState(true)
  const box = useRef<HTMLDivElement>(null)
  const slideRef = useRef<HTMLDivElement>(null)
  const swipeX = useRef<number | null>(null)
  const timerRef = useRef<number | null>(null)
  const progressRef = useRef<HTMLDivElement>(null)
  const n = ROOMS.length

  const go = useCallback((i: number) => {
    setIdx(((i % n) + n) % n)
  }, [n])

  // GSAP Cinematic Reveal on Room Switch
  useEffect(() => {
    if (!slideRef.current || prefersReducedMotion()) return

    const ctx = gsap.context(() => {
      // Photo cinematic focus
      gsap.fromTo(
        '.room-photo-hero',
        { scale: 1.08, filter: 'blur(5px)', opacity: 0.8 },
        { scale: 1, filter: 'blur(0px)', opacity: 1, duration: 0.75, ease: 'power2.out' }
      )

      // Title punch
      gsap.fromTo(
        '.room-title-hero',
        { y: -20, opacity: 0 },
        { y: 0, opacity: 1, duration: 0.5, ease: 'power3.out' }
      )

      // Floating glass card slide-up
      gsap.fromTo(
        '.room-glass-card',
        { y: 30, opacity: 0 },
        { y: 0, opacity: 1, duration: 0.55, ease: 'back.out(1.4)', delay: 0.1 }
      )

      // Bullets stagger in
      gsap.fromTo(
        '.room-task-bullet',
        { x: -15, opacity: 0 },
        { x: 0, opacity: 1, duration: 0.4, stagger: 0.08, ease: 'power2.out', delay: 0.2 }
      )
    }, slideRef.current)

    return () => ctx.revert()
  }, [idx])

  // Auto-advance timer synchronized with progress line
  useEffect(() => {
    if (prefersReducedMotion() || hovering || !inView) {
      if (timerRef.current) clearInterval(timerRef.current)
      return
    }

    const pBar = progressRef.current
    if (pBar) {
      gsap.killTweensOf(pBar)
      gsap.fromTo(pBar, { width: '0%' }, { width: '100%', duration: ROOM_SLIDE_MS / 1000, ease: 'none' })
    }

    timerRef.current = window.setInterval(() => {
      go(idx + 1)
    }, ROOM_SLIDE_MS)

    return () => {
      if (timerRef.current) clearInterval(timerRef.current)
    }
  }, [idx, hovering, inView, go])

  // Intersection Observer
  useEffect(() => {
    const el = box.current
    if (!el) return
    const io = new IntersectionObserver(([e]) => setInView(e.isIntersecting), { threshold: 0.25 })
    io.observe(el)
    return () => io.disconnect()
  }, [])

  const onPointerUp = (e: PointerEvent) => {
    if (swipeX.current === null) return
    const dx = e.clientX - swipeX.current
    swipeX.current = null
    if (Math.abs(dx) > 40) go(idx + (dx < 0 ? 1 : -1))
  }

  const currentRoom = ROOMS[idx]

  return (
    <section id="cong-viec" className="bg-(--lp-sand) px-6 py-24 md:py-32 lg:px-12 relative overflow-hidden">
      <div className="mx-auto max-w-7xl">
        {/* Section Header */}
        <div data-reveal className="flex flex-col justify-between gap-6 md:flex-row md:items-end">
          <div>
            <div className="inline-flex items-center gap-2 rounded-full border border-(--lp-line) bg-white/80 px-3 py-1 text-xs font-semibold text-(--lp-pine) shadow-xs backdrop-blur-sm">
              <Sparkles className="h-3.5 w-3.5" />
              <span>Quy trình chuẩn từng không gian</span>
            </div>
            <h2 className="mt-3 max-w-3xl text-[clamp(2.2rem,4vw,3.75rem)] font-bold leading-[1.05] tracking-[-0.03em] text-(--lp-forest)">
              Từng căn phòng, một danh sách việc rõ ràng.
            </h2>
          </div>

          <div className="flex shrink-0 items-center gap-4">
            <span className="text-sm font-bold text-(--lp-forest)/80 tabular-nums">
              <span className="text-xl font-extrabold text-(--lp-forest)">0{idx + 1}</span> / 0{n}
            </span>
            <div className="flex gap-2">
              <button
                type="button"
                aria-label="Phòng trước"
                onClick={() => go(idx - 1)}
                className="lp-btn lp-btn-outline lp-btn-icon shadow-sm hover:scale-105 active:scale-95 transition-all duration-200 cursor-pointer"
              >
                <ArrowLeft className="h-5 w-5" />
              </button>
              <button
                type="button"
                aria-label="Phòng kế tiếp"
                onClick={() => go(idx + 1)}
                className="lp-btn lp-btn-primary lp-btn-icon shadow-sm hover:scale-105 active:scale-95 transition-all duration-200 cursor-pointer"
              >
                <ArrowRight className="h-5 w-5" />
              </button>
            </div>
          </div>
        </div>

        {/* Visual-First Cinematic Room Showcase */}
        <div
          ref={box}
          className="mt-10"
          role="region"
          aria-roledescription="carousel"
          aria-label="Công việc theo từng phòng"
          onMouseEnter={() => setHovering(true)}
          onMouseLeave={() => setHovering(false)}
        >
          <div
            ref={slideRef}
            className="select-none overflow-hidden touch-pan-y"
            onPointerDown={(e) => { swipeX.current = e.clientX }}
            onPointerUp={onPointerUp}
            onPointerCancel={() => { swipeX.current = null }}
          >
            <article className="group relative w-full h-[32rem] md:h-[38rem] overflow-hidden rounded-3xl shadow-xl border border-(--lp-line) bg-white">
              {/* Eye-Catching Full-Bleed Photography - Sáng, tự nhiên, không màn đen */}
              <img
                key={currentRoom.name}
                src={currentRoom.image}
                alt={currentRoom.alt}
                draggable={false}
                decoding="async"
                className="room-photo-hero absolute inset-0 h-full w-full object-cover object-center filter brightness-100 contrast-[102%] transition-transform duration-[1200ms] ease-out group-hover:scale-105 will-change-transform"
              />

              {/* Top Header: Room Title & Badge */}
              <div className="absolute top-6 left-6 right-6 md:top-8 md:left-8 md:right-8 flex items-start justify-between z-10 pointer-events-none">
                <div className="flex flex-col items-start gap-2">
                  <span className="inline-flex items-center gap-1.5 rounded-full bg-white/95 backdrop-blur-md px-3.5 py-1 text-xs font-black uppercase tracking-wider text-(--lp-pine) border border-white/80 shadow-xs">
                    Khu vực 0{idx + 1}
                  </span>
                  <h3 className="room-title-hero text-2xl md:text-4xl lg:text-5xl font-black tracking-tight text-(--lp-forest) bg-white/90 backdrop-blur-md px-4 py-2 rounded-2xl border border-white/80 shadow-md">
                    {currentRoom.name}
                  </h3>
                </div>

                <div className="hidden sm:inline-flex items-center gap-2 rounded-full bg-white/95 backdrop-blur-md px-4 py-2 text-xs font-bold text-(--lp-forest) border border-white/80 shadow-xs">
                  <ShieldCheck className="h-4 w-4 text-(--lp-pine)" /> Nghiệm thu 100%
                </div>
              </div>

              {/* Bottom Floating Minimalist Checklist Card (Sáng sủa, tinh gọn, không màn đen) */}
              <div className="absolute bottom-6 left-6 right-6 md:bottom-8 md:left-8 md:right-auto z-10 max-w-lg">
                <div className="room-glass-card rounded-2xl bg-white/95 backdrop-blur-xl border border-white/80 p-5 md:p-6 shadow-xl">
                  <p className="text-[11px] font-black uppercase tracking-widest text-(--lp-pine) mb-3">
                    Checklist nghiệm thu:
                  </p>

                  <ul className="space-y-2.5">
                    {currentRoom.tasks.map((task) => (
                      <li
                        key={task}
                        className="room-task-bullet flex items-center gap-3 text-sm md:text-base font-semibold text-(--lp-forest)"
                      >
                        <span className="grid h-5 w-5 shrink-0 place-items-center rounded-full bg-(--lp-mist)/50 text-(--lp-pine)">
                          <CheckCircle2 className="h-3.5 w-3.5" />
                        </span>
                        <span className="leading-snug">{task}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              </div>
            </article>
          </div>

          {/* Minimalist Tab Selector with Progress Indicator */}
          <div className="mt-6 grid grid-cols-2 md:grid-cols-4 gap-3 md:gap-4" role="tablist" aria-label="Chọn phòng">
            {ROOMS.map((r, i) => {
              const active = i === idx
              return (
                <button
                  key={r.name}
                  type="button"
                  role="tab"
                  aria-selected={active}
                  onClick={() => go(i)}
                  className={`group relative text-left rounded-xl p-3.5 transition-all duration-200 overflow-hidden cursor-pointer ${
                    active
                      ? 'bg-white shadow-md border border-(--lp-line) scale-[1.02]'
                      : 'bg-white/60 hover:bg-white/90 hover:shadow-xs'
                  }`}
                >
                  <div className="flex items-center justify-between">
                    <span className={`text-sm md:text-base font-extrabold ${active ? 'text-(--lp-forest)' : 'text-(--lp-forest)/60 group-hover:text-(--lp-forest)'}`}>
                      {r.name}
                    </span>
                    <span className="text-xs font-bold text-(--lp-forest)/40">0{i + 1}</span>
                  </div>

                  {/* Progress Indicator */}
                  <div className="mt-2.5 h-1 w-full overflow-hidden rounded-full bg-(--lp-forest)/10">
                    {active && (
                      <div
                        ref={progressRef}
                        className="h-full bg-(--lp-forest) rounded-full will-change-[width]"
                        style={{ width: '0%' }}
                      />
                    )}
                  </div>
                </button>
              )
            })}
          </div>
        </div>
      </div>
    </section>
  )
}
