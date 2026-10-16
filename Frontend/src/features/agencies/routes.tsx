import type { FeatureModule } from '@/app/feature-module'
import { BankPage } from './BankPage'
import { EscrowPage } from './EscrowPage'
import { ImportPage } from './ImportPage'
import { JobsPage } from './JobsPage'
import { RosterPage } from './RosterPage'
import { SubscriptionPage } from './SubscriptionPage'
import { SwapPage } from './SwapPage'

// Owner (M5) adds routes and nav items here; nobody edits src/app. Partner Portal, decisions D9-D14.
const feature: FeatureModule = {
  area: 'partner',
  routes: [
    { path: 'subscription', element: <SubscriptionPage /> },
    { path: 'workers', element: <ImportPage /> },
    { path: 'roster', element: <RosterPage /> },
    { path: 'escrow', element: <EscrowPage /> },
    { path: 'bank', element: <BankPage /> },
    { path: 'jobs', element: <JobsPage /> },
    { path: 'swap', element: <SwapPage /> },
  ],
  nav: [
    { label: 'Gói thuê bao', to: 'subscription', section: 'TÀI KHOẢN' },
    { label: 'Ký quỹ & SLA', to: 'escrow', section: 'TÀI KHOẢN' },
    { label: 'Tài khoản nhận tiền', to: 'bank', section: 'TÀI KHOẢN' },
    { label: 'Thêm thợ', to: 'workers', section: 'NHÂN SỰ' },
    { label: 'Lịch ca tuần', to: 'roster', section: 'NHÂN SỰ' },
    { label: 'Đơn & hiệu suất', to: 'jobs', section: 'VẬN HÀNH' },
    { label: 'Đổi thợ 30 phút', to: 'swap', section: 'VẬN HÀNH' },
  ],
}

export default feature
