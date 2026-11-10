import type { FeatureModule } from '@/app/feature-module'
import { LedgerPage } from './LedgerPage'

// Owner adds routes and nav items here; nobody edits src/app.
const feature: FeatureModule = {
  area: 'admin',
  routes: [{ path: 'ledger', element: <LedgerPage /> }],
  nav: [{ label: 'Sổ cái', to: 'ledger', section: 'TÀI CHÍNH' }],
}

export default feature
