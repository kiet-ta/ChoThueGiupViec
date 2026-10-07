import { createBrowserRouter, Navigate } from 'react-router-dom'
import { RequireRole } from '@/auth/RequireRole'
import { AreaLayout } from './AreaLayout'
import { routesFor } from './feature-registry'
import { AreaHomePage } from '@/pages/AreaHomePage'
import { AreaNotFoundPage } from '@/pages/AreaNotFoundPage'
import { LoginPage } from '@/pages/LoginPage'
import { NotFoundPage } from '@/pages/NotFoundPage'

// Each area renders only for its own role (WEB-BASE-02): /admin needs Admin, /partner needs Partner.
export const router = createBrowserRouter([
  { path: '/', element: <Navigate to="/admin" replace /> },
  { path: '/login', element: <LoginPage /> },
  {
    path: '/admin',
    element: (
      <RequireRole area="admin">
        <AreaLayout area="admin" />
      </RequireRole>
    ),
    children: [
      { index: true, element: <AreaHomePage title="Admin console" /> },
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
  { path: '*', element: <NotFoundPage /> },
])
