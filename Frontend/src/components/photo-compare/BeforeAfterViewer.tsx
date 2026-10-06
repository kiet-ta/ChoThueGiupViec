import { useState, type KeyboardEvent } from 'react'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { pairPhotos, type ComparePhoto } from './pair-photos'

export interface BeforeAfterViewerProps {
  before: ComparePhoto[]
  after: ComparePhoto[]
  /** Angle shown first; defaults to the lowest angle. */
  initialAngleNo?: number
}

type Mode = 'side' | 'slider'

function VolBadge({ photo }: { photo: ComparePhoto }) {
  if (photo.volScore === undefined || photo.volScore === null) return null
  const ok = photo.isAccepted !== false
  return (
    <span
      className={cn(
        'absolute top-2 right-2 rounded-full px-2 py-0.5 text-xs font-medium',
        ok ? 'bg-emerald-100 text-emerald-900' : 'bg-red-100 text-red-900',
      )}
    >
      VoL {photo.volScore.toFixed(0)} · {ok ? 'Đạt nét' : 'Bị mờ'}
    </span>
  )
}

/** One photo or a "missing" placeholder; a broken image url falls back to the placeholder too. */
function Frame({ label, photo }: { label: string; photo: ComparePhoto | null }) {
  const [failed, setFailed] = useState<string | null>(null)
  const broken = photo !== null && failed === photo.url
  return (
    <figure className="flex flex-col gap-2">
      <figcaption className="text-sm font-medium">{label}</figcaption>
      <div className="relative aspect-4/3 overflow-hidden rounded-xl border border-border bg-muted">
        {photo && !broken ? (
          <>
            <img
              src={photo.url}
              alt={`${label} - góc ${photo.angleNo}`}
              loading="lazy"
              className="size-full object-cover"
              onError={() => setFailed(photo.url)}
            />
            <VolBadge photo={photo} />
          </>
        ) : (
          <div className="flex size-full items-center justify-center text-sm text-muted-foreground">
            {photo ? 'Không tải được ảnh' : 'Thiếu ảnh'}
          </div>
        )}
      </div>
    </figure>
  )
}

/** Overlay compare: the BEFORE photo is clipped at the slider position over the AFTER photo. */
function SliderCompare({ before, after }: { before: ComparePhoto | null; after: ComparePhoto | null }) {
  const [pos, setPos] = useState(50)
  if (!before || !after) {
    return (
      <div className="grid gap-4 sm:grid-cols-2">
        <Frame label="Trước" photo={before} />
        <Frame label="Sau" photo={after} />
      </div>
    )
  }
  return (
    <div className="flex flex-col gap-3">
      <div className="relative aspect-4/3 overflow-hidden rounded-xl border border-border bg-muted">
        <img src={after.url} alt={`Sau - góc ${after.angleNo}`} className="absolute inset-0 size-full object-cover" />
        <img
          src={before.url}
          alt={`Trước - góc ${before.angleNo}`}
          className="absolute inset-0 size-full object-cover"
          style={{ clipPath: `inset(0 ${100 - pos}% 0 0)` }}
        />
        <div className="absolute inset-y-0 w-0.5 bg-white shadow" style={{ left: `${pos}%` }} aria-hidden="true" />
        <span className="absolute top-2 left-2 rounded-full bg-black/60 px-2 py-0.5 text-xs text-white">Trước</span>
        <span className="absolute top-2 right-2 rounded-full bg-black/60 px-2 py-0.5 text-xs text-white">Sau</span>
      </div>
      <input
        type="range"
        min={0}
        max={100}
        value={pos}
        onChange={(e) => setPos(Number(e.target.value))}
        aria-label="Kéo để so sánh trước và sau"
        className="w-full"
      />
    </div>
  )
}

/**
 * Compares BEFORE and AFTER photos angle by angle (Dispute console, eKYC review, Agency review).
 * Arrow keys move between angles; the "Cạnh nhau / Kéo thanh" buttons switch the compare mode.
 */
export function BeforeAfterViewer({ before, after, initialAngleNo }: BeforeAfterViewerProps) {
  const pairs = pairPhotos(before, after)
  const startIndex = Math.max(0, pairs.findIndex((p) => p.angleNo === initialAngleNo))
  const [selected, setSelected] = useState(startIndex)
  const [mode, setMode] = useState<Mode>('side')

  if (pairs.length === 0) {
    return <p className="text-sm text-muted-foreground">Chưa có ảnh Trước/Sau để đối chiếu.</p>
  }

  const index = Math.min(selected, pairs.length - 1)
  const current = pairs[index]

  function onKeyDown(e: KeyboardEvent<HTMLDivElement>) {
    if (e.key === 'ArrowRight') setSelected(Math.min(index + 1, pairs.length - 1))
    if (e.key === 'ArrowLeft') setSelected(Math.max(index - 1, 0))
  }

  return (
    <div className="flex flex-col gap-4" tabIndex={0} onKeyDown={onKeyDown} aria-label="Đối chiếu ảnh Trước và Sau">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">
          Góc {current.angleNo} · {index + 1}/{pairs.length}
        </p>
        <div className="flex gap-1" role="group" aria-label="Chế độ so sánh">
          <Button size="sm" variant={mode === 'side' ? 'default' : 'outline'} onClick={() => setMode('side')}>
            Cạnh nhau
          </Button>
          <Button size="sm" variant={mode === 'slider' ? 'default' : 'outline'} onClick={() => setMode('slider')}>
            Kéo thanh
          </Button>
        </div>
      </div>

      {mode === 'side' ? (
        <div className="grid gap-4 sm:grid-cols-2">
          <Frame label="Trước" photo={current.before} />
          <Frame label="Sau" photo={current.after} />
        </div>
      ) : (
        <SliderCompare key={current.angleNo} before={current.before} after={current.after} />
      )}

      <div className="flex items-center gap-2 overflow-x-auto" role="tablist" aria-label="Chọn góc chụp">
        <Button size="sm" variant="outline" disabled={index === 0} onClick={() => setSelected(index - 1)}>
          Góc trước
        </Button>
        {pairs.map((p, i) => (
          <button
            key={p.angleNo}
            type="button"
            role="tab"
            aria-selected={i === index}
            onClick={() => setSelected(i)}
            className={cn(
              'flex shrink-0 items-center gap-1 rounded-lg border px-3 py-1.5 text-sm',
              i === index ? 'border-primary bg-primary text-primary-foreground' : 'border-border hover:bg-muted',
              (!p.before || !p.after) && 'border-dashed',
            )}
          >
            Góc {p.angleNo}
            {(!p.before || !p.after) && <span aria-label="Thiếu ảnh">!</span>}
          </button>
        ))}
        <Button size="sm" variant="outline" disabled={index === pairs.length - 1} onClick={() => setSelected(index + 1)}>
          Góc sau
        </Button>
      </div>
    </div>
  )
}
