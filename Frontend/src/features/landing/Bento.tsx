import { useEffect, useRef, useState, type MouseEvent } from 'react'
import { CheckCircle, ReceiptText, ShieldCheck, Sparkles, Users } from 'lucide-react'
import { IMAGES } from './data'
import { gsap, prefersReducedMotion } from './motion'

// 5 columns x 2 rows = 10 cells: A 3x2 = 6, B 2x1 = 2, C 1, D 1. `grid-flow-dense` + exact spans leave no gap.
export function Bento() {
  const containerRef = useRef<HTMLDivElement>(null)
  const [householdCount, setHouseholdCount] = useState(12000)
  const [insuranceAmount, setInsuranceAmount] = useState(30)

  useEffect(() => {
    const el = containerRef.current
    if (!el || prefersReducedMotion()) return

    const ctx = gsap.context(() => {
      // Heading punchy reveal
      gsap.from('.bento-header', {
        y: 40,
        opacity: 0,
        duration: 0.8,
        ease: 'power3.out',
        scrollTrigger: { trigger: el, start: 'top 85%' },
      })

      // Dramatic Staggered Scale-In for the 4 Bento Cards
      gsap.from('.bento-card', {
        y: 60,
        scale: 0.92,
        opacity: 0,
        duration: 0.75,
        stagger: 0.1,
        ease: 'back.out(1.3)',
        scrollTrigger: { trigger: '.bento-grid', start: 'top 82%' },
      })

      // Live Numeric Count Up for Insurance & Households
      const statsObj = { insurance: 0, households: 10000 }
      gsap.to(statsObj, {
        insurance: 50,
        households: 14500,
        duration: 1.6,
        ease: 'power2.out',
        scrollTrigger: { trigger: '.bento-grid', start: 'top 80%', once: true },
        onUpdate: () => {
          setInsuranceAmount(Math.round(statsObj.insurance))
          setHouseholdCount(Math.round(statsObj.households))
        },
      })
    }, el)

    return () => ctx.revert()
  }, [])

  // 3D Magnetic Tilt and Cursor Spotlight Handler
  const handleMouseMove = (e: MouseEvent<HTMLElement>) => {
    if (prefersReducedMotion()) return
    const card = e.currentTarget
    const rect = card.getBoundingClientRect()
    const x = e.clientX - rect.left
    const y = e.clientY - rect.top
    const centerX = rect.width / 2
    const centerY = rect.height / 2
    const rotateX = ((y - centerY) / centerY) * -7
    const rotateY = ((x - centerX) / centerX) * 7

    gsap.to(card, {
      rotateX,
      rotateY,
      transformPerspective: 900,
      duration: 0.25,
      ease: 'power1.out',
    })

    const spotlight = card.querySelector<HTMLElement>('.card-spotlight')
    if (spotlight) {
      spotlight.style.opacity = '1'
      spotlight.style.background = `radial-gradient(380px circle at ${x}px ${y}px, rgba(197, 236, 201, 0.22), transparent 70%)`
    }
  }

  const handleMouseLeave = (e: MouseEvent<HTMLElement>) => {
    if (prefersReducedMotion()) return
    const card = e.currentTarget
    gsap.to(card, {
      rotateX: 0,
      rotateY: 0,
      duration: 0.45,
      ease: 'power2.out',
    })

    const spotlight = card.querySelector<HTMLElement>('.card-spotlight')
    if (spotlight) {
      spotlight.style.opacity = '0'
    }
  }

  return (
    <section ref={containerRef} className="px-6 pb-24 md:pb-32 lg:px-12 relative overflow-hidden">
      <div className="mx-auto max-w-7xl">
        <div className="bento-header max-w-4xl">
          <div className="inline-flex items-center gap-2 rounded-full border border-(--lp-line) bg-white px-3 py-1 text-xs font-semibold text-(--lp-pine) shadow-xs">
            <Sparkles className="h-3.5 w-3.5" />
            <span>Tiêu chuẩn dịch vụ Nordic Care</span>
          </div>
          <h2 className="mt-3 text-[clamp(2rem,3.6vw,3.25rem)] font-bold leading-[1.05] tracking-[-0.03em] text-(--lp-forest)">
            Mang theo mọi thứ. Chủ nhà không phải lo gì.
          </h2>
          <p className="mt-2 text-base text-(--lp-ink-soft) md:text-lg">
            Tổ đội chuyên nghiệp được đào tạo bài bản với bộ trang thiết bị tiêu chuẩn khử khuẩn cao cấp.
          </p>
        </div>

        <div className="bento-grid mt-10 grid auto-rows-[11rem] grid-flow-dense grid-cols-1 gap-5 md:grid-cols-2 lg:grid-cols-5">
          {/* Card A: Big Hero Media (3 cols x 2 rows = 6 cells) */}
          <article
            onMouseMove={handleMouseMove}
            onMouseLeave={handleMouseLeave}
            className="bento-card lp-card group relative row-span-2 overflow-hidden bg-(--lp-forest) border border-white/10 md:col-span-2 lg:col-span-3 shadow-xl transition-shadow duration-300 hover:shadow-2xl will-change-transform transform-gpu"
          >
            {/* Interactive Spotlight layer */}
            <div className="card-spotlight pointer-events-none absolute inset-0 z-20 opacity-0 transition-opacity duration-300" />

            <img
              src={IMAGES.bento}
              alt="Máy hút bụi HEPA làm sạch thảm"
              decoding="async"
              className="lp-bento-media absolute inset-0 h-full w-full object-cover opacity-90 transition-transform duration-[850ms] ease-out group-hover:scale-105 filter grayscale-[70%] contrast-[112%] brightness-[0.9]"
            />
            <div className="absolute inset-0 bg-linear-to-t from-(--lp-forest) via-(--lp-forest)/40 to-transparent" />
            <div className="absolute bottom-0 p-6 text-white md:p-8 relative z-10">
              <span className="inline-flex items-center gap-1.5 rounded-full bg-white/15 px-3 py-1 text-xs font-medium text-white backdrop-blur-md mb-3 border border-white/15 shadow-xs">
                <CheckCircle className="h-3.5 w-3.5 text-(--lp-mist)" /> Tiêu chuẩn ECOCERT
              </span>
              <h3 className="text-2xl font-bold tracking-tight md:text-3xl text-white">
                Đồ nghề chuẩn, tự mang theo.
              </h3>
              <p className="mt-2 max-w-md text-sm text-white/85 md:text-base leading-relaxed">
                Máy hút bụi HEPA lọc 99.9% bụi mịn và bộ khăn 4 màu tách riêng bếp, toilet, bề mặt và kính. Dung dịch thảo mộc an toàn cho trẻ nhỏ.
              </p>
            </div>
          </article>

          {/* Card B: Insurance Proof (2 cols x 1 row = 2 cells) */}
          <article
            onMouseMove={handleMouseMove}
            onMouseLeave={handleMouseLeave}
            className="bento-card lp-card group relative flex items-center gap-5 border border-(--lp-pine)/20 bg-(--lp-mist) p-6 md:col-span-2 shadow-sm transition-shadow duration-300 hover:shadow-xl will-change-transform transform-gpu overflow-hidden"
          >
            <div className="card-spotlight pointer-events-none absolute inset-0 z-20 opacity-0 transition-opacity duration-300" />
            <span className="grid h-14 w-14 shrink-0 place-items-center rounded-xl bg-(--lp-forest)/10 text-(--lp-pine) transition-transform duration-300 group-hover:scale-110 shadow-xs">
              <ShieldCheck className="h-7 w-7" />
            </span>
            <div className="relative z-10">
              <p className="lp-num text-3xl font-black leading-none text-(--lp-forest) md:text-4xl tracking-tight">
                {insuranceAmount} triệu
              </p>
              <p className="mt-2 text-sm font-bold text-(--lp-pine)">Hạn mức bảo hiểm tài sản cho mỗi ca dọn.</p>
              <p className="mt-1 text-xs text-(--lp-pine)/80">Xử lý minh bạch qua hệ thống ký quỹ.</p>
            </div>
          </article>

          {/* Card C: Fixed Price (1 cell) */}
          <article
            onMouseMove={handleMouseMove}
            onMouseLeave={handleMouseLeave}
            className="bento-card lp-card group relative flex flex-col justify-between border border-(--lp-line) bg-white p-5 shadow-xs transition-shadow duration-300 hover:border-(--lp-forest)/40 hover:shadow-lg will-change-transform transform-gpu overflow-hidden"
          >
            <div className="card-spotlight pointer-events-none absolute inset-0 z-20 opacity-0 transition-opacity duration-300" />
            <span className="grid h-10 w-10 place-items-center rounded-lg bg-(--lp-sand) text-(--lp-sage) transition-transform duration-300 group-hover:scale-110">
              <ReceiptText className="h-5 w-5" />
            </span>
            <div className="relative z-10">
              <p className="text-xl font-bold tracking-tight text-(--lp-forest)">Giá cố định</p>
              <p className="mt-1 text-xs text-(--lp-ink-soft) leading-relaxed">
                Niêm yết minh bạch, không phụ phí ngày lễ, không tip bắt buộc.
              </p>
            </div>
          </article>

          {/* Card D: Household Count (1 cell) */}
          <article
            onMouseMove={handleMouseMove}
            onMouseLeave={handleMouseLeave}
            className="bento-card lp-card group relative flex flex-col justify-between border border-(--lp-line) bg-(--lp-sand) p-5 shadow-xs transition-shadow duration-300 hover:border-(--lp-forest)/40 hover:shadow-lg will-change-transform transform-gpu overflow-hidden"
          >
            <div className="card-spotlight pointer-events-none absolute inset-0 z-20 opacity-0 transition-opacity duration-300" />
            <span className="grid h-10 w-10 place-items-center rounded-lg bg-white text-(--lp-amber) transition-transform duration-300 group-hover:scale-110 shadow-xs">
              <Users className="h-5 w-5" />
            </span>
            <div className="relative z-10">
              <p className="lp-num text-2xl font-black leading-none text-(--lp-forest)">
                {householdCount.toLocaleString('vi-VN')}+
              </p>
              <p className="mt-1.5 text-xs text-(--lp-ink-soft) leading-relaxed">
                Hộ gia đình đã phục vụ định kỳ tại TP.HCM.
              </p>
            </div>
          </article>
        </div>
      </div>
    </section>
  )
}
