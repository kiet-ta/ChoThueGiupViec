import { useEffect, useState } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/auth/useAuth'
import { cn } from '@/lib/utils'
import type { Area, NavItem } from './feature-module'
import { navFor } from './feature-registry'
import { groupBySection } from './nav-groups'
import { activeSection, parseSaved, reveal, sectionId, serialize, storageKey, toggle, type OpenSections } from './nav-tree'

/** What the sidebar saved last time; a blocked or empty storage just gives the default (every section folded). */
function loadOpen(area: Area): OpenSections {
  try {
    return parseSaved(localStorage.getItem(storageKey(area)))
  } catch {
    return {}
  }
}

const TITLES: Record<Area, string> = {
  admin: 'GiupViec Admin',
  partner: 'GiupViec Partner',
}

/**
 * Desktop shell, 1440x900 reference: 240 px sidebar + main content (Figma "Admin Page").
 * Colours come from the shadcn theme tokens only; mapping the TO AM tokens into
 * index.css is a separate ticket (design skill, section 4).
 */
export function AreaLayout({ area }: { area: Area }) {
  const groups = groupBySection(navFor(area))
  const { session, logout } = useAuth()
  const { pathname } = useLocation()
  const current = activeSection(area, groups, pathname)
  // Folders: the section of the page being shown is open, the others open and fold by a click.
  const [open, setOpen] = useState<OpenSections>(() => reveal(loadOpen(area), current))
  // Moving to another page opens its section (adjusting state while rendering, not in an effect).
  const [shownPath, setShownPath] = useState(pathname)
  if (shownPath !== pathname) {
    setShownPath(pathname)
    setOpen((o) => reveal(o, current))
  }
  useEffect(() => {
    try {
      localStorage.setItem(storageKey(area), serialize(open))
    } catch {
      // storage is only a convenience: the sidebar works without it
    }
  }, [area, open])
  return (
    <div className="flex min-h-screen bg-background text-foreground">
      <aside className="flex w-60 shrink-0 flex-col gap-6 border-r border-sidebar-border bg-sidebar p-5 text-sidebar-foreground">
        <div className="text-lg font-semibold">{TITLES[area]}</div>
        <nav aria-label={`${area} navigation`} className="flex flex-col gap-5">
          {groups.map((g, index) =>
            g.section === undefined ? (
              <div key="root" className="flex flex-col gap-1">
                {g.items.map((item) => (
                  <SidebarLink key={item.to} area={area} item={item} />
                ))}
              </div>
            ) : (
              <div key={g.section} className="flex flex-col gap-1">
                <button
                  type="button"
                  aria-expanded={open[g.section] === true}
                  aria-controls={sectionId(area, index)}
                  onClick={() => setOpen((o) => toggle(o, g.section as string))}
                  className="flex items-center gap-1 rounded-lg px-3 py-1.5 text-left text-xs font-medium tracking-wide text-muted-foreground uppercase hover:bg-sidebar-accent/60"
                >
                  {open[g.section] === true ? (
                    <ChevronDown className="size-3.5 shrink-0" aria-hidden="true" />
                  ) : (
                    <ChevronRight className="size-3.5 shrink-0" aria-hidden="true" />
                  )}
                  {g.section}
                </button>
                {/* `hidden` takes a folded list out of the tab order and of the accessibility tree */}
                <div id={sectionId(area, index)} hidden={open[g.section] !== true}>
                  <div className="flex flex-col gap-1 pl-3">
                    {g.items.map((item) => (
                      <SidebarLink key={item.to} area={area} item={item} />
                    ))}
                  </div>
                </div>
              </div>
            ),
          )}
        </nav>
        <div className="mt-auto flex flex-col gap-2 border-t border-sidebar-border pt-4 text-sm">
          <span className="text-muted-foreground">{session ? `${session.user.role} #${session.user.id}` : ''}</span>
          <Button variant="outline" size="sm" onClick={() => void logout()}>
            Đăng xuất
          </Button>
        </div>
      </aside>
      <main className="min-w-0 flex-1 p-8">
        <Outlet />
      </main>
    </div>
  )
}

function SidebarLink({ area, item }: { area: Area; item: NavItem }) {
  return (
    <NavLink
      to={`/${area}/${item.to}`}
      className={({ isActive }) =>
        cn('rounded-lg px-3 py-2 text-sm', isActive ? 'bg-sidebar-accent text-sidebar-accent-foreground' : 'hover:bg-sidebar-accent/60')
      }
    >
      {item.label}
    </NavLink>
  )
}
