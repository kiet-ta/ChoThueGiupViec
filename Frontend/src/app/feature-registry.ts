import type { FeatureModule, Area, NavItem } from './feature-module'
import type { RouteObject } from 'react-router-dom'

// Every feature drops a `routes.tsx` next to its code; Vite finds it at build time.
const files = import.meta.glob<{ default: FeatureModule }>('../features/*/routes.tsx', {
  eager: true,
})

const modules = Object.entries(files)
  .sort(([a], [b]) => a.localeCompare(b))
  .map(([, mod]) => mod.default)

export function routesFor(area: Area): RouteObject[] {
  return modules.filter((m) => m.area === area).flatMap((m) => m.routes)
}

export function navFor(area: Area): NavItem[] {
  return modules.filter((m) => m.area === area).flatMap((m) => m.nav ?? [])
}
