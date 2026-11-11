import { useEffect, useMemo, useState } from 'react'
import { divIcon } from 'leaflet'
import { MapContainer, Marker, Polyline, TileLayer, useMap, ZoomControl } from 'react-leaflet'
import 'leaflet/dist/leaflet.css'
import { Navigation } from 'lucide-react'
import type { ApiError } from '@/services/api'
import { ordersApi } from './api'
import { decodePolyline6, formatDistance, formatDuration, ROUTE_REFRESH_MS, routeMidpoint, routeProblem, type WorkerRoute } from './view'

// Green rider marker (a motorbike on a green disc), drawn as inline SVG so no image file is bundled.
const workerIcon = divIcon({
  className: '',
  iconSize: [44, 44],
  iconAnchor: [22, 22],
  html: '<div style="width:44px;height:44px;border-radius:9999px;background:#16a34a;border:3px solid #fff;box-shadow:0 2px 6px rgba(0,0,0,.35);display:flex;align-items:center;justify-content:center"><svg width=\"28\" height=\"28\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"#fff\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><circle cx=\"5\" cy=\"17\" r=\"3\"/><circle cx=\"19\" cy=\"17\" r=\"3\"/><path d=\"M8 17h6.5l1.2-5H9.5z\" fill=\"#fff\"/><path d=\"M7 10.5h6\" stroke-width=\"2.6\"/><path d=\"M15.7 12l3.3 5\"/><path d=\"M14.6 8.5h3.2\"/></svg></div>',
})

// Google-style red map pin (teardrop); the tip is the address point. Inline SVG, no image file bundled.
const housePin = divIcon({
  className: '',
  iconSize: [32, 44],
  iconAnchor: [16, 44],
  html: `<svg width="32" height="44" viewBox="0 0 32 44" xmlns="http://www.w3.org/2000/svg" style="filter:drop-shadow(0 2px 2px rgba(0,0,0,.35))"><path d="M16 1C8 1 1.5 7.3 1.5 15.2 1.5 26 16 43 16 43s14.5-17 14.5-27.8C30.5 7.3 24 1 16 1z" fill="#EA4335" stroke="#B31412" stroke-width="1.2"/><circle cx="16" cy="15" r="5.5" fill="#7f1d1d"/></svg>`,
})

// Blue rounded time label on the middle of the route, like Google's route label.
function timeBubble(text: string) {
  return divIcon({
    className: '',
    iconSize: [0, 0],
    iconAnchor: [0, 0],
    html: `<div style="transform:translate(-50%,-50%);display:inline-block;white-space:nowrap;background:#4285F4;color:#fff;font:600 13px/1 system-ui,sans-serif;padding:6px 10px;border-radius:9999px;border:2px solid #fff;box-shadow:0 1px 4px rgba(0,0,0,.35)">${text}</div>`,
  })
}

function FitRoute({ points }: { points: [number, number][] }) {
  const map = useMap()
  useEffect(() => {
    if (points.length > 0) map.fitBounds(points, { padding: [32, 32] })
  }, [map, points])
  return null
}

/**
 * ROUTE-WEB-01 (#765): the route from the worker to the customer's house (Valhalla, motor_scooter), drawn like Google Maps on OpenStreetMap tiles softened with a CSS filter (white roads, light green parks, blue water; ROUTE-WEB-02, #774; CARTO Voyager now answers "API KEY REQUIRED" tiles and the Esri sets were too yellow or too grey).
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
  const midpoint = useMemo(() => routeMidpoint(line), [line])
  if (hidden) return null

  const who = workerName ?? 'Thợ'
  return (
    <section className="rounded-xl border border-stone-200 bg-white p-6" aria-label="Lộ trình của thợ">
      <h2 className="flex items-center gap-2 text-lg font-semibold"><Navigation size={18} />{who} đang trên đường tới</h2>
      {message && <p role="status" className="mt-2 text-sm text-stone-600">{message}</p>}
      {route && <>
        <p className="mt-2 text-sm text-stone-700">Khoảng cách: <strong>{formatDistance(route.distanceMeters)}</strong> · Dự kiến: <strong>{formatDuration(route.durationSeconds)}</strong></p>
        <div className="mt-4 h-72 overflow-hidden rounded-lg border border-stone-200">
          <MapContainer center={[route.destination.latitude, route.destination.longitude]} zoom={14} scrollWheelZoom zoomControl={false} className="h-full w-full">
            <TileLayer attribution="&copy; OpenStreetMap contributors" url="https://tile.openstreetmap.org/{z}/{x}/{y}.png" maxZoom={19} className="[filter:saturate(.85)_brightness(1.04)]" />
            <ZoomControl position="bottomright" />
            {line.length > 1 && <>
              <Polyline positions={line} pathOptions={{ color: '#ffffff', weight: 10, lineCap: 'round', lineJoin: 'round' }} />
              <Polyline positions={line} pathOptions={{ color: '#4285F4', weight: 6, lineCap: 'round', lineJoin: 'round' }} />
              {midpoint && <Marker position={midpoint} icon={timeBubble(formatDuration(route.durationSeconds))} interactive={false} keyboard={false} />}
            </>}
            <Marker position={[route.workerPosition.latitude, route.workerPosition.longitude]} icon={workerIcon} title={who} />
            <Marker position={[route.destination.latitude, route.destination.longitude]} icon={housePin} title="Địa chỉ" />
            <FitRoute points={line.length > 1 ? line : [[route.workerPosition.latitude, route.workerPosition.longitude], [route.destination.latitude, route.destination.longitude]]} />
          </MapContainer>
        </div>
        <p className="mt-2 text-xs text-stone-500">Vị trí thợ là gần đúng. Dữ liệu bản đồ © OpenStreetMap contributors.</p>
      </>}
    </section>
  )
}
