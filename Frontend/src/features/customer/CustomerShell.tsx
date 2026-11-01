import { House, LogOut, Menu } from 'lucide-react'
import { Link, NavLink, useLocation, useRoutes, type RouteObject } from 'react-router-dom'
import { useAuth } from '@/auth/useAuth'
import { BookingPage } from './booking/BookingPage'
import { HomePage } from './home/HomePage'
import { ExtensionPage } from './orders/ExtensionPage'
import { OrderDetailPage } from './orders/OrderDetailPage'
import { OrdersPage } from './orders/OrdersPage'
import { PaymentPage } from './payment/PaymentPage'

// Kept here, not in `customer/routes.tsx`: src/app/feature-registry.ts loads every `features/*/routes.tsx` as an Admin/Partner
// FeatureModule, and a customer file there breaks the whole app at start-up. Paths not listed stay placeholders.
const customerRoutes: RouteObject[] = [
  { index: true, element: <HomePage /> },
  { path: 'booking', element: <BookingPage /> },
  { path: 'payments/extension/:extensionId', element: <PaymentPage kind="extension" /> },
  { path: 'payments/:orderId', element: <PaymentPage /> },
  { path: 'orders', element: <OrdersPage /> },
  { path: 'orders/:orderId', element: <OrderDetailPage /> },
  { path: 'orders/:orderId/extension', element: <ExtensionPage /> },
]

function Placeholder() {
  const { pathname } = useLocation()
  const title = links.find(link => link.to === pathname)?.label ?? 'Trang không tồn tại'
  return <><h1 className="text-3xl font-semibold">{title}</h1><p className="mt-3 text-stone-500">Nội dung đang được chuẩn bị.</p></>
}
const links = [{ to: '/app', label: 'Trang chủ' }, { to: '/app/orders', label: 'Đơn của tôi' }, { to: '/app/favorites', label: 'Thợ yêu thích' }, { to: '/app/account', label: 'Tài khoản' }]
export function CustomerShell() {
  const { logout } = useAuth()
  const content = useRoutes([...customerRoutes, { path: '*', element: <Placeholder /> }])
  return <div className="min-h-screen bg-[#f7f8f4] text-[#1a2f25]">
    <header className="sticky top-0 z-20 border-b border-stone-200 bg-white/95 backdrop-blur"><div className="mx-auto flex max-w-[1200px] flex-wrap items-center justify-between gap-4 px-5 py-4"><Link to="/app" className="flex items-center gap-2 text-lg font-bold"><span className="grid h-9 w-9 place-items-center rounded-lg bg-[#1a2f25] text-white"><House size={19}/></span>TỔ ẤM</Link><nav aria-label="Điều hướng khách hàng" className="order-3 flex w-full gap-4 overflow-x-auto text-sm md:order-none md:w-auto">{links.map(link => <NavLink key={link.to} to={link.to} end={link.to === '/app'} className={({isActive}) => `whitespace-nowrap py-2 ${isActive ? 'font-semibold text-[#1a2f25]' : 'text-stone-500'}`}>{link.label}</NavLink>)}</nav><div className="flex items-center gap-3"><Link to="/app/booking" className="rounded-lg bg-[#1a2f25] px-4 py-2 text-sm font-semibold text-white">Đặt ca</Link><button type="button" onClick={() => void logout()} className="flex items-center gap-1 text-sm text-stone-600" aria-label="Đăng xuất"><LogOut size={17}/><span className="hidden sm:inline">Đăng xuất</span></button><Menu className="hidden" /></div></div></header>
    <main className="mx-auto max-w-[1200px] px-5 py-12">{content}</main>
  </div>
}
