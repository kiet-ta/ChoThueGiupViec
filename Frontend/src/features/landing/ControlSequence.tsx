import { useEffect, useRef, useState, type MouseEvent } from 'react'
import { ArrowRight, ClipboardCheck, MapPin, ShieldCheck, Sparkles, CheckCircle2 } from 'lucide-react'
import { CONTROL_STEPS } from './data'
import { gsap, ScrollTrigger, prefersReducedMotion } from './motion'

const ICONS = { shield: ShieldCheck, pin: MapPin, check: ClipboardCheck } as const
const N = CONTROL_STEPS.length
/** The vertical rule starts slightly filled so step 1 already reads as active. */
const RULE_START = 0.08

export function ControlSequence() {
  const root = useRef<HTMLElement>(null)
  const [activeStep, setActiveStep] = useState(0)
  const stRef = useRef<ScrollTrigger | null>(null)

  useEffect(() => {
    const section = root.current
    if (!section || prefersReducedMotion()) return

    // Desktop only: the section is pinned and scrolling scrubs through the steps.
    // Below 1024 px the cards simply stay stacked.
    const mm = gsap.matchMedia()
    mm.add('(min-width: 1024px)', () => {
      section.classList.add('is-pinned')
      const panels = gsap.utils.toArray<HTMLElement>('.lp-panel', section)
      const items = gsap.utils.toArray<HTMLElement>('.lp-step-item', section)
      const rule = section.querySelector<HTMLElement>('.lp-rule-fill')

      // Set initial state: panel 0 active & visible, others hidden
      gsap.set(panels[0], { opacity: 1, y: 0, scale: 1, zIndex: 10, visibility: 'visible', pointerEvents: 'auto' })
      panels.slice(1).forEach((p, idx) => {
        gsap.set(p, { opacity: 0, y: 40, scale: 0.95, zIndex: idx + 1, visibility: 'hidden', pointerEvents: 'none' })
      })
      gsap.set(items[0], { opacity: 1 })
      gsap.set(items.slice(1), { opacity: 0.45 })
      gsap.set(rule, { scaleY: RULE_START, transformOrigin: 'top' })

      // Timeline units:
      // t = 0 to 0.6: Step 0 is held
      // t = 0.6 to 1.2: Transition from Step 0 to Step 1
      // t = 1.2 to 1.8: Step 1 is held
      // t = 1.8 to 2.4: Transition from Step 1 to Step 2
      // t = 2.4 to 3.2: Step 2 is held before unpinning
      const tl = gsap.timeline({
        defaults: { ease: 'power2.inOut' },
        scrollTrigger: {
          trigger: section,
          start: 'top top',
          end: `+=${N * 100}%`,
          pin: true,
          scrub: 0.6,
          anticipatePin: 1,
          onUpdate: (self) => {
            const step = Math.min(N - 1, Math.floor(self.progress * N))
            setActiveStep(step)
          },
        },
      })

      stRef.current = tl.scrollTrigger ?? null

      // Build transition sequence between panels
      for (let k = 1; k < N; k++) {
        const t = (k - 1) * 1.2 + 0.6
        // Fade out previous panel
        tl.to(
          panels[k - 1],
          {
            opacity: 0,
            y: -40,
            scale: 0.95,
            zIndex: 1,
            visibility: 'hidden',
            pointerEvents: 'none',
            duration: 0.6,
          },
          t
        )
        // Fade in current panel
        .to(
          panels[k],
          {
            opacity: 1,
            y: 0,
            scale: 1,
            zIndex: 10,
            visibility: 'visible',
            pointerEvents: 'auto',
            duration: 0.6,
          },
          t
        )
        // Update left step indicators
        .to(items[k - 1], { opacity: 0.45, duration: 0.4 }, t)
        .to(items[k], { opacity: 1, duration: 0.4 }, t)
        .to(rule, { scaleY: RULE_START + (1 - RULE_START) * (k / (N - 1)), duration: 0.6 }, t)
      }

      // Generous hold on the final step before the section releases
      tl.to({}, { duration: 0.8 }, 2.4)

      // Refresh scroll metrics after layout settles
      const tId = setTimeout(() => ScrollTrigger.refresh(), 200)

      return () => {
        clearTimeout(tId)
        section.classList.remove('is-pinned')
      }
    })

    return () => mm.revert()
  }, [])

  // Allow clicking on left steps to smoothly jump to that card
  const handleStepClick = (index: number) => {
    const st = stRef.current
    if (!st) return
    const targetScroll = st.start + (st.end - st.start) * (index / (N - 1)) * 0.85
    window.scrollTo({ top: targetScroll, behavior: 'smooth' })
  }

  // Interactive High-Energy VIP Specular Shine on Inner Black Card
  const handleMouseEnter = (e: MouseEvent<HTMLElement>) => {
    if (prefersReducedMotion()) return
    const card = e.currentTarget
    const beam = card.querySelector<HTMLElement>('.vip-shine-beam')
    if (beam) {
      gsap.killTweensOf(beam)
      gsap.fromTo(
        beam,
        { x: '-150%', opacity: 0.95 },
        { x: '450%', opacity: 0.95, duration: 0.75, ease: 'power2.inOut' }
      )
    }
  }

  const handleMouseMove = (e: MouseEvent<HTMLElement>) => {
    if (prefersReducedMotion()) return
    const card = e.currentTarget
    const rect = card.getBoundingClientRect()
    const x = e.clientX - rect.left
    const y = e.clientY - rect.top
    const spotlight = card.querySelector<HTMLElement>('.vip-spotlight')
    if (spotlight) {
      spotlight.style.opacity = '1'
      spotlight.style.background = `radial-gradient(350px circle at ${x}px ${y}px, rgba(197, 236, 201, 0.25), transparent 70%)`
    }
  }

  const handleMouseLeave = (e: MouseEvent<HTMLElement>) => {
    if (prefersReducedMotion()) return
    const card = e.currentTarget
    const spotlight = card.querySelector<HTMLElement>('.vip-spotlight')
    if (spotlight) spotlight.style.opacity = '0'
  }

  return (
    <section ref={root} id="an-tam" className="lp-seq relative isolate overflow-hidden bg-(--lp-forest) px-6 py-24 text-white md:py-32 lg:px-12">
      <div className="pointer-events-none absolute -right-40 -top-40 -z-10 h-[34rem] w-[34rem] rounded-full bg-(--lp-sage)/30 blur-[120px]" />
      <div className="pointer-events-none absolute -left-32 bottom-0 -z-10 h-96 w-96 rounded-full bg-(--lp-pine)/25 blur-[100px]" />

      <div className="mx-auto grid w-full max-w-7xl items-center gap-14 lg:grid-cols-12 lg:gap-20">
        {/* Left Column: Heading & Step Indicators */}
        <div className="lg:col-span-5">
          <div className="inline-flex items-center gap-2 rounded-full border border-white/20 bg-white/10 px-3.5 py-1.5 text-xs font-semibold text-(--lp-mist) shadow-xs backdrop-blur-md">
            <Sparkles className="h-3.5 w-3.5 text-(--lp-mist)" />
            <span>Cam kết an tâm tuyệt đối</span>
          </div>
          <h2 className="mt-3 text-[clamp(2.2rem,4vw,3.75rem)] font-bold leading-[1.05] tracking-[-0.03em] text-white">
            Ba lớp kiểm soát trong mỗi ca dọn.
          </h2>
          <p className="mt-6 max-w-md text-lg text-white/75 leading-relaxed">
            Loại bỏ sự lơ là và nguy cơ thất thoát tài sản, từ lúc ghép nhân sự đến khi bạn thanh toán.
          </p>

          <ol className="relative mt-12 hidden lg:block" role="tablist" aria-label="Các bước kiểm soát">
            <span className="absolute left-0 top-0 h-full w-px bg-white/15" />
            <span className="lp-rule-fill absolute left-0 top-0 h-full w-0.5 bg-(--lp-mist) will-change-transform" />
            {CONTROL_STEPS.map((s, i) => (
              <li
                key={s.title}
                onClick={() => handleStepClick(i)}
                className={`lp-step-item flex items-baseline gap-4 py-3.5 pl-6 cursor-pointer transition-all duration-300 hover:text-white ${
                  activeStep === i ? 'text-white' : 'text-white/45'
                }`}
                role="tab"
                aria-selected={activeStep === i}
              >
                <span className={`lp-num text-sm font-bold transition-colors ${
                  activeStep === i ? 'text-(--lp-mist)' : 'text-white/35'
                }`}>
                  {String(i + 1).padStart(2, '0')}
                </span>
                <span className={`text-xl tracking-tight md:text-2xl ${
                  activeStep === i ? 'font-bold' : 'font-semibold'
                }`}>
                  {s.title}
                </span>
              </li>
            ))}
          </ol>
        </div>

        {/* Right Column: Stage with Stacked Luxury White Cards */}
        <div className="lg:col-span-7">
          <div className="lp-stage space-y-6">
            {CONTROL_STEPS.map((s, i) => {
              const Icon = ICONS[s.icon]
              return (
                <article
                  key={s.title}
                  className="lp-panel lp-card will-change-[transform,opacity]"
                >
                  <div
                    onMouseEnter={handleMouseEnter}
                    onMouseMove={handleMouseMove}
                    onMouseLeave={handleMouseLeave}
                    className="group relative flex h-full min-h-[28rem] flex-col justify-between gap-8 rounded-3xl border border-white/60 bg-white p-8 shadow-[0_30px_70px_-15px_rgba(0,0,0,0.55)] md:p-12 overflow-hidden cursor-default transition-all duration-300 hover:shadow-[0_35px_80px_-15px_rgba(0,0,0,0.7)] hover:-translate-y-1"
                  >
                    {/* VIP Specular Light Sweep Beam (GSAP Powered) */}
                    <div
                      className="vip-shine-beam pointer-events-none absolute -top-1/2 left-0 h-[220%] w-40 -rotate-25 opacity-0 z-30"
                      style={{
                        background:
                          'linear-gradient(90deg, transparent 0%, rgba(255,255,255,0.4) 20%, rgba(197,236,201,0.65) 50%, rgba(255,255,255,0.4) 80%, transparent 100%)',
                        filter: 'blur(3px)',
                      }}
                    />

                    {/* Interactive Cursor Spotlight */}
                    <div className="vip-spotlight pointer-events-none absolute inset-0 z-20 opacity-0 transition-opacity duration-300" />

                    {/* Top Row: Icon Badge & Step Number Pill */}
                    <div className="flex items-center justify-between relative z-10">
                      <div className="flex items-center gap-3">
                        <span className="grid h-14 w-14 place-items-center rounded-2xl bg-(--lp-sand) border border-(--lp-line) text-(--lp-forest) shadow-xs transition-transform duration-300 group-hover:scale-105">
                          <Icon className="h-7 w-7 text-(--lp-pine)" />
                        </span>
                      </div>

                      {/* Prominent Number Badge */}
                      <span className="inline-flex items-center gap-2 rounded-full border border-(--lp-line) bg-(--lp-sand) px-4 py-1.5 text-xs font-black tracking-widest text-(--lp-forest) shadow-xs">
                        <span className="h-2 w-2 rounded-full bg-(--lp-pine) animate-pulse" />
                        <span>VIP PROTOCOL 0{i + 1} / 0{N}</span>
                      </span>
                    </div>

                    {/* Content: Title & Description */}
                    <div className="relative z-10">
                      <h3 className="text-3xl font-black tracking-tight text-(--lp-forest) md:text-5xl">
                        {s.title}
                      </h3>
                      <p className="mt-4 max-w-xl text-lg text-(--lp-ink-soft) leading-relaxed">
                        {s.desc}
                      </p>
                    </div>

                    {/* Card Footer: Security Note & Trust Watermark */}
                    <div className="relative z-10 flex items-center justify-between border-t border-(--lp-line) pt-5 text-sm">
                      <span className="font-semibold text-(--lp-pine) flex items-center gap-2">
                        <CheckCircle2 className="h-4 w-4 text-(--lp-pine)" />
                        {s.note}
                      </span>
                      <span className="text-xs uppercase tracking-[0.2em] font-mono font-bold text-(--lp-forest)/40">
                        Bảo chứng minh bạch
                      </span>
                    </div>
                  </div>
                </article>
              )
            })}
          </div>

          <a href="#bang-gia" className="lp-btn lp-btn-light mt-8 lg:hidden shadow-lg">
            Đặt lịch ngay <ArrowRight className="h-[18px] w-[18px]" />
          </a>
        </div>
      </div>
    </section>
  )
}
