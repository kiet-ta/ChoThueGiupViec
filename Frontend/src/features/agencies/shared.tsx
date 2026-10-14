import type { ReactNode } from 'react'

export function PageHeader({ eyebrow, title, children }: { eyebrow: string; title: string; children?: ReactNode }) {
  return (
    <header>
      <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{eyebrow}</div>
      <h1 className="text-2xl font-semibold">{title}</h1>
      {children && <p className="mt-1 text-sm text-muted-foreground">{children}</p>}
    </header>
  )
}

export function Alert({ tone = 'error', children }: { tone?: 'error' | 'info'; children: ReactNode }) {
  return (
    <div
      role={tone === 'error' ? 'alert' : 'status'}
      className={
        tone === 'error'
          ? 'rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm'
          : 'rounded-lg border border-border bg-muted p-3 text-sm'
      }
    >
      {children}
    </div>
  )
}

export function Panel({ title, children, aria }: { title?: string; children: ReactNode; aria?: string }) {
  return (
    <section className="rounded-xl border border-border bg-card p-5 text-card-foreground" aria-label={aria ?? title}>
      {title && <h2 className="mb-3 text-lg font-semibold">{title}</h2>}
      {children}
    </section>
  )
}

/** A QR payload is shown as text with the pay link; rendering an image needs a QR package no ticket has added. */
export function PayBox({ qrPayload, payUrl, expiresAt }: { qrPayload: string | null; payUrl: string | null; expiresAt: string }) {
  return (
    <div className="flex flex-col gap-2 rounded-lg border border-border bg-muted p-3 text-sm">
      <div className="font-medium">Thanh toán qua MoMo</div>
      {payUrl && (
        <a href={payUrl} target="_blank" rel="noreferrer" className="text-primary underline">
          Mở trang thanh toán
        </a>
      )}
      {qrPayload && <code className="break-all text-xs">{qrPayload}</code>}
      <div className="text-xs text-muted-foreground">Mã hết hạn lúc {new Date(expiresAt).toLocaleString('vi-VN')}.</div>
    </div>
  )
}

/** One labelled figure (text, so a VND amount or a status fits). */
export function Metric({ label, value }: { label: string; value: string }) {
  return (
    <section className="rounded-xl border border-border bg-card p-5 text-card-foreground">
      <h2 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{label}</h2>
      <div className="mt-2 text-2xl font-semibold tabular-nums">{value}</div>
    </section>
  )
}
