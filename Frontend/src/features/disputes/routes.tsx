import type { FeatureModule } from '@/app/feature-module'
import { DisputeCasePage } from './DisputeCasePage'
import { DisputeQueuePage } from './DisputeQueuePage'

// Owner adds routes and nav items here; nobody edits src/app.
const feature: FeatureModule = {
  area: 'admin',
  routes: [
    { path: 'disputes', element: <DisputeQueuePage /> },
    { path: 'disputes/:disputeId', element: <DisputeCasePage /> },
  ],
  nav: [{ label: 'Khiếu nại tranh chấp', to: 'disputes', section: 'XỬ LÝ TRANH CHẤP' }],
}

export default feature
