import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-3">
      <h1 className="text-2xl font-semibold">Không tìm thấy trang</h1>
      <Link to="/" className="underline">
        Về trang chủ
      </Link>
    </main>
  )
}
