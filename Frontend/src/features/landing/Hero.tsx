import { useEffect, useRef } from 'react'
import { ArrowRight } from 'lucide-react'
import { IMAGES } from './data'
import { gsap, prefersReducedMotion } from './motion'

export function Hero() {
  const root = useRef<HTMLElement>(null)

  useEffect(() => {
    if (prefersReducedMotion()) return
    const ctx = gsap.context(() => {
      gsap.from('[data-hero-in]', { opacity: 0, y: 32, duration: 1, stagger: 0.12, ease: 'power3.out' })
      gsap.fromTo('[data-hero-bg]', { scale: 1.12 }, { scale: 1, duration: 2.4, ease: 'power2.out' })
    }, root)
    return () => ctx.revert()
  }, [])

  return (
    <section ref={root} id="top" className="relative isolate flex min-h-svh items-center overflow-hidden bg-(--lp-forest) text-white">
      <div className="absolute inset-0 -z-10" aria-hidden="true">
        <img
          data-hero-bg
          src={IMAGES.hero}
          alt=""
          fetchPriority="high"
          className="h-full w-full object-cover object-center"
        />
        <div className="absolute inset-0 bg-linear-to-r from-(--lp-forest)/90 via-(--lp-forest)/70 to-(--lp-forest)/30 md:to-(--lp-forest)/10" />
        <div className="absolute inset-x-0 bottom-0 h-44 bg-linear-to-t from-(--lp-forest)/70 to-transparent" />
      </div>

      <div className="mx-auto w-full max-w-7xl px-6 pb-28 pt-36 lg:px-12">
        <h1
          data-hero-in
          className="max-w-3xl text-balance text-[clamp(2.6rem,5vw,5rem)] font-bold leading-[1.04] tracking-[-0.035em]"
        >
          Dọn nhà theo giờ, đúng hẹn, <span className="text-(--lp-mist)">giá cố định.</span>
        </h1>
        <p data-hero-in className="mt-7 max-w-xl text-lg leading-relaxed text-white/85 md:text-xl">
          Chuyên viên mang đủ đồ nghề đến tận nhà. Nghiệm thu xong, hài lòng rồi bạn mới thanh toán.
        </p>
        <div data-hero-in className="mt-10 flex flex-wrap items-center gap-3">
          <a href="#tinh-gia" className="lp-btn lp-btn-light">
            Tính giá trong 10 giây <ArrowRight className="h-[18px] w-[18px]" />
          </a>
          <a href="#an-tam" className="lp-btn lp-btn-ghost">
            Xem quy trình an tâm
          </a>
        </div>
      </div>
    </section>
  )
}
