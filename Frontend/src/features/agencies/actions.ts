import { useRef, useState } from 'react'
import { ApiError } from '@/services/api'
import { codeMessage, errorCode } from './view'

/** The words for a failed call: a known business code, else the server's message, else a generic line. */
export function failureText(e: unknown): string {
  if (e instanceof ApiError) {
    if (e.status === 0) return 'Không kết nối được máy chủ.'
    const fromCode = codeMessage(errorCode(e.data))
    if (fromCode) return fromCode
    const first = Object.values(e.fieldErrors)[0]?.[0]
    return first ?? (e.message || `Lỗi ${e.status}`)
  }
  return 'Đã xảy ra lỗi.'
}

/**
 * Runs one async action at a time (a second click while it runs is ignored) and keeps its busy flag and error text.
 * `run` returns the action's value, or undefined when it failed or was ignored.
 */
export function useAction() {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const inFlight = useRef(false)

  async function run<T>(action: () => Promise<T>): Promise<T | undefined> {
    if (inFlight.current) return undefined
    inFlight.current = true
    setBusy(true)
    setError(null)
    try {
      return await action()
    } catch (e) {
      setError(failureText(e))
      return undefined
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  return { busy, error, run, setError }
}
