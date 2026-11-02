import { Bed, Building2, CalendarDays, CircleCheck } from 'lucide-react'
import { PLANS } from './data'

const PLAN_ICON = { studio: Bed, apartment: Building2, townhouse: Building2 } as const

export function PricingPlans() {
  return (
    <section id="bang-gia" className="px-6 py-24 md:py-32 lg:px-12">
      <div className="mx-auto max-w-7xl">
        <h2 data-reveal className="max-w-5xl text-[clamp(2.2rem,4vw,3.75rem)] font-bold leading-[1.05] tracking-[-0.03em] text-(--lp-forest)">
          Giá minh bạch cho từng loại không gian.
        </h2>

        <div className="mt-16 grid grid-cols-1 items-stretch gap-5 lg:grid-cols-3">
          {PLANS.map((p) => {
            const Icon = PLAN_ICON[p.key]
            const f = p.featured
            return (
              <article
                key={p.key}
                data-reveal
                className={`lp-card flex flex-col justify-between p-8 md:p-10 ${
                  f
                    ? 'bg-(--lp-forest) text-white shadow-[0_40px_70px_-30px_rgba(26,47,37,0.7)] lg:-translate-y-4'
                    : 'border border-(--lp-line) bg-white transition-shadow hover:shadow-xl'
                }`}
              >
                <div>
                  <div className="flex items-center justify-between text-sm">
                    <span className={`lp-num ${f ? 'text-(--lp-mist)/80' : 'text-(--lp-ink-soft)'}`}>{p.range}</span>
                    {f ? (
                      <span className="rounded-md bg-(--lp-mist) px-3 py-1 text-xs font-semibold text-(--lp-forest)">Phổ biến nhất</span>
                    ) : (
                      <Icon className="h-4 w-4 text-(--lp-ink-soft)" />
                    )}
                  </div>
                  <h3 className={`mt-8 text-3xl font-bold tracking-tight ${f ? '' : 'text-(--lp-forest)'}`}>{p.name}</h3>
                  <p className={`mt-2 ${f ? 'text-white/70' : 'text-(--lp-ink-soft)'}`}>{p.blurb}</p>
                  <p className={`lp-num mt-8 text-5xl font-semibold ${f ? 'text-white' : 'text-(--lp-forest)'}`}>
                    {p.price}
                    <span className={`text-base font-normal ${f ? 'text-white/60' : 'text-(--lp-ink-soft)'}`}> đ / buổi</span>
                  </p>
                  <p className={`mt-1 text-sm font-medium ${f ? 'text-(--lp-mist)/90' : p.key === 'townhouse' ? 'text-(--lp-amber)' : 'text-(--lp-sage)'}`}>{p.meta}</p>
                  <ul className={`mt-8 space-y-3 text-sm ${f ? 'text-white/90' : ''}`}>
                    {p.features.map((t) => (
                      <li key={t} className="flex gap-2.5">
                        <CircleCheck className={`h-[18px] w-[18px] shrink-0 ${f ? 'text-(--lp-mist)' : 'text-(--lp-sage)'}`} />
                        {t}
                      </li>
                    ))}
                  </ul>
                </div>
                <a href="#tinh-gia" className={`lp-btn mt-10 ${f ? 'lp-btn-light' : 'lp-btn-outline'}`}>
                  {p.cta}
                </a>
              </article>
            )
          })}
        </div>

        <div data-reveal className="lp-card mt-8 flex flex-col justify-between gap-6 border border-(--lp-line) bg-(--lp-sand) p-8 md:flex-row md:items-center md:p-10">
          <div className="flex items-center gap-5">
            <span className="grid h-12 w-12 place-items-center rounded-lg bg-(--lp-amber)/20 text-(--lp-amber)">
              <CalendarDays className="h-6 w-6" />
            </span>
            <div>
              <h4 className="text-xl font-bold text-(--lp-forest)">Gói thuê bao định kỳ</h4>
              <p className="text-(--lp-ink-soft)">Tiết kiệm 20% chi phí, cố định một chuyên viên quen thuộc.</p>
            </div>
          </div>
          <a href="#tinh-gia" className="lp-btn lp-btn-primary shrink-0">Xem ưu đãi</a>
        </div>
      </div>
    </section>
  )
}
