import { createBrowserRouter, Navigate } from 'react-router-dom'
import { AreaLayout } from './AreaLayout'
import { routesFor } from './feature-registry'
import { AreaHomePage } from '@/pages/AreaHomePage'
import { NotFoundPage } from '@/pages/NotFoundPage'

// Role guards arrive with WEB-BASE-02; until then both areas are open.
export const router = createBrowserRouter([
  { path: '/', element: <Navigate to="/admin" replace /> },
  {
    path: '/admin',
    element: <AreaLayout area="admin" />,
    children: [{ index: true, element: <AreaHomePage title="Admin console" /> }, ...routesFor('admin')],
  },
  {
    path: '/partner',
    element: <AreaLayout area="partner" />,
    children: [{ index: true, element: <AreaHomePage title="Partner Portal" /> }, ...routesFor('partner')],
  },
  { path: '*', element: <NotFoundPage /> },
])
