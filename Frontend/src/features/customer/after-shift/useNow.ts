import { useEffect, useState } from 'react'

/** The current time, refreshed every `everyMs` (ACCEPT-02: the acceptance sentence switches from "on site" to "waiting" by itself). */
export function useNow(everyMs = 30_000): Date {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), everyMs)
    return () => clearInterval(id)
  }, [everyMs])
  return now
}
