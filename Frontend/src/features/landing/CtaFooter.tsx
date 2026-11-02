import { ArrowRight } from 'lucide-react'
import { FOOTER_LINKS, IMAGES } from './data'

/** Banner with the photo behind the headline only. The footer is a separate, plain block below it. */
export function CtaBanner() {
  return (
    <section className="relative isolate overflow-hidden px-6 py-28 text-white md:py-40 lg:px-12">
      <div className="absolute inset-0 -z-10" aria-hidden="true">
        <img src={IMAGES.cta} alt="" loading="lazy" className="h-full w-full object-cover" />
        <div className="absolute inset-0 bg-(--lp-forest)/70" />
        <div className="absolute inset-0" style={{ background: 'radial-gradient(ellipse 65% 75% at 50% 45%, rgba(26,47,37,.5), rgba(26,47,37,0) 100%)' }} />
      </div>
      <div data-reveal className="mx-auto max-w-7xl text-center">
        <h2 className="mx-auto max-w-5xl text-[clamp(2.8rem,6.5vw,6rem)] font-bold leading-[1.02] tracking-[-0.04em]">
          Để cuối tuần thật sự là <span className="text-(--lp-mist)">ngày nghỉ.</span>
        </h2>
        <p className="mt-7 text-lg text-white/85 md:text-xl">Đặt lịch trong 60 giây. Chuyên viên đầy đủ đồ nghề sẽ có mặt đúng giờ.</p>
        <div className="mt-10 flex flex-col items-center justify-center gap-3 sm:flex-row">
          <a href="#tinh-gia" className="lp-btn lp-btn-light !px-8">
            Đặt lịch ngay <ArrowRight className="h-[18px] w-[18px]" />
          </a>
          <a href="tel:19008899" className="lp-btn lp-btn-ghost !px-8">
            Hotline 1900 8899
          </a>
        </div>
      </div>
    </section>
  )
}

export function LandingFooter() {
  return (
    <footer className="bg-(--lp-footer) px-6 py-14 text-white lg:px-12">
      <div className="mx-auto flex max-w-7xl flex-col justify-between gap-8 md:flex-row">
        <div>
          <p className="text-xl font-bold">TỔ ẤM</p>
          <p className="mt-2 text-sm text-white/60">© 2025 TỔ ẤM Home Care. Yên tâm chuẩn Bắc Âu.</p>
          <p className="mt-1 text-sm text-white/60">
            Hotline: <a className="hover:text-white" href="tel:19008899">1900 8899</a>
          </p>
        </div>
        <nav className="flex flex-wrap gap-x-6 gap-y-2 text-sm text-white/70 md:max-w-xl md:justify-end" aria-label="Chính sách">
          {FOOTER_LINKS.map((t) => (
            <a key={t} className="transition hover:text-white" href="#">{t}</a>
          ))}
        </nav>
      </div>
    </footer>
  )
}
