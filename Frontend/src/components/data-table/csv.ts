// Client-side CSV of the rows currently on screen. Real bank files (.xlsx) are produced by the
// backend (payouts contract section 2.3) and are not handled here. No imports: runs under `node --test`.

export type CsvCell = string | number | boolean | null | undefined

export interface CsvColumn<T> {
  header: string
  value: (row: T) => CsvCell
}

const BOM = '﻿' // lets Excel read Vietnamese UTF-8

function escapeCell(cell: CsvCell): string {
  if (cell === null || cell === undefined) return ''
  let text = String(cell)
  // Spreadsheet formula injection: user text such as "=HYPERLINK(...)" must stay text.
  if (typeof cell === 'string' && /^[=+\-@\t\r]/.test(text)) text = `'${text}`
  return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text
}

export function toCsv<T>(columns: CsvColumn<T>[], rows: T[]): string {
  const lines = [columns.map((c) => escapeCell(c.header)).join(',')]
  for (const row of rows) lines.push(columns.map((c) => escapeCell(c.value(row))).join(','))
  return `${BOM}${lines.join('\r\n')}\r\n`
}
