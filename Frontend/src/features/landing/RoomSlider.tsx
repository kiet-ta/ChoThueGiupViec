import { useCallback, useEffect, useRef, useState, type CSSProperties, type PointerEvent } from 'react'
import { ArrowLeft, ArrowRight, CircleCheck } from 'lucide-react'
import { ROOMS, ROOM_SLIDE_MS } from './data'
import { prefersReducedMotion } from './motion'

// Slides advance when the active tab's CSS progress animation ends (`onAnimationEnd`), so the bar and the
// timing can never drift apart. Hover, focus and being off-screen only pause that animation.
export function RoomSlider() {
  const [idx, setIdx] = useState(0)
  const [hovering, setHovering] = useState(false)
  const [inView, setInView] = useState(true)
  const [reduced] = useState(prefersReducedMotion)
  const box = useRef<HTMLDivElement>(null)
  const viewport = useRef<HTMLDivElement>(null)
  const track = useRef<HTMLDivElement>(null)
  const swipeX = useRef<number | null>(null)
  const n = ROOMS.length

  const go = useCallback((i: number) => setIdx(((i % n) + n) % n), [n])

  const layout = useCallback(() => {
    const t = track.current
    const v = viewport.current
    const slide = t?.children[idx] as HTMLElement | undefined
    if (!t || !v || !slide) return
    const max = t.scrollWidth - v.clientWidth
    t.style.transform = `translateX(${-Math.max(0, Math.min(slide.offsetLeft, max))}px)`
  }, [idx])

  useEffect(() => {
    layout()
    window.addEventListener('resize', layout)
    return () => window.removeEventListener('resize', layout)
  }, [layout])

  useEffect(() => {
    const el = box.current
    if (!el) return
    const io = new IntersectionObserver(([e]) => setInView(e.isIntersecting), { threshold: 0.35 })
    io.observe(el)
    return () => io.disconnect()
  }, [])

  const paused = hovering || !inView

  const onPointerUp = (e: PointerEvent) => {
    if (swipeX.current === null) return
    const dx = e.clientX - swipeX.current
    swipeX.current = null
    if (Math.abs(dx) > 50) go(idx + (dx < 0 ? 1 : -1))
  }

  return (
    <section id="cong-viec" className="bg-(--lp-sand) px-6 py-24 md:py-32 lg:px-12">
      <div className="mx-auto max-w-7xl">
        <div data-reveal className="flex flex-col justify-between gap-8 md:flex-row md:items-end">
          <h2 className="max-w-3xl text-[clamp(2.2rem,4vw,3.75rem)] font-bold leading-[1.05] tracking-[-0.03em] text-(--lp-forest)">
            Từng căn phòng, một danh sách việc rõ ràng.
          </h2>
          <div className="flex shrink-0 gap-2">
            <button type="button" aria-label="Phòng trước" onClick={() => go(idx - 1)} className="lp-btn lp-btn-outline lp-btn-icon">
              <ArrowLeft className="h-5 w-5" />
            </button>
            <button type="button" aria-label="Phòng kế tiếp" onClick={() => go(idx + 1)} className="lp-btn lp-btn-primary lp-btn-icon">
              <ArrowRight className="h-5 w-5" />
            </button>
          </div>
        </div>

        <div
          ref={box}
          className="mt-14"
          role="region"
          aria-roledescription="carousel"
          aria-label="Công việc theo từng phòng"
          onMouseEnter={() => setHovering(true)}
          onMouseLeave={() => setHovering(false)}
        >
          <div
            ref={viewport}
            className="select-none overflow-hidden touch-pan-y"
            onPointerDown={(e) => { swipeX.current = e.clientX }}
            onPointerUp={onPointerUp}
            onPointerCancel={() => { swipeX.current = null }}
          >
            <div ref={track} className="lp-track flex gap-4">
              {ROOMS.map((r, i) => (
                <article
                  key={r.name}
                  className={`lp-slide lp-card group relative h-96 shrink-0 basis-[90%] overflow-hidden bg-(--lp-forest) md:h-[30rem] md:basis-[74%] lg:h-[34rem] lg:basis-[66%] ${i === idx ? 'is-on' : ''}`}
                  role="group"
                  aria-roledescription="slide"
                  aria-label={`${i + 1} trên ${n}: ${r.name}`}
                >
                  <img
                    src={r.image}
                    alt={r.alt}
                    draggable={false}
                    loading="lazy"
                    className="absolute inset-0 h-full w-full object-cover transition-transform duration-[1200ms] ease-out group-hover:scale-105"
                  />
                  <div className="absolute inset-0 bg-linear-to-t from-(--lp-forest)/90 via-(--lp-forest)/25 to-transparent" />
                  <div className="absolute bottom-0 left-0 max-w-lg p-7 text-white md:p-10">
                    <h3 className="text-3xl font-bold tracking-tight md:text-4xl">{r.name}</h3>
                    <ul className="mt-4 space-y-2 text-sm text-white/90 md:text-base">
                      {r.tasks.map((t) => (
                        <li key={t} className="flex gap-2">
                          <CircleCheck className="h-5 w-5 shrink-0 text-(--lp-mist)" />
                          {t}
                        </li>
                      ))}
                    </ul>
                  </div>
                </article>
              ))}
            </div>
          </div>

          <div className="mt-8 grid grid-cols-4 gap-3 md:gap-5" role="tablist" aria-label="Chọn phòng">
            {ROOMS.map((r, i) => {
              const active = i === idx
              const fillClass = !active ? '' : reduced ? 'is-static' : `is-running ${paused ? 'is-paused' : ''}`
              return (
                <button key={r.name} type="button" role="tab" aria-selected={active} onClick={() => go(i)} className="lp-tab group text-left">
                  <span className="lp-tab-label block text-sm font-semibold md:text-base">{r.name}</span>
                  <span className="mt-3 block h-1 overflow-hidden rounded-full bg-(--lp-forest)/15">
                    <span
                      key={active ? `on-${idx}` : `off-${i}`}
                      className={`lp-fill block ${fillClass}`}
                      style={{ ['--lp-slide-ms' as string]: `${ROOM_SLIDE_MS}ms` } as CSSProperties}
                      onAnimationEnd={() => { if (active && !reduced) go(idx + 1) }}
                    />
                  </span>
                </button>
              )
            })}
          </div>
        </div>
      </div>
    </section>
  )
}
