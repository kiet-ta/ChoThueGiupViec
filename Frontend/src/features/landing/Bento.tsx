import { ReceiptText, ShieldCheck, Users } from 'lucide-react'
import { IMAGES, PLACEHOLDER_PROOF } from './data'

// 5 columns x 2 rows = 10 cells: A 3x2 = 6, B 2x1 = 2, C 1, D 1. `grid-flow-dense` + exact spans leave no gap.
export function Bento() {
  return (
    <section className="px-6 pb-24 md:pb-32 lg:px-12">
      <div className="mx-auto max-w-7xl">
        <h2 data-reveal className="max-w-4xl text-[clamp(2rem,3.6vw,3.25rem)] font-bold leading-[1.05] tracking-[-0.03em] text-(--lp-forest)">
          Mang theo mọi thứ. Chủ nhà không phải lo gì.
        </h2>

        <div className="mt-10 grid auto-rows-[9.5rem] grid-flow-dense grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-5">
          <article data-reveal className="lp-card group relative row-span-2 overflow-hidden bg-(--lp-forest) md:col-span-2 lg:col-span-3">
            <img
              src={IMAGES.bento}
              alt="Máy hút bụi HEPA làm sạch thảm"
              loading="lazy"
              className="absolute inset-0 h-full w-full object-cover opacity-80 mix-blend-luminosity transition-transform duration-[900ms] ease-out group-hover:scale-105"
            />
            <div className="absolute inset-0 bg-linear-to-t from-(--lp-forest)/90 via-(--lp-forest)/25 to-transparent" />
            <div className="absolute bottom-0 p-6 text-white md:p-8">
              <h3 className="text-2xl font-bold tracking-tight md:text-3xl">Đồ nghề chuẩn, tự mang theo.</h3>
              <p className="mt-2 max-w-md text-sm text-white/80 md:text-base">
                Máy hút bụi HEPA và bộ khăn 4 màu tách riêng bếp, toilet, bề mặt và kính. Dung dịch thảo mộc ECOCERT.
              </p>
            </div>
          </article>

          <article data-reveal className="lp-card flex items-center gap-5 bg-(--lp-mist) p-6 md:col-span-2">
            <span className="grid h-14 w-14 shrink-0 place-items-center rounded-lg bg-(--lp-forest)/10 text-(--lp-pine)">
              <ShieldCheck className="h-7 w-7" />
            </span>
            <div>
              <p className="lp-num text-3xl font-semibold leading-none text-(--lp-forest) md:text-4xl">50 triệu</p>
              <p className="mt-2 text-sm font-medium text-(--lp-pine)">Hạn mức bảo hiểm tài sản cho mỗi ca dọn.</p>
            </div>
          </article>

          <article data-reveal className="lp-card flex flex-col justify-between border border-(--lp-line) bg-white p-5">
            <ReceiptText className="h-6 w-6 text-(--lp-sage)" />
            <div>
              <p className="text-xl font-bold tracking-tight text-(--lp-forest)">Giá cố định</p>
              <p className="mt-0.5 text-sm text-(--lp-ink-soft)">Không phụ phí, không tiền tip.</p>
            </div>
          </article>

          <article data-reveal className="lp-card flex flex-col justify-between border border-(--lp-line) bg-(--lp-sand) p-5">
            <Users className="h-6 w-6 text-(--lp-amber)" />
            <div>
              <p className="lp-num text-2xl font-semibold leading-none text-(--lp-forest)">{PLACEHOLDER_PROOF.households}+</p>
              <p className="mt-1.5 text-sm text-(--lp-ink-soft)">Hộ gia đình đã phục vụ (số mẫu).</p>
            </div>
          </article>
        </div>
      </div>
    </section>
  )
}
