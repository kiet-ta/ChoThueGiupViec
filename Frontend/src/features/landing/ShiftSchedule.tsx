import { useState } from 'react'
import { ArrowRight, CheckCircle2, Clock, MapPin, Moon, Sparkles, Sun, SunMedium } from 'lucide-react'

interface ShiftOption {
  id: 'morning' | 'afternoon' | 'evening'
  code: string
  name: string
  time: string
  tag: string
  tagColor: string
  workersAvailable: number
  priceStart: string
  bestFor: string
  icon: typeof Sun
}

const SHIFTS: ShiftOption[] = [
  {
    id: 'morning',
    code: 'SHIFT_MORNING',
    name: 'Ca Sáng',
    time: '08:00 – 12:00',
    tag: 'Khung giờ vàng',
    tagColor: 'bg-(--lp-mist) text-(--lp-forest)',
    workersAvailable: 42,
    priceStart: '180.000',
    bestFor: 'Dọn sạch đón ngày mới, nhà bếp & phòng khách thoáng đãng',
    icon: Sun,
  },
  {
    id: 'afternoon',
    code: 'SHIFT_AFTERNOON',
    name: 'Ca Chiều',
    time: '13:00 – 17:00',
    tag: 'Yên tĩnh & Nhanh gọn',
    tagColor: 'bg-amber-100 text-amber-900',
    workersAvailable: 36,
    priceStart: '180.000',
    bestFor: 'Giặt ủi, lau kính, căn hộ nghỉ trưa, chuẩn bị đón trẻ về',
    icon: SunMedium,
  },
  {
    id: 'evening',
    code: 'SHIFT_EVENING',
    name: 'Ca Tối',
    time: '18:00 – 22:00',
    tag: 'Tiện lợi sau giờ làm',
    tagColor: 'bg-(--lp-forest)/10 text-(--lp-forest)',
    workersAvailable: 28,
    priceStart: '200.000',
    bestFor: 'Người bận rộn cả ngày, dọn dẹp bát đĩa & phòng ngủ sạch sẽ',
    icon: Moon,
  },
]

const DAYS = [
  { id: 'today', label: 'Hôm nay', note: 'Ca gần nhất 13:00' },
  { id: 'tomorrow', label: 'Ngày mai', note: 'Đầy đủ 3 ca' },
  { id: 'weekend', label: 'Thứ Bảy & CN', note: 'Đặt sớm giữ chỗ' },
]

export function ShiftSchedule() {
  const [activeDay, setActiveDay] = useState('today')
  const [selectedShift, setSelectedShift] = useState<'morning' | 'afternoon' | 'evening'>('morning')

  const current = SHIFTS.find((s) => s.id === selectedShift) ?? SHIFTS[0]

  return (
    <section id="lich-ca" className="px-6 py-24 md:py-32 lg:px-12 bg-(--lp-sand)/60">
      <div className="mx-auto max-w-7xl">
        <div data-reveal className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
          <div>
            <div className="inline-flex items-center gap-2 rounded-full border border-(--lp-line) bg-white/80 px-3 py-1 text-xs font-semibold text-(--lp-pine) backdrop-blur-sm">
              <Clock className="h-3.5 w-3.5" />
              <span>Lịch nhận ca linh hoạt</span>
            </div>
            <h2 className="mt-3 text-[clamp(2rem,3.4vw,3rem)] font-bold leading-[1.08] tracking-[-0.03em] text-(--lp-forest)">
              Chọn ca dọn phù hợp với lịch của bạn.
            </h2>
            <p className="mt-2 text-base text-(--lp-ink-soft) md:text-lg">
              3 khung giờ chuẩn mỗi ngày. Chuyên viên có mặt đúng hẹn trong phạm vi 100 mét.
            </p>
          </div>

          {/* Day selection tabs */}
          <div className="flex rounded-xl border border-(--lp-line) bg-white p-1.5 shadow-xs">
            {DAYS.map((d) => (
              <button
                key={d.id}
                type="button"
                onClick={() => setActiveDay(d.id)}
                className={`flex flex-col items-center rounded-lg px-4 py-2 text-xs font-semibold transition-all duration-200 ${
                  activeDay === d.id
                    ? 'bg-(--lp-forest) text-white shadow-xs'
                    : 'text-(--lp-ink-soft) hover:text-(--lp-forest) hover:bg-black/5'
                }`}
              >
                <span>{d.label}</span>
                <span className={`text-[10px] font-normal ${activeDay === d.id ? 'text-white/80' : 'text-black/40'}`}>
                  {d.note}
                </span>
              </button>
            ))}
          </div>
        </div>

        {/* 3 Shift Cards Grid (inspired by Arena3 Court Grid) */}
        <div className="mt-10 grid grid-cols-1 gap-5 md:grid-cols-3">
          {SHIFTS.map((shift) => {
            const Icon = shift.icon
            const isSelected = selectedShift === shift.id
            return (
              <div
                key={shift.id}
                data-reveal
                onClick={() => setSelectedShift(shift.id)}
                className={`lp-card group relative cursor-pointer border p-6 transition-all duration-300 ${
                  isSelected
                    ? 'border-(--lp-forest) bg-white shadow-[0_12px_32px_-8px_rgba(26,47,37,0.18)] scale-[1.01]'
                    : 'border-(--lp-line) bg-white/80 hover:bg-white hover:border-(--lp-forest)/40 hover:shadow-md'
                }`}
              >
                {/* Header row */}
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3">
                    <span
                      className={`grid h-10 w-10 place-items-center rounded-lg transition-transform duration-300 group-hover:scale-105 ${
                        isSelected
                          ? 'bg-(--lp-forest) text-(--lp-mist)'
                          : 'bg-(--lp-sand) text-(--lp-forest)'
                      }`}
                    >
                      <Icon className="h-5 w-5" />
                    </span>
                    <div>
                      <h3 className="font-bold text-lg text-(--lp-forest)">{shift.name}</h3>
                      <p className="lp-num text-xs font-semibold text-(--lp-ink-soft)">{shift.time}</p>
                    </div>
                  </div>
                  <span className={`rounded-full px-2.5 py-1 text-xs font-semibold ${shift.tagColor}`}>
                    {shift.tag}
                  </span>
                </div>

                {/* Body info */}
                <p className="mt-4 text-sm leading-relaxed text-(--lp-ink)">
                  {shift.bestFor}
                </p>

                {/* Meta details */}
                <div className="mt-6 border-t border-(--lp-line) pt-4">
                  <div className="flex items-center justify-between text-xs">
                    <span className="flex items-center gap-1.5 text-(--lp-pine) font-medium">
                      <span className="relative flex h-2 w-2">
                        <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-(--lp-sage) opacity-75" />
                        <span className="relative inline-flex h-2 w-2 rounded-full bg-(--lp-sage)" />
                      </span>
                      {shift.workersAvailable} thợ sẵn sàng
                    </span>
                    <span className="text-(--lp-ink-soft)">
                      Từ <strong className="lp-num text-sm font-bold text-(--lp-forest)">{shift.priceStart} đ</strong>
                    </span>
                  </div>
                </div>

                {/* Selected radio marker */}
                <div className="mt-4 flex items-center justify-between pt-2">
                  <span className={`text-xs font-semibold ${isSelected ? 'text-(--lp-forest)' : 'text-(--lp-ink-soft)'}`}>
                    {isSelected ? '✓ Đang chọn ca này' : 'Bấm để chọn'}
                  </span>
                  <div
                    className={`h-5 w-5 rounded-full border flex items-center justify-center transition-colors ${
                      isSelected
                        ? 'border-(--lp-forest) bg-(--lp-forest) text-white'
                        : 'border-(--lp-line) bg-transparent'
                    }`}
                  >
                    {isSelected && <CheckCircle2 className="h-4 w-4" />}
                  </div>
                </div>
              </div>
            )
          })}
        </div>

        {/* Selected Summary Banner with Direct Booking Action */}
        <div data-reveal className="mt-8 flex flex-col items-center justify-between gap-4 rounded-xl border border-(--lp-forest)/20 bg-(--lp-forest) p-5 text-white shadow-lg md:flex-row md:px-8">
          <div className="flex items-center gap-4">
            <span className="grid h-12 w-12 shrink-0 place-items-center rounded-xl bg-white/10 text-(--lp-mist)">
              <Sparkles className="h-6 w-6" />
            </span>
            <div>
              <p className="text-sm font-medium text-white/80">
                Bạn đã chọn: <strong className="text-white font-bold">{current.name} ({current.time})</strong> — {activeDay === 'today' ? 'Hôm nay' : activeDay === 'tomorrow' ? 'Ngày mai' : 'Cuối tuần'}
              </p>
              <p className="text-xs text-white/60 flex items-center gap-1.5 mt-0.5">
                <MapPin className="h-3.5 w-3.5 text-(--lp-mist)" />
                Khu vực phủ sóng: Q1, Q2, Q7, Bình Thạnh, Thủ Đức và các quận lân cận
              </p>
            </div>
          </div>
          <a
            href="#tinh-gia"
            className="lp-btn lp-btn-light !py-2.5 !px-6 text-sm shrink-0 whitespace-nowrap group"
          >
            Tiếp tục tính giá diện tích <ArrowRight className="h-4 w-4 transition-transform duration-200 group-hover:translate-x-0.5" />
          </a>
        </div>
      </div>
    </section>
  )
}
