import { useEffect, useRef } from 'react'
import '@/features/landing/landing.css'
import { Bento } from '@/features/landing/Bento'
import { ControlSequence } from '@/features/landing/ControlSequence'
import { CtaBanner, LandingFooter } from '@/features/landing/CtaFooter'
import { Hero } from '@/features/landing/Hero'
import { LandingNav } from '@/features/landing/LandingNav'
import { PriceCalculator } from '@/features/landing/PriceCalculator'
import { PricingPlans } from '@/features/landing/PricingPlans'
import { RoomSlider } from '@/features/landing/RoomSlider'
import { Testimonials } from '@/features/landing/Testimonials'
import { gsap, ScrollTrigger, prefersReducedMotion } from '@/features/landing/motion'

// Public landing page at `/` (ticket #569). Visual reference: Frontend/ui-template/plandingPageWeb.html.
export function LandingPage() {
  const root = useRef<HTMLDivElement>(null)

  useEffect(() => {
    // Smooth anchor scrolling only while this page is mounted, so the admin shells keep the default behaviour.
    const html = document.documentElement
    const previous = html.style.scrollBehavior
    const previousTitle = document.title
    document.title = 'TỔ ẤM | Dọn nhà theo giờ, đúng hẹn, giá cố định'
    if (!prefersReducedMotion()) html.style.scrollBehavior = 'smooth'

    // Scroll reveals for every [data-reveal] block. Revert on unmount so no trigger survives navigation to /login.
    const ctx = gsap.context(() => {
      if (prefersReducedMotion()) return
      gsap.utils.toArray<HTMLElement>('[data-reveal]').forEach((el) => {
        gsap.from(el, {
          opacity: 0,
          y: 40,
          duration: 0.9,
          ease: 'power3.out',
          scrollTrigger: { trigger: el, start: 'top 90%', once: true },
        })
      })
      ScrollTrigger.refresh()
    }, root)

    return () => {
      ctx.revert()
      html.style.scrollBehavior = previous
      document.title = previousTitle
    }
  }, [])

  return (
    <div ref={root} className="landing min-h-screen">
      <LandingNav />
      <main>
        <Hero />
        <PriceCalculator />
        <Bento />
        <RoomSlider />
        <ControlSequence />
        <PricingPlans />
        <Testimonials />
        <CtaBanner />
      </main>
      <LandingFooter />
    </div>
  )
}
