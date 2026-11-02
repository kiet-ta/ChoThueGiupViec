import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { apiClient } from '@/services/api'
import { apiClientPath, needsAuthenticatedFetch } from './auth-image'

export interface AuthImageProps {
  src: string
  alt: string
  className?: string
  style?: CSSProperties
  loading?: 'lazy' | 'eager'
  /** Called when the picture cannot be shown (a failed request or a broken address). */
  onError?: () => void
}

/**
 * An `<img>` that can show a photo behind the API's Bearer token (ADMIN-UAT-11: the photos of a shift are stored at `/api/...` and a
 * browser sends no `Authorization` header for an image). A protected address is loaded through `apiClient.download` (base URL, token, a 401
 * ends the session) and shown from an object URL that is released when the picture goes away; any other address is a plain `<img>`.
 */
export function AuthImage({ src, alt, className, style, loading, onError }: AuthImageProps) {
  const protectedAddress = needsAuthenticatedFetch(src)
  const [loaded, setLoaded] = useState<{ src: string; url: string } | null>(null)
  const errorRef = useRef(onError)
  useEffect(() => {
    errorRef.current = onError
  })

  useEffect(() => {
    if (!protectedAddress) return
    let cancelled = false
    let objectUrl: string | null = null
    apiClient
      .download(apiClientPath(src))
      .then((blob) => {
        if (cancelled) return
        objectUrl = URL.createObjectURL(blob)
        setLoaded({ src, url: objectUrl })
      })
      .catch(() => {
        if (!cancelled) errorRef.current?.()
      })
    return () => {
      cancelled = true
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [src, protectedAddress])

  if (!protectedAddress) {
    return <img src={src} alt={alt} loading={loading} className={className} style={style} onError={onError} />
  }
  if (loaded?.src !== src) {
    return <div role="img" aria-label={alt} aria-busy="true" className={className} style={style} />
  }
  return <img src={loaded.url} alt={alt} className={className} style={style} onError={onError} />
}
