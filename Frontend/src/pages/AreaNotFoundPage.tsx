import { Link } from 'react-router-dom'

/** A path inside an area that no feature owns: rendered inside the layout, so the login guard applies first. */
export function AreaNotFoundPage({ homeTo }: { homeTo: string }) {
  return (
    <div className="flex flex-col gap-3">
      <h1 className="text-2xl font-semibold">Không tìm thấy trang</h1>
      <Link to={homeTo} className="underline">
        Về trang chủ
      </Link>
    </div>
  )
}
