/** Pure helper: strips non-digit characters and caps at max length. */
export function cleanDigits(raw: string, length = 6): string {
  return raw.replace(/\D/g, '').slice(0, length)
}

/** Pure helper: distributes pasted digits into the digit slots and determines next focus index. */
export function distributePastedDigits(
  current: string[],
  startIndex: number,
  pastedText: string,
  length = 6,
): { code: string; nextFocus: number } {
  const clean = cleanDigits(pastedText, length)
  const next = current.slice(0, length)
  while (next.length < length) next.push('')
  for (let i = 0; i < clean.length; i++) {
    if (startIndex + i < length) {
      next[startIndex + i] = clean[i]
    }
  }
  const code = next.join('')
  const nextFocus = Math.min(length - 1, startIndex + (clean.length > 0 ? clean.length : 0))
  return { code, nextFocus }
}
