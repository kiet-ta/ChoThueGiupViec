// Pure logic of the folding sidebar tree (no React, so `node --test` covers it). A section is a folder row that opens and
// folds its links; the items without a section stay at the top level.
import type { Area, NavItem } from './feature-module'

/** Which sections are open, by section title. A section that is not in the record is folded. */
export type OpenSections = Record<string, boolean>

/** Whether `pathname` is the page of `item` or a page below it (`/admin/ledger` and `/admin/ledger/42`, not `/admin/ledger-x`). */
export function isItemActive(area: Area, item: NavItem, pathname: string): boolean {
  const base = `/${area}/${item.to}`
  return pathname === base || pathname.startsWith(`${base}/`)
}

/** The section that holds the current page, or undefined (a page of no section, or a path no item has). */
export function activeSection(area: Area, groups: { section?: string; items: NavItem[] }[], pathname: string): string | undefined {
  return groups.find((g) => g.section !== undefined && g.items.some((i) => isItemActive(area, i, pathname)))?.section
}

/** `open` with the section of the current page opened; every other section keeps what the person chose. */
export function reveal(open: OpenSections, section: string | undefined): OpenSections {
  if (section === undefined || open[section] === true) return open
  return { ...open, [section]: true }
}

/** The state after a click on a section row. Does not change the state it was given. */
export function toggle(open: OpenSections, section: string): OpenSections {
  return { ...open, [section]: open[section] !== true }
}

/** What was saved, as an `OpenSections`: anything that is not a JSON object of booleans gives an empty state. */
export function parseSaved(raw: string | null): OpenSections {
  if (raw === null) return {}
  try {
    const value: unknown = JSON.parse(raw)
    if (typeof value !== 'object' || value === null || Array.isArray(value)) return {}
    const open: OpenSections = {}
    for (const [key, v] of Object.entries(value)) if (v === true) open[key] = true
    return open
  } catch {
    return {}
  }
}

/** The text to save: only the open sections, the folded ones are the default. */
export function serialize(open: OpenSections): string {
  return JSON.stringify(Object.fromEntries(Object.entries(open).filter(([, v]) => v === true)))
}

/** The key under which an area saves its state. */
export const storageKey = (area: Area): string => `giupviec.sidebar.open.${area}`

/** An id for the list of links of a section, for `aria-controls` (section titles hold spaces, `&` and accents). */
export function sectionId(area: Area, index: number): string {
  return `nav-${area}-section-${index}`
}
