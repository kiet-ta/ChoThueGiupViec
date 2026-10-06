// Pure API client core: no import.meta, no globals except what is injected, so it runs under `node --test`.
// Envelope and error rules: .spec/contracts/identity.md section 1 (O5 = field errors on 400).

export interface ApiResponse<T> {
  success: boolean
  message: string
  data: T | null
}

/** `data.errors` of a 400 (decision O5): field name -> messages. */
export type FieldErrors = Record<string, string[]>

export class ApiError extends Error {
  /** HTTP status; 0 when the request never got a response (network failure). */
  readonly status: number
  readonly data: unknown
  readonly fieldErrors: FieldErrors
  /** From the Retry-After header (423 lockout, 429 cooldown), in seconds. */
  readonly retryAfterSeconds: number | null

  constructor(
    status: number,
    message: string,
    options: { data?: unknown; fieldErrors?: FieldErrors; retryAfterSeconds?: number | null } = {},
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.data = options.data ?? null
    this.fieldErrors = options.fieldErrors ?? {}
    this.retryAfterSeconds = options.retryAfterSeconds ?? null
  }
}

export interface ApiConfig {
  /** Token for the Authorization header; undefined/null means anonymous. */
  getAccessToken?: () => string | null | undefined
  /** Called once per request that was sent with a token and answered 401. */
  onUnauthorized?: () => void
}

export type FetchLike = (input: string, init?: RequestInit) => Promise<Response>

export type QueryValue = string | number | boolean | null | undefined
export type Query = Record<string, QueryValue>

/** Builds `?a=1&b=x`; null/undefined values are dropped, the rest are encoded. Empty -> ''. */
export function buildQuery(query?: Query): string {
  if (!query) return ''
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value === null || value === undefined) continue
    params.append(key, String(value))
  }
  const text = params.toString()
  return text ? `?${text}` : ''
}

export interface RequestOptions {
  query?: Query
  /** Serialised as JSON; use for POST/PUT. */
  body?: unknown
  headers?: Record<string, string>
  signal?: AbortSignal
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function readFieldErrors(data: unknown): FieldErrors {
  if (!isRecord(data) || !isRecord(data.errors)) return {}
  const out: FieldErrors = {}
  for (const [field, messages] of Object.entries(data.errors)) {
    if (Array.isArray(messages)) out[field] = messages.map(String)
  }
  return out
}

function readRetryAfter(res: Response): number | null {
  const raw = res.headers.get('Retry-After')
  if (raw === null) return null
  const seconds = Number(raw)
  return Number.isFinite(seconds) && seconds >= 0 ? seconds : null
}

export function createApiClient(deps: { baseUrl: string; fetch: FetchLike; config?: () => ApiConfig }) {
  async function request<T>(method: string, path: string, options: RequestOptions = {}): Promise<T> {
    const config = deps.config?.() ?? {}
    const token = config.getAccessToken?.() ?? null

    const headers: Record<string, string> = { Accept: 'application/json', ...options.headers }
    if (options.body !== undefined) headers['Content-Type'] = 'application/json'
    if (token) headers.Authorization = `Bearer ${token}`

    let res: Response
    try {
      res = await deps.fetch(`${deps.baseUrl}${path}${buildQuery(options.query)}`, {
        method,
        headers,
        body: options.body === undefined ? undefined : JSON.stringify(options.body),
        signal: options.signal,
      })
    } catch (cause) {
      if (cause instanceof DOMException && cause.name === 'AbortError') throw cause
      throw new ApiError(0, 'Không kết nối được tới máy chủ.')
    }

    // A proxy error or a 500 may not carry the envelope; never leak a raw SyntaxError.
    let envelope: ApiResponse<T> | null = null
    const text = await res.text()
    if (text) {
      try {
        const parsed: unknown = JSON.parse(text)
        if (isRecord(parsed) && typeof parsed.success === 'boolean') envelope = parsed as unknown as ApiResponse<T>
      } catch {
        envelope = null
      }
    }

    if (res.ok && envelope?.success) return envelope.data as T

    if (res.status === 401 && token) config.onUnauthorized?.()
    throw new ApiError(res.status, envelope?.message || res.statusText || `HTTP ${res.status}`, {
      data: envelope?.data,
      fieldErrors: res.status === 400 ? readFieldErrors(envelope?.data) : {},
      retryAfterSeconds: readRetryAfter(res),
    })
  }

  return {
    request,
    get: <T>(path: string, options?: Omit<RequestOptions, 'body'>) => request<T>('GET', path, options),
    post: <T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'body'>) =>
      request<T>('POST', path, { ...options, body }),
    put: <T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'body'>) =>
      request<T>('PUT', path, { ...options, body }),
    delete: <T>(path: string, options?: Omit<RequestOptions, 'body'>) => request<T>('DELETE', path, options),
  }
}
