import { useEffect, useRef } from 'react'
import { ArrowRight, Sparkles } from 'lucide-react'
import { IMAGES } from './data'
import { gsap, prefersReducedMotion } from './motion'

export function Hero() {
  const root = useRef<HTMLElement>(null)
  const videoRef = useRef<HTMLVideoElement>(null)

  useEffect(() => {
    if (prefersReducedMotion()) {
      if (videoRef.current) videoRef.current.pause()
      return
    }
    const ctx = gsap.context(() => {
      gsap.from('[data-hero-in]', {
        opacity: 0,
        y: 28,
        duration: 0.9,
        stagger: 0.1,
        ease: 'power3.out',
      })
      gsap.fromTo('[data-hero-bg]', { scale: 1.12 }, { scale: 1, duration: 2.2, ease: 'power2.out' })
    }, root)
    return () => ctx.revert()
  }, [])

  return (
    <section ref={root} id="top" className="relative isolate flex min-h-svh items-center overflow-hidden bg-(--lp-forest) text-white">
      {/* Full-Bleed Immersive Photographic & Cinematic Video Canvas */}
      <div className="absolute inset-0 -z-10 overflow-hidden bg-(--lp-forest)" aria-hidden="true">
        <video
          ref={videoRef}
          data-hero-bg
          autoPlay
          loop
          muted
          playsInline
          poster={IMAGES.hero}
          className="h-full w-full object-cover object-center md:object-right filter brightness-[1.05] contrast-[102%]"
        >
          <source src="/videos/hero-family.mp4" type="video/mp4" />
        </video>
        {/* Lớp phủ chuyển sắc thông minh: trên Mobile phủ đều dịu mắt đảm bảo đọc chữ; trên Desktop viền xanh từ trái lan sang làm nổi khối chữ */}
        <div className="absolute inset-0 bg-linear-to-b from-(--lp-forest)/90 via-(--lp-forest)/65 to-(--lp-forest)/80 md:bg-linear-to-r md:from-(--lp-forest) md:from-10% md:via-(--lp-forest)/85 md:via-30% md:via-(--lp-forest)/25 md:via-46% md:to-transparent md:to-58%" />
        {/* Gradient chân trang giữ độ sắc nét cho thanh stats */}
        <div className="absolute inset-x-0 bottom-0 h-32 bg-linear-to-t from-(--lp-forest)/90 via-(--lp-forest)/30 to-transparent" />
      </div>

      <div className="mx-auto w-full max-w-7xl px-6 pb-20 pt-36 lg:px-12">
        {/* Live Status Pill Badge */}
        <div data-hero-in className="mb-6 inline-flex items-center gap-2 rounded-full border border-white/30 bg-black/40 px-3.5 py-1.5 text-xs font-semibold text-white backdrop-blur-md shadow-sm">
          <span className="relative flex h-2 w-2">
            <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-(--lp-mist) opacity-75" />
            <span className="relative inline-flex h-2 w-2 rounded-full bg-(--lp-mist)" />
          </span>
          <span>Đang nhận ca hôm nay • 100% chuyên viên sẵn sàng</span>
        </div>

        <h1
          data-hero-in
          className="lp-hero-title max-w-4xl text-balance text-[clamp(2.6rem,5vw,5rem)] font-extrabold leading-[1.04] tracking-[-0.035em] text-white"
        >
          Mang <span className="text-[#FFD749]">BÃ</span> đi xa, <span className="text-(--lp-mist)">Cho nhà êm ấm</span>
        </h1>
        <p data-hero-in className="lp-hero-desc mt-6 max-w-2xl text-lg leading-relaxed text-white/95 md:text-xl font-medium">
          Chuyên viên trang bị trọn bộ dụng cụ chuyên dụng, phục vụ tận nơi. Khách hàng trực tiếp nghiệm thu từng khu vực; trao an tâm và hài lòng đến khách hàng.
        </p>
        
        <div data-hero-in className="mt-8 flex flex-wrap items-center gap-3">
          <a href="#lich-ca" className="lp-btn lp-btn-light group shadow-md">
            Xem lịch ca & Đặt ngay <ArrowRight className="h-[18px] w-[18px] transition-transform duration-200 group-hover:translate-x-0.5" />
          </a>
          <a href="#tinh-gia" className="lp-btn lp-btn-ghost backdrop-blur-md bg-white/10 hover:bg-white/20 shadow-sm">
            <Sparkles className="h-4 w-4 text-(--lp-mist)" /> Tính giá trong 10 giây
          </a>
        </div>

        {/* Quick Stats Bar */}
        <div data-hero-in className="mt-14 grid grid-cols-2 gap-4 border-t border-white/20 pt-8 sm:grid-cols-4 lg:max-w-4xl">
          <div className="flex flex-col">
            <span className="lp-num text-2xl font-bold tracking-tight text-white md:text-3xl drop-shadow-sm">15 phút</span>
            <span className="mt-1 text-xs text-white/85 md:text-sm font-medium">Ghép chuyên viên nhanh nhất</span>
          </div>
          <div className="flex flex-col">
            <span className="lp-num text-2xl font-bold tracking-tight text-(--lp-mist) md:text-3xl drop-shadow-sm">4.9 ★</span>
            <span className="mt-1 text-xs text-white/85 md:text-sm font-medium">Hài lòng từ 10.000+ ca</span>
          </div>
          <div className="flex flex-col">
            <span className="lp-num text-2xl font-bold tracking-tight text-white md:text-3xl drop-shadow-sm">100%</span>
            <span className="mt-1 text-xs text-white/85 md:text-sm font-medium">Xác minh CCCD chip & GPS</span>
          </div>
          <div className="flex flex-col">
            <span className="lp-num text-2xl font-bold tracking-tight text-(--lp-amber) md:text-3xl drop-shadow-sm">0 đ</span>
            <span className="mt-1 text-xs text-white/85 md:text-sm font-medium">Phí huỷ trước 2 giờ</span>
          </div>
        </div>
      </div>
    </section>
  )
}
