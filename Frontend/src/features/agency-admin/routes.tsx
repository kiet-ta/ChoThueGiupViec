import type { FeatureModule } from '@/app/feature-module'
import { AppealsPage } from './AppealsPage'
import { PackagesPage } from './PackagesPage'

// Admin pages of the Agencies module (M5, decisions D9, D12); the Partner pages are in features/agencies.
const feature: FeatureModule = {
  area: 'admin',
  routes: [
    { path: 'subscription-packages', element: <PackagesPage /> },
    { path: 'escrow-appeals', element: <AppealsPage /> },
  ],
  nav: [
    { label: 'Gói thuê bao', to: 'subscription-packages', section: 'CẤU HÌNH' },
    { label: 'Khiếu nại ký quỹ', to: 'escrow-appeals', section: 'XỬ LÝ ĐỐI TÁC' },
  ],
}

export default feature
