// Pure price estimate for the landing calculator (ticket #569). No React import so `node --test` can run it.
//
// Every number below is copied from the approved design mock (Frontend/ui-template/plandingPageWeb.html,
// function `calc`). They are marketing estimates for the landing page only: the real booking price is
// computed by the backend (see Backend pricing), so these are NOT a source of truth.

export type PropertyType = 'apartment' | 'studio' | 'townhouse'

export const AREA_MIN_M2 = 30
export const AREA_MAX_M2 = 250
/** BR-21: above this floor area (or any townhouse) the dispatcher sends two workers. */
export const TWO_WORKER_THRESHOLD_M2 = 80
/** Lowest estimate shown when two workers are sent. */
export const TWO_WORKER_FLOOR_VND = 420_000
/** Estimates are shown rounded to the nearest 10.000 VND. */
export const ROUND_STEP_VND = 10_000

const STUDIO_BASE_VND = 180_000
const STUDIO_FREE_AREA_M2 = 40
const STUDIO_EXTRA_VND_PER_M2 = 1_500
const TOWNHOUSE_BASE_VND = 360_000
const TOWNHOUSE_VND_PER_M2 = 1_800
const APARTMENT_BASE_VND = 200_000
const APARTMENT_VND_PER_M2 = 1_400

export interface PriceEstimate {
  priceVnd: number
  workers: 1 | 2
}

export function estimatePrice(type: PropertyType, areaM2: number): PriceEstimate {
  let base: number
  if (type === 'studio') {
    base = STUDIO_BASE_VND
    if (areaM2 > STUDIO_FREE_AREA_M2) base += (areaM2 - STUDIO_FREE_AREA_M2) * STUDIO_EXTRA_VND_PER_M2
  } else if (type === 'townhouse') {
    base = TOWNHOUSE_BASE_VND + areaM2 * TOWNHOUSE_VND_PER_M2
  } else {
    base = APARTMENT_BASE_VND + areaM2 * APARTMENT_VND_PER_M2
  }

  const workers: 1 | 2 = areaM2 <= TWO_WORKER_THRESHOLD_M2 && type !== 'townhouse' ? 1 : 2
  if (workers === 2) base = Math.max(base, TWO_WORKER_FLOOR_VND)

  return { priceVnd: Math.round(base / ROUND_STEP_VND) * ROUND_STEP_VND, workers }
}
