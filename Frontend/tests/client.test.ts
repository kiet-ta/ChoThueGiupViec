import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { ApiError, buildQuery, createApiClient, type ApiConfig, type FetchLike } from '../src/services/client.ts'

function reply(status: number, body: unknown, headers: Record<string, string> = {}): Response {
  const text = typeof body === 'string' ? body : JSON.stringify(body)
  return new Response(text === '' ? null : text, { status, headers })
}

function make(fetchImpl: FetchLike, config: ApiConfig = {}) {
  return createApiClient({ baseUrl: '/api', fetch: fetchImpl, config: () => config })
}

describe('api client', () => {
  it('returns data from a success envelope and builds the url', async () => {
    let seen = ''
    const client = make(async (url) => {
      seen = url
      return reply(200, { success: true, message: 'ok', data: { id: 7 } })
    })
    const data = await client.get<{ id: number }>('/admin/me', { query: { a: 1, b: undefined, c: 'x y' } })
    assert.deepEqual(data, { id: 7 })
    assert.equal(seen, '/api/admin/me?a=1&c=x+y')
  })

  it('sends the JSON body with content-type', async () => {
    let init: RequestInit | undefined
    const client = make(async (_url, i) => {
      init = i
      return reply(201, { success: true, message: '', data: null })
    })
    await client.post('/x', { name: 'A' })
    assert.equal(init?.method, 'POST')
    assert.equal(init?.body, '{"name":"A"}')
    const sent = (init?.headers ?? {}) as Record<string, string>
    assert.equal(sent['Content-Type'], 'application/json')
  })

  it('maps 400 to ApiError with fieldErrors (O5)', async () => {
    const client = make(async () =>
      reply(400, { success: false, message: 'invalid', data: { errors: { email: ['bad'], phone: ['a', 'b'] } } }),
    )
    await assert.rejects(client.post('/x', {}), (e: unknown) => {
      assert.ok(e instanceof ApiError)
      assert.equal(e.status, 400)
      assert.deepEqual(e.fieldErrors, { email: ['bad'], phone: ['a', 'b'] })
      return true
    })
  })

  it('adds Bearer token and calls onUnauthorized once on 401', async () => {
    let auth: string | undefined
    let calls = 0
    const client = make(
      async (_url, i) => {
        auth = ((i?.headers ?? {}) as Record<string, string>).Authorization
        return reply(401, { success: false, message: 'no', data: null })
      },
      { getAccessToken: () => 'tok', onUnauthorized: () => void calls++ },
    )
    await assert.rejects(client.get('/x'), (e: unknown) => e instanceof ApiError && e.status === 401)
    assert.equal(auth, 'Bearer tok')
    assert.equal(calls, 1)
  })

  it('does not call onUnauthorized for an anonymous 401 (wrong password)', async () => {
    let calls = 0
    const client = make(async () => reply(401, { success: false, message: 'wrong', data: null }), {
      onUnauthorized: () => void calls++,
    })
    await assert.rejects(client.post('/auth/password/login', {}))
    assert.equal(calls, 0)
  })

  it('reads Retry-After on 423', async () => {
    const client = make(async () => reply(423, { success: false, message: 'locked', data: null }, { 'Retry-After': '900' }))
    await assert.rejects(
      client.post('/x', {}),
      (e: unknown) => e instanceof ApiError && e.status === 423 && e.retryAfterSeconds === 900,
    )
  })

  it('turns a non-JSON 502 into ApiError, not SyntaxError', async () => {
    const client = make(async () => reply(502, '<html>Bad gateway</html>'))
    await assert.rejects(client.get('/x'), (e: unknown) => e instanceof ApiError && e.status === 502)
  })

  it('turns an empty 500 body into ApiError', async () => {
    const client = make(async () => reply(500, ''))
    await assert.rejects(client.get('/x'), (e: unknown) => e instanceof ApiError && e.status === 500)
  })

  it('turns a network failure into ApiError status 0', async () => {
    const client = make(async () => {
      throw new TypeError('fetch failed')
    })
    await assert.rejects(client.get('/x'), (e: unknown) => e instanceof ApiError && e.status === 0)
  })

  it('treats a 200 without the envelope as an error', async () => {
    const client = make(async () => reply(200, { hello: 'world' }))
    await assert.rejects(client.get('/x'), (e: unknown) => e instanceof ApiError && e.status === 200)
  })

  it('buildQuery drops null/undefined and returns empty string for nothing', () => {
    assert.equal(buildQuery(), '')
    assert.equal(buildQuery({ a: null, b: undefined }), '')
    assert.equal(buildQuery({ q: 'a&b', n: 0, ok: false }), '?q=a%26b&n=0&ok=false')
  })
})
