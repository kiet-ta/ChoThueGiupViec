import type { FeatureModule } from '@/app/feature-module'
import { DevPaymentsPage } from './DevPaymentsPage'

// UAT-PAY: a development tool; the backend answers 404 for it outside Development.
const feature: FeatureModule = {
  area: 'admin',
  routes: [{ path: 'dev-payments', element: <DevPaymentsPage /> }],
  nav: [{ label: 'Xác nhận thanh toán', to: 'dev-payments', section: 'CÔNG CỤ DEV' }],
}

export default feature
