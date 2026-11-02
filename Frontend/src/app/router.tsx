import { createBrowserRouter, Navigate } from 'react-router-dom'
import { RequireRole } from '@/auth/RequireRole'
import { AreaLayout } from './AreaLayout'
import { routesFor } from './feature-registry'
import { AreaHomePage } from '@/pages/AreaHomePage'
import { AreaNotFoundPage } from '@/pages/AreaNotFoundPage'
import { LandingPage } from '@/pages/LandingPage'
import { LoginPage } from '@/pages/LoginPage'
import { AdminLoginPage } from '@/pages/AdminLoginPage'
import { CustomerShell } from '@/features/customer/CustomerShell'
import { useAuth } from '@/auth/useAuth'
import { RegisterAgencyPage } from '@/pages/RegisterAgencyPage'
import { NotFoundPage } from '@/pages/NotFoundPage'

function AdminEntry() {
  const { status, session } = useAuth()
  if (status === 'loading') return <div role="status">Đang kiểm tra phiên đăng nhập…</div>
  if (!session) return <AdminLoginPage />
  return <RequireRole area="admin"><AreaLayout area="admin" /></RequireRole>
}

// Each area renders only for its own role (WEB-BASE-02): /admin needs Admin, /partner needs Partner.
export const router = createBrowserRouter([
  { path: '/', element: <LandingPage /> }, // public landing (ticket #569); the consoles stay at /admin and /partner
  { path: '/login', element: <LoginPage /> },
  { path: '/register', element: <RegisterAgencyPage /> },
  {
    path: '/admin',
    element: (
      <AdminEntry />
    ),
    children: [
      // The sidebar's first item is the dashboard; the bare /admin used to be an empty page.
      { index: true, element: <Navigate to="dashboard" replace /> },
      ...routesFor('admin'),
      { path: '*', element: <AreaNotFoundPage homeTo="/admin" /> },
    ],
  },
  {
    path: '/partner',
    element: (
      <RequireRole area="partner">
        <AreaLayout area="partner" />
      </RequireRole>
    ),
    children: [
      { index: true, element: <AreaHomePage title="Partner Portal" /> },
      ...routesFor('partner'),
      { path: '*', element: <AreaNotFoundPage homeTo="/partner" /> },
    ],
  },
  { path: '/app/*', element: <RequireRole area="customer"><CustomerShell /></RequireRole> },
  { path: '*', element: <NotFoundPage /> },
])
