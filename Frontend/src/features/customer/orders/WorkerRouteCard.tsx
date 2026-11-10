import { useEffect, useMemo, useState } from 'react'
import { CircleMarker, MapContainer, Polyline, TileLayer, useMap } from 'react-leaflet'
import 'leaflet/dist/leaflet.css'
import { Navigation } from 'lucide-react'
import type { ApiError } from '@/services/api'
import { ordersApi } from './api'
import { decodePolyline6, formatDistance, formatDuration, ROUTE_REFRESH_MS, routeProblem, type WorkerRoute } from './view'

function FitRoute({ points }: { points: [number, number][] }) {
  const map = useMap()
  useEffect(() => {
    if (points.length > 0) map.fitBounds(points, { padding: [32, 32] })
  }, [map, points])
  return null
}

/**
 * ROUTE-WEB-01 (#765): the route from the worker to the customer's house (Valhalla, motor_scooter), drawn on OpenStreetMap tiles.
 * It reloads every 30 s. The worker's position is rounded by the server, so the marker is approximate. A failed call keeps the
 * rest of the order page working; a job that is no longer on its way (409/404) hides the card.
 */
export function WorkerRouteCard({ assignmentId, workerName }: { assignmentId: number; workerName: string | null }) {
  const [route, setRoute] = useState<WorkerRoute | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [hidden, setHidden] = useState(false)

  useEffect(() => {
    let cancelled = false
    async function load() {
      try {
        const r = await ordersApi.route(assignmentId)
        if (cancelled) return
        setRoute(r)
        setMessage(null)
        setHidden(false)
      } catch (e) {
        if (cancelled) return
        const text = routeProblem(e as ApiError)
        setHidden(text === null)
        setMessage(text)
        setRoute(null)
      }
    }
    void load()
    const timer = setInterval(() => void load(), ROUTE_REFRESH_MS)
    return () => {
      cancelled = true
      clearInterval(timer)
    }
  }, [assignmentId])

  const line = useMemo(() => (route ? decodePolyline6(route.polyline) : []), [route])
  if (hidden) return null

  const who = workerName ?? 'Thợ'
  return (
    <section className="rounded-xl border border-stone-200 bg-white p-6" aria-label="Lộ trình của thợ">
      <h2 className="flex items-center gap-2 text-lg font-semibold"><Navigation size={18} />{who} đang trên đường tới</h2>
      {message && <p role="status" className="mt-2 text-sm text-stone-600">{message}</p>}
      {route && <>
        <p className="mt-2 text-sm text-stone-700">Khoảng cách: <strong>{formatDistance(route.distanceMeters)}</strong> · Dự kiến: <strong>{formatDuration(route.durationSeconds)}</strong></p>
        <div className="mt-4 h-72 overflow-hidden rounded-lg border border-stone-200">
          <MapContainer center={[route.destination.latitude, route.destination.longitude]} zoom={14} scrollWheelZoom className="h-full w-full">
            <TileLayer attribution="&copy; OpenStreetMap contributors" url="https://tile.openstreetmap.org/{z}/{x}/{y}.png" />
            {line.length > 1 && <Polyline positions={line} pathOptions={{ color: '#1a2f25', weight: 5 }} />}
            <CircleMarker center={[route.workerPosition.latitude, route.workerPosition.longitude]} radius={9} pathOptions={{ color: '#1a2f25', fillColor: '#f59e0b', fillOpacity: 1 }} />
            <CircleMarker center={[route.destination.latitude, route.destination.longitude]} radius={9} pathOptions={{ color: '#7f1d1d', fillColor: '#ef4444', fillOpacity: 1 }} />
            <FitRoute points={line.length > 1 ? line : [[route.workerPosition.latitude, route.workerPosition.longitude], [route.destination.latitude, route.destination.longitude]]} />
          </MapContainer>
        </div>
        <p className="mt-2 text-xs text-stone-500">Vị trí thợ là gần đúng. Dữ liệu bản đồ © OpenStreetMap contributors.</p>
      </>}
    </section>
  )
}
