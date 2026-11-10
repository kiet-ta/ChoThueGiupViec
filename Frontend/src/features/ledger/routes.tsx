import type { FeatureModule } from '@/app/feature-module'
import { LedgerPage } from './LedgerPage'
import { WithdrawalsPage } from './WithdrawalsPage'

// Owner adds routes and nav items here; nobody edits src/app.
const feature: FeatureModule = {
  area: 'admin',
  routes: [
    { path: 'ledger', element: <LedgerPage /> },
    { path: 'withdrawals', element: <WithdrawalsPage /> },
  ],
  nav: [
    { label: 'Sổ cái', to: 'ledger', section: 'TÀI CHÍNH' },
    { label: 'Duyệt rút tiền', to: 'withdrawals', section: 'TÀI CHÍNH' },
  ],
}

export default feature
