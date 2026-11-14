import { useEffect, useRef } from 'react'
import { divIcon } from 'leaflet'
import { MapContainer, Marker, TileLayer, useMap, useMapEvents } from 'react-leaflet'
import 'leaflet/dist/leaflet.css'
import { ADDRESS_MAP_TILES } from './view'

// The house pin in the brand green (teardrop; the tip is the point). Inline SVG, no image file bundled.
const homePin = divIcon({
  className: '',
  iconSize: [32, 44],
  iconAnchor: [16, 44],
  html: '<svg width="32" height="44" viewBox="0 0 32 44" xmlns="http://www.w3.org/2000/svg" style="filter:drop-shadow(0 2px 2px rgba(0,0,0,.35))"><path d="M16 1C8 1 1.5 7.3 1.5 15.2 1.5 26 16 43 16 43s14.5-17 14.5-27.8C30.5 7.3 24 1 16 1z" fill="#1a2f25" stroke="#0f1d16" stroke-width="1.2"/><circle cx="16" cy="15" r="5.5" fill="#fff"/></svg>',
})

/** Zoom of a street with its houses, and of a whole district. */
const HOUSE_ZOOM = 17
const DISTRICT_ZOOM = 14

function ClickToPick({ onPick }: { onPick: (latitude: number, longitude: number) => void }) {
  useMapEvents({ click: (e) => onPick(e.latlng.lat, e.latlng.lng) })
  return null
}

/** Moves the map to the pin when [focus] changes ("Vị trí của tôi"); a click never moves the map. */
function FollowFocus({ focus, latitude, longitude }: { focus: number; latitude: number; longitude: number }) {
  const map = useMap()
  const followed = useRef(0)
  useEffect(() => {
    // Only a new focus moves the map, not every new pin.
    if (focus <= followed.current) return
    followed.current = focus
    map.setView([latitude, longitude], Math.max(map.getZoom(), HOUSE_ZOOM))
  }, [focus, latitude, longitude, map])
  return null
}

/**
 * ADDR-MAP-02 (#876): the map of the Web address form. A click puts the pin and hands the point to the page, which reads the
 * address there. The mouse wheel scrolls the page, not the map (zoom with the buttons or a double click).
 */
export function AddressMap({ latitude, longitude, hasPoint, focus, onPick }: {
  latitude: number
  longitude: number
  hasPoint: boolean
  focus: number
  onPick: (latitude: number, longitude: number) => void
}) {
  return <MapContainer center={[latitude, longitude]} zoom={hasPoint ? HOUSE_ZOOM : DISTRICT_ZOOM} scrollWheelZoom={false} className="h-full w-full cursor-crosshair">
    <TileLayer attribution={ADDRESS_MAP_TILES.attribution} url={ADDRESS_MAP_TILES.url} maxZoom={19} />
    <ClickToPick onPick={onPick} />
    <FollowFocus focus={focus} latitude={latitude} longitude={longitude} />
    {hasPoint && <Marker position={[latitude, longitude]} icon={homePin} title="Vị trí nhà bạn" />}
  </MapContainer>
}
