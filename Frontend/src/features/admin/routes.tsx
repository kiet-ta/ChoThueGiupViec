import type { FeatureModule } from '@/app/feature-module'
import { AbsencePage } from './absence/AbsencePage'
import { DashboardPage } from './dashboard/DashboardPage'

// Owner adds routes and nav items here; nobody edits src/app.
const feature: FeatureModule = {
  area: 'admin',
  routes: [
    { path: 'dashboard', element: <DashboardPage /> },
    { path: 'absence-reports', element: <AbsencePage /> },
  ],
  nav: [
    { label: 'Tổng quan', to: 'dashboard' },
    { label: 'Biên bản vắng mặt', to: 'absence-reports', section: 'QUẢN LÝ NHÂN SỰ' },
  ],
}

export default feature
