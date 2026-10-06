import type { RouteObject } from 'react-router-dom'

/** The two desktop areas of Frontend/ (decision D3): Admin console and Partner Portal. */
export type Area = 'admin' | 'partner'

export interface NavItem {
  label: string
  /** Path relative to the area root, e.g. 'disputes' -> /admin/disputes */
  to: string
  /** Optional sidebar group heading, e.g. 'XỬ LÝ TRANH CHẤP' */
  section?: string
}

/**
 * What every `src/features/<name>/routes.tsx` must `export default`.
 * The shell (src/app) loads these files automatically, so a feature never edits src/app.
 */
export interface FeatureModule {
  area: Area
  /** Route objects relative to the area root (no leading slash on `path`). */
  routes: RouteObject[]
  nav?: NavItem[]
}
