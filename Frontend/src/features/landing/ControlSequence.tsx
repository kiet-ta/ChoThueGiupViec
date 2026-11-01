import { useEffect, useRef } from 'react'
import { ArrowRight, ClipboardCheck, MapPin, ShieldCheck } from 'lucide-react'
import { CONTROL_STEPS } from './data'
import { gsap, ScrollTrigger, prefersReducedMotion } from './motion'

const ICONS = { shield: ShieldCheck, pin: MapPin, check: ClipboardCheck } as const
const N = CONTROL_STEPS.length
/** The vertical rule starts slightly filled so step 1 already reads as active. */
const RULE_START = 0.08

export function ControlSequence() {
  const root = useRef<HTMLElement>(null)

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

      gsap.set(panels.slice(1), { opacity: 0, y: 36 })
      gsap.set(items.slice(1), { opacity: 0.35 })
      gsap.set(rule, { scaleY: RULE_START, transformOrigin: 'top' })

      // Timeline units: step k is held on [k, k + 0.5]; the transition into step k runs on [k - 0.5, k].
      const tl = gsap.timeline({
        defaults: { ease: 'none' },
        scrollTrigger: {
          trigger: section,
          start: 'top top',
          end: `+=${N * 85}%`,
          pin: true,
          scrub: 0.6,
          anticipatePin: 1,
          snap: { snapTo: 'labels', duration: { min: 0.25, max: 0.7 }, ease: 'power2.inOut', delay: 0.05 },
        },
      })
      for (let k = 0; k < N; k++) tl.addLabel(`s${k}`, k)
      for (let k = 1; k < N; k++) {
        const t = k - 0.5
        tl.to(panels[k - 1], { opacity: 0, y: -36, duration: 0.5 }, t)
          .to(panels[k], { opacity: 1, y: 0, duration: 0.5 }, t)
          .to(items[k - 1], { opacity: 0.35, duration: 0.5 }, t)
          .to(items[k], { opacity: 1, duration: 0.5 }, t)
          .to(rule, { scaleY: RULE_START + (1 - RULE_START) * (k / (N - 1)), duration: 0.5 }, t)
      }
      tl.to({}, { duration: 0.5 }, N - 1) // hold on the last step before the pin releases
      ScrollTrigger.refresh()

      return () => section.classList.remove('is-pinned')
    })

    return () => mm.revert()
  }, [])

  return (
    <section ref={root} id="an-tam" className="lp-seq relative isolate overflow-hidden bg-(--lp-forest) px-6 py-24 text-white md:py-32 lg:px-12">
      <div className="pointer-events-none absolute -right-40 -top-40 -z-10 h-[34rem] w-[34rem] rounded-full bg-(--lp-sage)/30 blur-[120px]" />
      <div className="mx-auto grid w-full max-w-7xl items-center gap-14 lg:grid-cols-12 lg:gap-20">
        <div className="lg:col-span-5">
          <h2 className="text-[clamp(2.2rem,4vw,3.75rem)] font-bold leading-[1.05] tracking-[-0.03em] text-(--lp-mist)">
            Ba lớp kiểm soát trong mỗi ca dọn.
          </h2>
          <p className="mt-6 max-w-md text-lg text-white/70">
            Loại bỏ sự lơ là và nguy cơ thất thoát tài sản, từ lúc ghép nhân sự đến khi bạn thanh toán.
          </p>
          <ol className="relative mt-12 hidden lg:block">
            <span className="absolute left-0 top-0 h-full w-px bg-white/15" />
            <span className="lp-rule-fill absolute left-0 top-0 h-full w-px bg-(--lp-mist)" />
            {CONTROL_STEPS.map((s, i) => (
              <li key={s.title} className="lp-step-item flex items-baseline gap-4 py-3 pl-6">
                <span className="lp-num text-sm text-(--lp-mist)/70">{String(i + 1).padStart(2, '0')}</span>
                <span className="text-xl font-semibold tracking-tight md:text-2xl">{s.title}</span>
              </li>
            ))}
          </ol>
        </div>

        <div className="lg:col-span-7">
          <div className="lp-stage space-y-5">
            {CONTROL_STEPS.map((s, i) => {
              const Icon = ICONS[s.icon]
              return (
                <article key={s.title} className="lp-panel lp-card flex flex-col justify-between gap-10 border border-white/10 bg-[#244034] p-8 shadow-2xl md:p-12">
                  <div className="flex items-center justify-between">
                    <span className="grid h-14 w-14 place-items-center rounded-lg bg-white/10 text-(--lp-mist)">
                      <Icon className="h-7 w-7" />
                    </span>
                    <span className="lp-num text-sm text-(--lp-mist)/60">{String(i + 1).padStart(2, '0')} / {String(N).padStart(2, '0')}</span>
                  </div>
                  <div>
                    <h3 className="text-3xl font-bold tracking-tight md:text-5xl">{s.title}</h3>
                    <p className="mt-4 max-w-xl text-lg text-white/75">{s.desc}</p>
                  </div>
                  <p className="border-t border-white/10 pt-5 text-sm text-(--lp-mist)/80">{s.note}</p>
                </article>
              )
            })}
          </div>
          <a href="#bang-gia" className="lp-btn lp-btn-light mt-8 lg:hidden">
            Đặt lịch ngay <ArrowRight className="h-[18px] w-[18px]" />
          </a>
        </div>
      </div>
    </section>
  )
}
