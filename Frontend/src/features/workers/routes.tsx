import type { FeatureModule } from '@/app/feature-module'
import { EkycAuditQueuePage } from './EkycAuditQueuePage'

// Owner adds routes and nav items here; nobody edits src/app.
const feature: FeatureModule = {
  area: 'admin',
  routes: [{ path: 'workers/ekyc-audit', element: <EkycAuditQueuePage /> }],
  nav: [{ label: 'Hậu kiểm eKYC Thợ', to: 'workers/ekyc-audit', section: 'QUẢN LÝ NHÂN SỰ' }],
}

export default feature

