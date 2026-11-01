import type { RouteObject } from 'react-router-dom'
import { BookingPage } from './booking/BookingPage'
import { HomePage } from './home/HomePage'
import { PaymentPage } from './payment/PaymentPage'

/** Customer screens mounted by CustomerShell under /app. Paths not listed here stay placeholders until their own ticket. */
export const customerRoutes: RouteObject[] = [
  { index: true, element: <HomePage /> },
  { path: 'booking', element: <BookingPage /> },
  { path: 'payments/:orderId', element: <PaymentPage /> },
]
