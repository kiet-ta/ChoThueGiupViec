// Pure grouping of the sidebar items (no React, so `node --test` covers it).
import type { NavItem } from './feature-module'

/**
 * Group the items under their optional `section` heading. Modules register in any order, so the items of one section are not
 * always next to each other: they are merged under one heading, in order of the first appearance of each section.
 */
export function groupBySection(items: NavItem[]): { section?: string; items: NavItem[] }[] {
  const groups: { section?: string; items: NavItem[] }[] = []
  for (const item of items) {
    const group = groups.find((g) => g.section === item.section)
    if (group) group.items.push(item)
    else groups.push({ section: item.section, items: [item] })
  }
  return groups
}
