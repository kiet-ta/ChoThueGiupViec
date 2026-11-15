/** Parses a positive safe integer route parameter (`"42"` -> `42`), returning `null` for empty, zero, negative, fractional, or non-numeric strings. */
export function parsePositiveInt(raw: string | undefined): number | null {
  if (!raw) return null
  const trimmed = raw.trim()
  if (!/^[1-9]\d*$/.test(trimmed)) return null
  const value = Number(trimmed)
  return Number.isSafeInteger(value) && value > 0 ? value : null
}
