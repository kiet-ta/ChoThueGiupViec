import type { FeatureModule } from '@/app/feature-module'
import { PriceRulesPage } from './price-rules/PriceRulesPage'

// Owner (M2) adds routes and nav items here; nobody edits src/app.
const feature: FeatureModule = {
  area: 'admin',
  routes: [{ path: 'price-rules', element: <PriceRulesPage /> }],
  nav: [{ label: 'Bảng giá', to: 'price-rules', section: 'CẤU HÌNH' }],
}

export default feature
