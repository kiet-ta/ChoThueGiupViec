import { useEffect, useRef, useState, type MouseEvent } from 'react'
import { Bed, Building2, CalendarDays, CheckCircle2, Sparkles, Zap } from 'lucide-react'
import { PLANS } from './data'
import { gsap, prefersReducedMotion } from './motion'

const PLAN_ICON = { studio: Bed, apartment: Building2, townhouse: Building2 } as const

// Parse numeric string e.g. "180.000" -> 180000
function parsePrice(raw: string): number {
  return parseInt(raw.replace(/\./g, ''), 10) || 0
}

function formatVnd(val: number): string {
  return Math.round(val).toLocaleString('vi-VN').replace(/,/g, '.')
}

export function PricingPlans() {
  const [billing, setBilling] = useState<'single' | 'monthly'>('single')
  const containerRef = useRef<HTMLDivElement>(null)
  const animPricesRef = useRef({ studio: 0, apartment: 0, townhouse: 0 })
  const [displayPrices, setDisplayPrices] = useState({ studio: 180000, apartment: 240000, townhouse: 480000 })
  const hasTriggeredRef = useRef(false)

  // GSAP Odometer & Punch Animation Function
  const animatePrices = (targetBilling: 'single' | 'monthly') => {
    if (prefersReducedMotion()) {
      const isM = targetBilling === 'monthly'
      setDisplayPrices({
        studio: isM ? 144000 : 180000,
        apartment: isM ? 192000 : 240000,
        townhouse: isM ? 384000 : 480000,
      })
      return
    }

    const isMonthly = targetBilling === 'monthly'
    const targets = {
      studio: isMonthly ? 144000 : 180000,
      apartment: isMonthly ? 192000 : 240000,
      townhouse: isMonthly ? 384000 : 480000,
    }

    // Kinetic card pop
    gsap.fromTo(
      '.pricing-plan-card',
      { y: 15, scale: 0.96 },
      { y: 0, scale: 1, duration: 0.45, stagger: 0.06, ease: 'back.out(2)' }
    )

    // Numbers roll up rapidly with power3 easing
    gsap.to(animPricesRef.current, {
      studio: targets.studio,
      apartment: targets.apartment,
      townhouse: targets.townhouse,
      duration: 0.75,
      ease: 'power3.out',
      onUpdate: () => {
        setDisplayPrices({
          studio: Math.round(animPricesRef.current.studio),
          apartment: Math.round(animPricesRef.current.apartment),
          townhouse: Math.round(animPricesRef.current.townhouse),
        })
      },
    })

    // Punch the number elements
    gsap.fromTo(
      '.price-num-hero',
      { scale: 0.85, filter: 'blur(3px)' },
      { scale: 1, filter: 'blur(0px)', duration: 0.4, ease: 'back.out(2.2)' }
    )
  }

  // Scroll entrance with GSAP
  useEffect(() => {
    const el = containerRef.current
    if (!el) return

    const io = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting && !hasTriggeredRef.current) {
          hasTriggeredRef.current = true
          // Animate from 0 to base prices on initial scroll reveal
          animPricesRef.current = { studio: 0, apartment: 0, townhouse: 0 }
          animatePrices('single')
        }
      },
      { threshold: 0.2 }
    )
    io.observe(el)
    return () => io.disconnect()
  }, [])

  const handleToggle = (mode: 'single' | 'monthly') => {
    if (mode === billing) return
    setBilling(mode)
    animatePrices(mode)
  }

  const handleCardMouseMove = (e: MouseEvent<HTMLElement>) => {
    if (prefersReducedMotion()) return
    const card = e.currentTarget
    const rect = card.getBoundingClientRect()
    const x = e.clientX - rect.left
    const y = e.clientY - rect.top
    const centerX = rect.width / 2
    const centerY = rect.height / 2
    const rotateX = ((y - centerY) / centerY) * -6
    const rotateY = ((x - centerX) / centerX) * 6

    gsap.to(card, {
      rotateX,
      rotateY,
      transformPerspective: 1000,
      duration: 0.25,
      ease: 'power1.out',
    })
  }

  const handleCardMouseLeave = (e: MouseEvent<HTMLElement>) => {
    if (prefersReducedMotion()) return
    const card = e.currentTarget
    gsap.to(card, {
      rotateX: 0,
      rotateY: 0,
      duration: 0.45,
      ease: 'power2.out',
    })
  }

  return (
    <section ref={containerRef} id="bang-gia" className="px-6 py-24 md:py-32 lg:px-12 relative overflow-hidden">
      <div className="mx-auto max-w-7xl">
        <div data-reveal className="flex flex-col md:flex-row md:items-end justify-between gap-8">
          <div>
            <div className="inline-flex items-center gap-2 rounded-full border border-(--lp-line) bg-white px-3 py-1 text-xs font-semibold text-(--lp-pine) shadow-xs">
              <Sparkles className="h-3.5 w-3.5" />
              <span>Bảng giá niêm yết minh bạch</span>
            </div>
            <h2 className="mt-3 max-w-4xl text-[clamp(2.2rem,4vw,3.75rem)] font-bold leading-[1.05] tracking-[-0.03em] text-(--lp-forest)">
              Giá minh bạch cho từng loại không gian.
            </h2>
            <p className="mt-3 text-base text-(--lp-ink-soft) md:text-lg">
              Cam kết không phát sinh chi phí ngoài giờ hay ngày lễ. Hài lòng 100% mới thanh toán.
            </p>
          </div>

          {/* Interactive Billing Mode Switcher (Arena3 style) */}
          <div className="flex items-center self-start md:self-end rounded-full bg-(--lp-sand) p-1.5 border border-(--lp-line) shadow-inner">
            <button
              type="button"
              onClick={() => handleToggle('single')}
              className={`rounded-full px-5 py-2.5 text-xs md:text-sm font-bold transition-all duration-300 cursor-pointer ${
                billing === 'single'
                  ? 'bg-(--lp-forest) text-white shadow-lg scale-105'
                  : 'text-(--lp-forest)/70 hover:text-(--lp-forest)'
              }`}
            >
              Theo ca lẻ
            </button>
            <button
              type="button"
              onClick={() => handleToggle('monthly')}
              className={`flex items-center gap-2 rounded-full px-5 py-2.5 text-xs md:text-sm font-bold transition-all duration-300 cursor-pointer ${
                billing === 'monthly'
                  ? 'bg-(--lp-forest) text-white shadow-lg scale-105'
                  : 'text-(--lp-forest)/70 hover:text-(--lp-forest)'
              }`}
            >
              <span>Thuê bao định kỳ</span>
              <span className="rounded-full bg-(--lp-amber) px-2 py-0.5 text-[10px] font-black uppercase text-white tracking-widest shadow-xs">
                -20%
              </span>
            </button>
          </div>
        </div>

        {/* Pricing Cards Grid with 3D Tilt Physics */}
        <div className="mt-14 grid grid-cols-1 items-stretch gap-6 lg:grid-cols-3">
          {PLANS.map((p) => {
            const Icon = PLAN_ICON[p.key]
            const f = p.featured
            const currentNum = displayPrices[p.key as keyof typeof displayPrices] ?? parsePrice(p.price)

            return (
              <article
                key={p.key}
                onMouseMove={handleCardMouseMove}
                onMouseLeave={handleCardMouseLeave}
                className={`pricing-plan-card lp-card group relative flex flex-col justify-between p-8 md:p-10 transition-shadow duration-300 shadow-lg hover:shadow-2xl will-change-transform transform-gpu ${
                  f
                    ? 'bg-(--lp-forest) text-white ring-2 ring-(--lp-mist) shadow-[0_35px_80px_-25px_rgba(26,47,37,0.8)] lg:-translate-y-4'
                    : 'border border-(--lp-line) bg-white hover:border-(--lp-forest)/40'
                }`}
              >
                {f && (
                  <div className="absolute -top-3.5 left-1/2 -translate-x-1/2 rounded-full bg-(--lp-mist) px-4 py-1 text-xs font-black uppercase tracking-wider text-(--lp-forest) shadow-md flex items-center gap-1.5">
                    <Zap className="h-3.5 w-3.5 fill-current" /> Phổ biến nhất (78% khách chọn)
                  </div>
                )}

                <div>
                  <div className="flex items-center justify-between text-sm">
                    <span className={`lp-num font-bold text-xs uppercase tracking-wider ${f ? 'text-(--lp-mist)' : 'text-(--lp-pine)'}`}>
                      {p.range}
                    </span>
                    <span className={`grid h-10 w-10 place-items-center rounded-xl shadow-xs transition-transform duration-300 group-hover:scale-110 ${f ? 'bg-white/10 text-(--lp-mist)' : 'bg-(--lp-sand) text-(--lp-forest)'}`}>
                      <Icon className="h-5 w-5" />
                    </span>
                  </div>

                  <h3 className={`mt-6 text-2xl md:text-3xl font-black tracking-tight ${f ? 'text-white' : 'text-(--lp-forest)'}`}>
                    {p.name}
                  </h3>
                  <p className={`mt-2 text-sm leading-relaxed ${f ? 'text-white/75' : 'text-(--lp-ink-soft)'}`}>
                    {p.blurb}
                  </p>

                  {/* Punchy Animated GSAP Price Display */}
                  <div className="mt-8 border-y border-current/10 py-5">
                    <div className="flex items-baseline gap-2">
                      <span
                        className={`price-num-hero lp-num text-5xl md:text-6xl font-black tracking-tighter inline-block tabular-nums ${
                          f ? 'text-white' : 'text-(--lp-forest)'
                        }`}
                      >
                        {formatVnd(currentNum)}
                      </span>
                      <div className="flex flex-col">
                        <span className={`text-xs font-black uppercase tracking-wider ${f ? 'text-(--lp-mist)' : 'text-(--lp-pine)'}`}>
                          VNĐ
                        </span>
                        <span className={`text-xs ${f ? 'text-white/60' : 'text-(--lp-ink-soft)'}`}>
                          / ca dọn
                        </span>
                      </div>
                    </div>

                    {billing === 'monthly' && (
                      <div className="mt-2.5 flex items-center gap-2 text-xs">
                        <span className="line-through opacity-50 font-semibold">{p.price} đ</span>
                        <span className="rounded-md bg-(--lp-amber)/20 px-2 py-0.5 font-black text-(--lp-amber)">
                          Giảm 20% trọn đời
                        </span>
                      </div>
                    )}

                    <p className={`mt-2.5 text-xs font-bold ${f ? 'text-(--lp-mist)' : 'text-(--lp-pine)'}`}>
                      {p.meta} · Trọn gói đồ nghề & bảo hiểm
                    </p>
                  </div>

                  {/* Feature Checklist with Hover Glow */}
                  <ul className={`mt-6 space-y-3 text-sm ${f ? 'text-white/90' : 'text-(--lp-ink)'}`}>
                    {p.features.map((t) => (
                      <li key={t} className="flex items-start gap-2.5 group/feat">
                        <CheckCircle2 className={`h-4 w-4 shrink-0 mt-0.5 transition-transform duration-200 group-hover/feat:scale-125 ${f ? 'text-(--lp-mist)' : 'text-(--lp-pine)'}`} />
                        <span className="leading-snug">{t}</span>
                      </li>
                    ))}
                  </ul>
                </div>

                <a
                  href="#tinh-gia"
                  className={`lp-btn mt-8 w-full text-center shadow-md transition-all duration-300 group-hover:scale-[1.02] cursor-pointer ${
                    f ? 'lp-btn-light font-black tracking-wide' : 'lp-btn-primary font-bold'
                  }`}
                >
                  {p.cta}
                </a>
              </article>
            )
          })}
        </div>

        {/* Subscription Benefit Strip */}
        <div data-reveal className="lp-card mt-8 flex flex-col justify-between gap-6 border border-(--lp-line) bg-(--lp-sand) p-8 md:flex-row md:items-center md:p-10 shadow-xs">
          <div className="flex items-center gap-5">
            <span className="grid h-12 w-12 place-items-center rounded-xl bg-(--lp-forest) text-(--lp-mist) shadow-sm">
              <CalendarDays className="h-6 w-6" />
            </span>
            <div>
              <h4 className="text-xl font-bold text-(--lp-forest)">Thuê bao định kỳ hàng tuần</h4>
              <p className="text-sm text-(--lp-ink-soft)">
                Cố định nhân sự thân quen, giảm 20% trọn đời và ưu tiên khung giờ vàng cuối tuần.
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={() => handleToggle('monthly')}
            className="lp-btn lp-btn-primary shrink-0 shadow-md hover:scale-105 active:scale-95 transition-all duration-200 cursor-pointer"
          >
            {billing === 'monthly' ? 'Đang áp dụng gói định kỳ' : 'Xem ưu đãi định kỳ'}
          </button>
        </div>
      </div>
    </section>
  )
}
