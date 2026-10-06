// Keyboard rules of the Before/After viewer, kept pure so they run under `node --test`.

/** Form controls keep their own arrow-key behaviour (the compare slider is an <input type="range">). */
export function ownsArrowKeys(tagName: string, isContentEditable = false): boolean {
  return isContentEditable || ['INPUT', 'SELECT', 'TEXTAREA'].includes(tagName.toUpperCase())
}

/** -1 / +1 for ArrowLeft / ArrowRight, 0 for any other key. */
export function stepFromKey(key: string): -1 | 0 | 1 {
  if (key === 'ArrowLeft') return -1
  if (key === 'ArrowRight') return 1
  return 0
}

/** Moves by `step` and stays inside 0..length-1. */
export function moveIndex(index: number, step: number, length: number): number {
  return Math.min(Math.max(index + step, 0), Math.max(length - 1, 0))
}
