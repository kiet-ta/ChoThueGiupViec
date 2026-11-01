import { useState } from 'react'
import { ArrowLeft, ArrowRight } from 'lucide-react'
import { PLACEHOLDER_PROOF } from './data'

const TONES = ['bg-(--lp-forest) text-(--lp-mist)', 'bg-(--lp-amber) text-(--lp-forest)', 'bg-(--lp-sage) text-white']

export function Testimonials() {
  const items = PLACEHOLDER_PROOF.testimonials
  const [idx, setIdx] = useState(0)
  const go = (i: number) => setIdx((i + items.length) % items.length)

  return (
    <section id="danh-gia" className="bg-(--lp-sand) px-6 py-24 md:py-32 lg:px-12">
      <div className="mx-auto max-w-6xl">
        <div data-reveal className="flex items-end justify-between gap-6">
          <h2 className="max-w-3xl text-[clamp(2.2rem,4vw,3.75rem)] font-bold leading-[1.05] tracking-[-0.03em] text-(--lp-forest)">
            Người dùng nói gì về tổ ấm của họ.
          </h2>
          <div className="hidden shrink-0 gap-2 md:flex">
            <button type="button" aria-label="Đánh giá trước" onClick={() => go(idx - 1)} className="lp-btn lp-btn-outline lp-btn-icon">
              <ArrowLeft className="h-5 w-5" />
            </button>
            <button type="button" aria-label="Đánh giá kế tiếp" onClick={() => go(idx + 1)} className="lp-btn lp-btn-primary lp-btn-icon">
              <ArrowRight className="h-5 w-5" />
            </button>
          </div>
        </div>

        <div className="mt-16 grid items-center gap-10 md:grid-cols-12">
          <div className="flex md:col-span-4 md:justify-center" aria-hidden="true">
            {items.map((t, i) => (
              <span
                key={t.initials}
                className={`lp-avatar grid h-24 w-24 place-items-center rounded-full border-4 border-(--lp-sand) text-2xl font-bold md:h-28 md:w-28 ${TONES[i % TONES.length]} ${i ? '-ml-8' : ''} ${i === idx ? 'is-on' : ''}`}
              >
                {t.initials}
              </span>
            ))}
          </div>
          <div className="md:col-span-8">
            <div className="grid" aria-live="polite">
              {items.map((t, i) => (
                <figure key={t.initials} className={`lp-quote ${i === idx ? 'is-on' : ''}`} aria-hidden={i !== idx}>
                  <blockquote className="text-[clamp(1.5rem,2.6vw,2.4rem)] font-medium leading-snug tracking-tight text-(--lp-forest)">
                    “{t.quote}”
                  </blockquote>
                  <figcaption className="mt-8 flex items-center gap-3">
                    <span className="font-semibold text-(--lp-forest)">{t.name}</span>
                    <span className="text-(--lp-ink-soft)">· {t.place}</span>
                  </figcaption>
                </figure>
              ))}
            </div>
            <div className="mt-8 flex gap-2 md:hidden">
              <button type="button" aria-label="Đánh giá trước" onClick={() => go(idx - 1)} className="lp-btn lp-btn-outline lp-btn-icon">
                <ArrowLeft className="h-5 w-5" />
              </button>
              <button type="button" aria-label="Đánh giá kế tiếp" onClick={() => go(idx + 1)} className="lp-btn lp-btn-primary lp-btn-icon">
                <ArrowRight className="h-5 w-5" />
              </button>
            </div>
          </div>
        </div>
        <p className="mt-14 text-xs text-(--lp-ink-soft)">
          Nội dung và số liệu trên trang này là dữ liệu mẫu để xem bố cục. Thay bằng số liệu và đánh giá thật trước khi ra mắt.
        </p>
      </div>
    </section>
  )
}
