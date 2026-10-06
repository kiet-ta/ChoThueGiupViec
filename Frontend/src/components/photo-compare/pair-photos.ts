// Pairs BEFORE and AFTER job photos by angle (decisions Q03: the AFTER angle set must equal the BEFORE set).
// No imports: runs under `node --test`.

export interface ComparePhoto {
  angleNo: number
  url: string
  /** Variance-of-Laplacian sharpness stored by the server (JOB_PHOTO.vol_score). */
  volScore?: number | null
  /** JOB_PHOTO.is_accepted; a rejected (blurry) photo is a retake candidate. */
  isAccepted?: boolean | null
  caption?: string
}

export interface PhotoPair {
  angleNo: number
  before: ComparePhoto | null
  after: ComparePhoto | null
}

/** One photo per angle: an accepted one wins over a rejected one; among equals the last in the input wins (latest retake). */
function pickPerAngle(photos: ComparePhoto[]): Map<number, ComparePhoto> {
  const best = new Map<number, ComparePhoto>()
  for (const photo of photos) {
    const current = best.get(photo.angleNo)
    // A later photo replaces the current one unless it is rejected while the current one is accepted.
    if (!current || photo.isAccepted !== false || current.isAccepted === false) best.set(photo.angleNo, photo)
  }
  return best
}

/** Pairs sorted by angleNo; an angle present on one side only keeps `null` on the other. */
export function pairPhotos(before: ComparePhoto[], after: ComparePhoto[]): PhotoPair[] {
  const b = pickPerAngle(before)
  const a = pickPerAngle(after)
  const angles = [...new Set([...b.keys(), ...a.keys()])].sort((x, y) => x - y)
  return angles.map((angleNo) => ({ angleNo, before: b.get(angleNo) ?? null, after: a.get(angleNo) ?? null }))
}
