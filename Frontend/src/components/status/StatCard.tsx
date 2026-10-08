import { cn } from '@/lib/utils'

export interface StatFigure {
  /** null while there is no number to show (loading or a failed load): the card shows a dash, never a made-up 0. */
  value: number | null
  label: string
  /** `danger` colours the number red (for example disputes near their SLA while above 0). */
  tone?: 'default' | 'danger'
}

/** A dashboard card: a small caption and one or two big numbers with a label under each. */
export function StatCard({ title, figures, loading = false }: { title: string; figures: StatFigure[]; loading?: boolean }) {
  return (
    <section className="rounded-xl border border-border bg-card p-5 text-card-foreground" aria-busy={loading}>
      <h2 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{title}</h2>
      <div className="mt-3 flex divide-x divide-border">
        {figures.map((f) => (
          <div key={f.label} className="flex-1 px-4 first:pl-0 last:pr-0">
            {loading ? (
              <div className="h-9 w-16 animate-pulse rounded bg-muted" />
            ) : (
              <div className={cn('text-3xl font-semibold tabular-nums', f.tone === 'danger' && 'text-destructive')}>{f.value ?? '–'}</div>
            )}
            <div className="mt-1 text-sm text-muted-foreground">{f.label}</div>
          </div>
        ))}
      </div>
    </section>
  )
}
