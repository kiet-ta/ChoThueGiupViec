/**
 * The customer Web navigation (WEB-CUST-UI-01): each menu of the navbar opens a dropdown with direct links into its pages,
 * so the customer reaches a filter or a sub-page in one click. Pure data, also used by the footer and the phone menu.
 */
export interface NavItem {
  label: string
  to: string
  hint: string
}
export interface NavGroup {
  key: 'home' | 'orders' | 'favorites' | 'account'
  label: string
  to: string
  items: NavItem[]
}

export const NAV_GROUPS: NavGroup[] = [
  {
    key: 'home', label: 'Trang chủ', to: '/app', items: [
      { label: 'Trang chủ', to: '/app', hint: 'Ca sắp tới và đặt nhanh' },
      { label: 'Đặt ca mới', to: '/app/booking', hint: 'Chọn gói, ngày và giờ làm' },
      { label: 'Đơn sắp tới', to: '/app/orders?status=ASSIGNED', hint: 'Các ca đã có thợ nhận' },
    ],
  },
  {
    key: 'orders', label: 'Đơn của tôi', to: '/app/orders', items: [
      { label: 'Tất cả đơn', to: '/app/orders', hint: 'Mọi đơn bạn đã đặt' },
      { label: 'Chờ thanh toán', to: '/app/orders?status=PENDING_PAYMENT', hint: 'Đơn cần thanh toán để tìm thợ' },
      { label: 'Đang tìm thợ', to: '/app/orders?status=DISPATCHING', hint: 'Hệ thống đang ghép thợ' },
      { label: 'Đã có thợ', to: '/app/orders?status=ASSIGNED', hint: 'Thợ đã nhận ca' },
      { label: 'Hoàn tất', to: '/app/orders?status=COMPLETED', hint: 'Đánh giá hoặc đặt lại' },
      { label: 'Khiếu nại của tôi', to: '/app/disputes', hint: 'Theo dõi khiếu nại đã gửi' },
      { label: 'Tạo khiếu nại', to: '/app/disputes/new', hint: 'Trong 24 giờ sau khi xong ca' },
    ],
  },
  {
    key: 'favorites', label: 'Thợ yêu thích', to: '/app/favorites', items: [
      { label: 'Thợ yêu thích', to: '/app/favorites', hint: 'Những người bạn tin tưởng' },
      { label: 'Đặt ca với thợ quen', to: '/app/favorites', hint: 'Bấm “Đặt lại” cạnh tên thợ' },
    ],
  },
  {
    key: 'account', label: 'Tài khoản', to: '/app/account', items: [
      { label: 'Tổng quan', to: '/app/account', hint: 'Thông tin tài khoản của bạn' },
      { label: 'Hồ sơ', to: '/app/profile', hint: 'Tên, email, số điện thoại' },
      { label: 'Địa chỉ', to: '/app/addresses', hint: 'Sổ địa chỉ và diện tích nhà' },
      { label: 'Thêm địa chỉ', to: '/app/addresses/new', hint: 'Lưu một nơi mới cần dọn' },
    ],
  },
]

/** The menu a page belongs to, to mark it active in the navbar. */
export function groupOf(pathname: string): NavGroup['key'] {
  if (pathname.startsWith('/app/orders') || pathname.startsWith('/app/disputes') || pathname.startsWith('/app/payments')) return 'orders'
  if (pathname.startsWith('/app/favorites')) return 'favorites'
  if (pathname.startsWith('/app/account') || pathname.startsWith('/app/profile') || pathname.startsWith('/app/addresses')) return 'account'
  return 'home'
}

/** The photo band of each part of the site (Unsplash License, `public/customer/CREDITS.txt`). */
const BANNERS: Record<NavGroup['key'], string> = {
  home: '/customer/mop-living-room.jpg',
  orders: '/customer/broom-kitchen.jpg',
  favorites: '/customer/vacuum-sofa.jpg',
  account: '/customer/caddy-stairs.jpg',
}
export const bannerFor = (pathname: string) => BANNERS[groupOf(pathname)]

/** The order statuses a link can open the orders page on (`?status=`). */
export const ORDER_STATUSES = ['PENDING_PAYMENT', 'DISPATCHING', 'ASSIGNED', 'COMPLETED', 'CANCELLED'] as const

/** The filter in the URL, or null for "Tất cả" (also for an unknown value). */
export function statusFromSearch(search: string | URLSearchParams): string | null {
  const value = (typeof search === 'string' ? new URLSearchParams(search) : search).get('status')
  return value && (ORDER_STATUSES as readonly string[]).includes(value) ? value : null
}
