import { NavLink, Outlet } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/auth/useAuth'
import { cn } from '@/lib/utils'
import type { Area, NavItem } from './feature-module'
import { navFor } from './feature-registry'

const TITLES: Record<Area, string> = {
  admin: 'GiupViec Admin',
  partner: 'GiupViec Partner',
}

/** Group consecutive items under their optional `section` heading. */
function groupBySection(items: NavItem[]): { section?: string; items: NavItem[] }[] {
  const groups: { section?: string; items: NavItem[] }[] = []
  for (const item of items) {
    const last = groups[groups.length - 1]
    if (last && last.section === item.section) last.items.push(item)
    else groups.push({ section: item.section, items: [item] })
  }
  return groups
}

/**
 * Desktop shell, 1440x900 reference: 240 px sidebar + main content (Figma "Admin Page").
 * Colours come from the shadcn theme tokens only; mapping the TO AM tokens into
 * index.css is a separate ticket (design skill, section 4).
 */
export function AreaLayout({ area }: { area: Area }) {
  const groups = groupBySection(navFor(area))
  const { session, logout } = useAuth()
  return (
    <div className="flex min-h-screen bg-background text-foreground">
      <aside className="flex w-60 shrink-0 flex-col gap-6 border-r border-sidebar-border bg-sidebar p-5 text-sidebar-foreground">
        <div className="text-lg font-semibold">{TITLES[area]}</div>
        <nav aria-label={`${area} navigation`} className="flex flex-col gap-5">
          {groups.map((g) => (
            <div key={g.section ?? 'root'} className="flex flex-col gap-1">
              {g.section && (
                <div className="px-3 text-xs font-medium tracking-wide text-muted-foreground uppercase">
                  {g.section}
                </div>
              )}
              {g.items.map((item) => (
                <NavLink
                  key={item.to}
                  to={`/${area}/${item.to}`}
                  className={({ isActive }) =>
                    cn(
                      'rounded-lg px-3 py-2 text-sm',
                      isActive
                        ? 'bg-sidebar-accent text-sidebar-accent-foreground'
                        : 'hover:bg-sidebar-accent/60',
                    )
                  }
                >
                  {item.label}
                </NavLink>
              ))}
            </div>
          ))}
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
