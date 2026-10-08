import type { FeatureModule } from '@/app/feature-module'
import { DashboardPage } from './dashboard/DashboardPage'

// Owner adds routes and nav items here; nobody edits src/app.
const feature: FeatureModule = {
  area: 'admin',
  routes: [{ path: 'dashboard', element: <DashboardPage /> }],
  nav: [{ label: 'Tổng quan', to: 'dashboard' }],
}

export default feature
