import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '@/services/api'

export interface Resource<T> {
  data: T | null
  loading: boolean
  /** Message of the failed load; null while loading or after a success. */
  error: string | null
  reload: () => void
}

/** The text an Admin sees for a failed request: the server's message when there is one, else a generic line. */
export function errorText(e: unknown): string {
  if (e instanceof ApiError) return e.status === 0 ? 'Không kết nối được máy chủ.' : e.message || `Lỗi ${e.status}`
  return 'Đã xảy ra lỗi.'
}

/**
 * Loads data when the component mounts and whenever `deps` change; `reload()` loads again. A slow answer of an older request never
 * overwrites a newer one. Keeps the previous data on screen while a new request is running (`loading` is true meanwhile).
 */
export function useResource<T>(load: () => Promise<T>, deps: readonly unknown[]): Resource<T> {
  const [data, setData] = useState<T | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)
  const latest = useRef(0)

  useEffect(() => {
    const run = ++latest.current
    setLoading(true)
    setError(null)
    load().then(
      (value) => {
        if (run !== latest.current) return
        setData(value)
        setLoading(false)
      },
      (e: unknown) => {
        if (run !== latest.current) return
        setError(errorText(e))
        setLoading(false)
      },
    )
    // `load` is a closure recreated each render; the caller states what makes the request different in `deps`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick])

  const reload = useCallback(() => setTick((n) => n + 1), [])
  return { data, loading, error, reload }
}
