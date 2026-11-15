import { Link } from 'react-router-dom'
import { House } from 'lucide-react'

/** The copyright year, read once when the module loads. */
const YEAR = new Date().getFullYear()

const COLUMNS: { title: string; links: { label: string; to: string }[] }[] = [
  { title: 'Đặt ca', links: [{ label: 'Đặt ca mới', to: '/app/booking' }, { label: 'Đơn sắp tới', to: '/app/orders?status=ASSIGNED' }, { label: 'Thợ yêu thích', to: '/app/favorites' }] },
  { title: 'Đơn', links: [{ label: 'Tất cả đơn', to: '/app/orders' }, { label: 'Chờ thanh toán', to: '/app/orders?status=PENDING_PAYMENT' }, { label: 'Hoàn tất', to: '/app/orders?status=COMPLETED' }] },
  { title: 'Hỗ trợ', links: [{ label: 'Khiếu nại của tôi', to: '/app/disputes' }, { label: 'Tạo khiếu nại', to: '/app/disputes/new' }] },
  { title: 'Tài khoản', links: [{ label: 'Hồ sơ', to: '/app/profile' }, { label: 'Địa chỉ', to: '/app/addresses' }, { label: 'Tổng quan', to: '/app/account' }] },
]

/** WEB-CUST-UI-01: a deep-green footer over a faint photo, with the links of each part of the site. No invented contact
 *  details or guarantees (design skill §3). */
export function CustomerFooter() {
  return <footer className="relative mt-16 overflow-hidden bg-[#1a2f25] text-white">
    <img src="/customer/broom-kitchen.jpg" alt="" aria-hidden className="pointer-events-none absolute inset-0 h-full w-full object-cover opacity-[0.12] mix-blend-luminosity" />
    <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(800px_400px_at_10%_0%,rgba(65,100,72,0.55),transparent_70%)]" />
    <div className="relative mx-auto grid max-w-[1200px] gap-10 px-5 py-12 md:grid-cols-[1.3fr_repeat(4,1fr)]">
      <div>
        <Link to="/app" className="flex items-center gap-2.5 text-lg font-bold"><span className="grid h-9 w-9 place-items-center rounded-xl bg-white/10 ring-1 ring-white/20"><House size={18} /></span>TỔ ẤM</Link>
        <p className="mt-4 max-w-xs text-sm leading-relaxed text-white/70">Đặt người giúp việc theo từng ca. Nhà sạch, lòng nhẹ.</p>
      </div>
      {COLUMNS.map((c) => <div key={c.title}>
        <p className="text-xs font-semibold uppercase tracking-wider text-[#D9A441]">{c.title}</p>
        <ul className="mt-3 space-y-2">{c.links.map((l) => <li key={l.label}><Link to={l.to} className="text-sm text-white/75 transition-colors hover:text-white">{l.label}</Link></li>)}</ul>
      </div>)}
    </div>
    <div className="relative border-t border-white/10">
      <div className="mx-auto flex max-w-[1200px] flex-wrap items-center justify-between gap-2 px-5 py-4 text-xs text-white/55">
        <span>© {YEAR} TỔ ẤM</span>
        <span>Ảnh: Unsplash</span>
      </div>
    </div>
  </footer>
}
