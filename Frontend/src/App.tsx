import { useEffect } from 'react'
import { RouterProvider } from 'react-router-dom'
import { router } from '@/app/router'
import { authController } from '@/auth'

export default function App() {
  // With a refresh token left from before a reload, get a new access token before any page is shown.
  useEffect(() => {
    void authController.bootstrap()
  }, [])

  return <RouterProvider router={router} />
}
