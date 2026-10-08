import type { FeatureModule } from '@/app/feature-module'
import { DispatchMonitoringPage } from './DispatchMonitoringPage'

const feature: FeatureModule = {
  area: 'admin',
  routes: [
    {
      path: 'dispatch',
      element: <DispatchMonitoringPage />,
    },
  ],
  nav: [
    {
      label: 'Giám sát điều phối',
      to: 'dispatch',
      section: 'ĐIỀU PHỐI & HIỆN TRƯỜNG',
    },
  ],
}

export default feature
