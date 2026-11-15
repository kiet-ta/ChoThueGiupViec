import { useLocation, useRoutes, type RouteObject } from 'react-router-dom'
import { useAuth } from '@/auth/useAuth'
import { AccountPage } from './account/AccountPage'
import { AddressesPage } from './account/AddressesPage'
import { AddressFormPage } from './account/AddressFormPage'
import { FavoritesPage } from './account/FavoritesPage'
import { ProfilePage } from './account/ProfilePage'
import { AcceptancePage } from './after-shift/AcceptancePage'
import { DisputeCreatePage } from './after-shift/DisputeCreatePage'
import { DisputeDetailPage } from './after-shift/DisputeDetailPage'
import { DisputesPage } from './after-shift/DisputesPage'
import { RatingPage } from './after-shift/RatingPage'
import { BookingPage } from './booking/BookingPage'
import { HomePage } from './home/HomePage'
import { ExtensionPage } from './orders/ExtensionPage'
import { OrderDetailPage } from './orders/OrderDetailPage'
import { OrdersPage } from './orders/OrdersPage'
import { PaymentPage } from './payment/PaymentPage'
import { CustomerFooter } from './shell/CustomerFooter'
import { CustomerNavbar } from './shell/CustomerNavbar'
import { bannerFor, NAV_GROUPS } from './shell/nav'
import './shell/customer-shell.css'

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
  { path: 'orders/:orderId/acceptance', element: <AcceptancePage /> },
  { path: 'orders/:orderId/rating', element: <RatingPage /> },
  { path: 'account', element: <AccountPage /> },
  { path: 'profile', element: <ProfilePage /> },
  { path: 'favorites', element: <FavoritesPage /> },
  { path: 'addresses', element: <AddressesPage /> },
  { path: 'addresses/new', element: <AddressFormPage /> },
  { path: 'addresses/:addressId', element: <AddressFormPage /> },
  { path: 'disputes', element: <DisputesPage /> },
  { path: 'disputes/new', element: <DisputeCreatePage /> },
  { path: 'disputes/:disputeId', element: <DisputeDetailPage /> },
]

function Placeholder() {
  const { pathname } = useLocation()
  const title = NAV_GROUPS.find((g) => g.to === pathname)?.label ?? 'Trang không tồn tại'
  return <><h1 className="text-3xl font-semibold">{title}</h1><p className="mt-3 text-stone-500">Nội dung đang được chuẩn bị.</p></>
}
export function CustomerShell() {
  const { logout } = useAuth()
  const { pathname } = useLocation()
  const content = useRoutes([...customerRoutes, { path: '*', element: <Placeholder /> }])
  // WEB-CUST-UI-01: layered canvas, the photo band of the part of the site, the page rising over it on each navigation.
  return <div className="cust-canvas flex min-h-screen flex-col text-[#1a2f25]">
    <CustomerNavbar onLogout={() => void logout()} />
    <div className="relative flex-1">
      <div className="cust-band" aria-hidden><img key={bannerFor(pathname)} src={bannerFor(pathname)} alt="" /></div>
      <main key={pathname} className="cust-main cust-page-in mx-auto w-full max-w-[1200px] px-5 py-12">{content}</main>
    </div>
    <CustomerFooter />
  </div>
}
