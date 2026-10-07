import type { FeatureModule } from '@/app/feature-module'
import { PayoutBatchDetailPage } from './PayoutBatchDetailPage'
import { PayoutBatchesPage } from './PayoutBatchesPage'

// Owner adds routes and nav items here; nobody edits src/app.
const feature: FeatureModule = {
  area: 'admin',
  routes: [
    { path: 'payout-batches', element: <PayoutBatchesPage /> },
    { path: 'payout-batches/:batchId', element: <PayoutBatchDetailPage /> },
  ],
  nav: [{ label: 'Kỳ payout', to: 'payout-batches', section: 'TÀI CHÍNH' }],
}

export default feature
