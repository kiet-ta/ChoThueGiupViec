import type { FeatureModule } from '@/app/feature-module'
import { AbsencePage } from './absence/AbsencePage'
import { AuditPage } from './audit/AuditPage'
import { DashboardPage } from './dashboard/DashboardPage'
import { WorkerControlsPage } from './worker-controls/WorkerControlsPage'

// Owner adds routes and nav items here; nobody edits src/app.
const feature: FeatureModule = {
  area: 'admin',
  routes: [
    { path: 'dashboard', element: <DashboardPage /> },
    { path: 'absence-reports', element: <AbsencePage /> },
    { path: 'worker-controls', element: <WorkerControlsPage /> },
    { path: 'audit-logs', element: <AuditPage /> },
  ],
  nav: [
    { label: 'Tổng quan', to: 'dashboard' },
    { label: 'Biên bản vắng mặt', to: 'absence-reports', section: 'QUẢN LÝ NHÂN SỰ' },
    { label: 'Cờ & mở khoá thợ', to: 'worker-controls', section: 'QUẢN LÝ NHÂN SỰ' },
    { label: 'Nhật ký kiểm toán', to: 'audit-logs', section: 'HỆ THỐNG' },
  ],
}

export default feature
