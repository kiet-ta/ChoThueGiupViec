import { useState } from 'react'
import { ArrowRight, Building2, BedDouble, House, User, Users } from 'lucide-react'
import { AREA_MAX_M2, AREA_MIN_M2, estimatePrice, type PropertyType } from './pricing'

const TYPES: { value: PropertyType; label: string; Icon: typeof House }[] = [
  { value: 'apartment', label: 'Căn hộ', Icon: Building2 },
  { value: 'studio', label: 'Studio / Mini', Icon: BedDouble },
  { value: 'townhouse', label: 'Nhà phố / Biệt thự', Icon: House },
]

export function PriceCalculator() {
  const [type, setType] = useState<PropertyType>('apartment')
  const [area, setArea] = useState(65)
  const { priceVnd, workers } = estimatePrice(type, area)
  const pct = ((area - AREA_MIN_M2) / (AREA_MAX_M2 - AREA_MIN_M2)) * 100

  return (
    <section id="tinh-gia" className="px-6 pb-24 pt-24 md:pb-32 md:pt-32 lg:px-12">
      <div className="mx-auto max-w-7xl">
        <div data-reveal className="max-w-3xl">
          <h2 className="text-[clamp(2.2rem,4vw,3.75rem)] font-bold leading-[1.05] tracking-[-0.03em] text-(--lp-forest)">
            Kéo một thanh trượt, biết ngay giá trọn gói.
          </h2>
          <p className="mt-5 max-w-xl text-lg text-(--lp-ink-soft)">
            Hệ thống tự chia nhân sự theo diện tích để ca dọn kết thúc đúng thời lượng.
          </p>
        </div>

        <div data-reveal className="mt-14 grid gap-5 lg:grid-cols-12">
          <div className="lp-card border border-(--lp-line) bg-white p-8 shadow-sm md:p-12 lg:col-span-7">
            <p id="calc-type-label" className="text-sm font-medium text-(--lp-ink-soft)">Loại không gian</p>
            <div className="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-3" role="radiogroup" aria-labelledby="calc-type-label">
              {TYPES.map(({ value, label, Icon }) => {
                const on = value === type
                return (
                  <button
                    key={value}
                    type="button"
                    role="radio"
                    aria-checked={on}
                    onClick={() => setType(value)}
                    className={`rounded-lg border px-4 py-4 text-left transition ${
                      on
                        ? 'border-(--lp-forest) bg-(--lp-forest) text-white'
                        : 'border-(--lp-line) bg-white text-(--lp-forest) hover:bg-(--lp-sand)'
                    }`}
                  >
                    <Icon className="mb-2 h-5 w-5" />
                    <span className="font-semibold">{label}</span>
                  </button>
                )
              })}
            </div>

            <div className="mt-10">
              <div className="flex items-baseline justify-between">
                <label htmlFor="calc-area" className="text-sm font-medium text-(--lp-ink-soft)">Diện tích sàn</label>
                <span className="lp-num text-3xl font-semibold text-(--lp-forest)">{area} m²</span>
              </div>
              <input
                id="calc-area"
                type="range"
                min={AREA_MIN_M2}
                max={AREA_MAX_M2}
                value={area}
                onChange={(e) => setArea(Number(e.target.value))}
                className="lp-range mt-3"
                style={{ ['--lp-track' as string]: `linear-gradient(to right, #416448 ${pct}%, #e2e3de ${pct}%)` }}
              />
              <div className="lp-num flex justify-between text-xs text-(--lp-ink-soft)">
                <span>{AREA_MIN_M2} m²</span>
                <span>{AREA_MAX_M2} m²</span>
              </div>
            </div>
          </div>

          <div className="lp-card relative isolate flex min-h-[24rem] flex-col justify-between overflow-hidden bg-(--lp-forest) p-8 text-white md:p-12 lg:col-span-5">
            <div className="pointer-events-none absolute -right-20 -top-20 -z-10 h-72 w-72 rounded-full bg-(--lp-sage)/50 blur-3xl" />
            <div>
              <p className="text-sm text-(--lp-mist)/80">Trọn gói ước tính</p>
              <p className="lp-num mt-2 text-[clamp(2.6rem,5vw,4.25rem)] font-semibold leading-none" aria-live="polite">
                {priceVnd.toLocaleString('vi-VN')}
                <span className="text-2xl font-normal text-(--lp-mist)/70"> đ</span>
              </p>
            </div>
            <div className="mt-8 flex items-start gap-3 rounded-lg border border-white/10 bg-white/10 p-4">
              {workers === 1 ? (
                <User className="mt-0.5 h-5 w-5 shrink-0 text-(--lp-mist)" />
              ) : (
                <Users className="mt-0.5 h-5 w-5 shrink-0 text-(--lp-mist)" />
              )}
              <div>
                <p className="font-semibold">
                  {workers === 1 ? '1 chuyên viên lành nghề' : '2 chuyên viên làm đồng thời'}
                </p>
                <p className="mt-1 text-sm text-white/70">
                  {workers === 1
                    ? 'Đạt thời lượng tối ưu cho diện tích 80 m² trở xuống.'
                    : 'Tổ đội 2 người được kích hoạt cho nhà trên 80 m² hoặc nhà phố, rút ngắn thời gian hoàn thành.'}
                </p>
              </div>
            </div>
            <a href="#bang-gia" className="lp-btn lp-btn-light mt-6">
              Đặt lịch với mức giá này <ArrowRight className="h-[18px] w-[18px]" />
            </a>
          </div>
        </div>
      </div>
    </section>
  )
}
