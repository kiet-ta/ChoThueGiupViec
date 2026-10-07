import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { areaOfRole, decideAccess, safeNext } from '../src/auth/access.ts'
import {
  createAuthController,
  MIN_REFRESH_DELAY_MS,
  refreshDelayMs,
  type AuthApi,
  type Timers,
} from '../src/auth/auth-controller.ts'
import { describeLoginError } from '../src/auth/login-errors.ts'
import { createSessionStore, REFRESH_TOKEN_KEY, type AuthResult, type KeyValueStorage } from '../src/auth/session.ts'

function memoryStorage(): KeyValueStorage & { data: Map<string, string> } {
  const data = new Map<string, string>()
  return {
    data,
    getItem: (k) => data.get(k) ?? null,
    setItem: (k, v) => void data.set(k, v),
    removeItem: (k) => void data.delete(k),
  }
}

function result(access: string, refresh: string, role = 'Admin', expires = 900): AuthResult {
  return { tokenType: 'Bearer', accessToken: access, accessTokenExpiresInSeconds: expires, refreshToken: refresh, user: { id: 1, role } }
}

describe('session store', () => {
  it('keeps the access token in memory only and the refresh token in storage', () => {
    const storage = memoryStorage()
    const store = createSessionStore(storage, () => 1_000)
    const session = store.set(result('ACCESS', 'REFRESH'))
    assert.equal(session.accessExpiresAt, 1_000 + 900_000)
    assert.deepEqual([...storage.data.keys()], [REFRESH_TOKEN_KEY])
    assert.equal(storage.data.get(REFRESH_TOKEN_KEY), 'REFRESH')
    assert.ok(![...storage.data.values()].some((v) => v.includes('ACCESS')), 'the access token is never stored')
  })

  it('loadRefreshToken survives a reload (new store, same storage) and clear wipes both', () => {
    const storage = memoryStorage()
    createSessionStore(storage).set(result('A', 'R'))
    const afterReload = createSessionStore(storage)
    assert.equal(afterReload.get(), null)
    assert.equal(afterReload.loadRefreshToken(), 'R')
    afterReload.clear()
    assert.equal(afterReload.loadRefreshToken(), null)
    assert.equal(storage.data.size, 0)
  })

  it('works in memory when the storage throws or is missing', () => {
    const throwing: KeyValueStorage = {
      getItem: () => { throw new Error('blocked') },
      setItem: () => { throw new Error('blocked') },
      removeItem: () => { throw new Error('blocked') },
    }
    for (const storage of [throwing, null]) {
      const store = createSessionStore(storage)
      assert.equal(store.set(result('A', 'R')).accessToken, 'A')
      assert.equal(store.loadRefreshToken(), 'R')
      store.clear()
      assert.equal(store.get(), null)
    }
  })
})

describe('refresh delay', () => {
  it('refreshes at 80 % of the lifetime and never in a tight loop', () => {
    assert.equal(refreshDelayMs(900), 720_000)
    assert.equal(refreshDelayMs(1), MIN_REFRESH_DELAY_MS)
    assert.equal(refreshDelayMs(0), MIN_REFRESH_DELAY_MS)
  })
})

describe('access decision', () => {
  it('maps only Admin and Partner to an area', () => {
    assert.equal(areaOfRole('Admin'), 'admin')
    assert.equal(areaOfRole('Partner'), 'partner')
    assert.equal(areaOfRole('Customer'), null)
    assert.equal(areaOfRole('Worker'), null)
    assert.equal(areaOfRole(null), null)
  })

  it('sends anonymous users to login with the wanted path', () => {
    assert.deepEqual(decideAccess(null, 'admin', '/admin/disputes?x=1'), { kind: 'login', next: '/admin/disputes?x=1' })
  })

  it('allows the own area and redirects the other one to the own area home', () => {
    assert.deepEqual(decideAccess('Admin', 'admin', '/admin'), { kind: 'allow' })
    assert.deepEqual(decideAccess('Partner', 'partner', '/partner/x'), { kind: 'allow' })
    assert.deepEqual(decideAccess('Admin', 'partner', '/partner'), { kind: 'redirect', to: '/admin' })
    assert.deepEqual(decideAccess('Partner', 'admin', '/admin/payouts'), { kind: 'redirect', to: '/partner' })
  })

  it('a role without any area here (Customer, Worker) is treated as not logged in', () => {
    assert.deepEqual(decideAccess('Customer', 'admin', '/admin'), { kind: 'login', next: '/admin' })
  })
})

describe('safeNext (open redirect)', () => {
  it('keeps a path inside the own area, with query and hash', () => {
    assert.equal(safeNext('/admin/disputes?page=2#x', 'Admin'), '/admin/disputes?page=2#x')
    assert.equal(safeNext('/admin', 'Admin'), '/admin')
    assert.equal(safeNext('/partner/agencies', 'Partner'), '/partner/agencies')
  })

  it('falls back to the area home for everything else', () => {
    const bad = [
      undefined, null, '', 'admin', 'https://evil.example/admin', '//evil.example/admin', '/\\evil.example',
      '/admin/../x\\y', '/administrator', '/partner/x', '/login', '/admin\n/x', 'javascript:alert(1)',
    ]
    for (const next of bad) assert.equal(safeNext(next, 'Admin'), '/admin', String(next))
    assert.equal(safeNext('/admin/x', 'Partner'), '/partner')
  })

  it('goes to login for a role without an area', () => {
    assert.equal(safeNext('/admin', 'Customer'), '/login')
  })
})

describe('login errors', () => {
  it('401 is one generic message that does not say which field was wrong', () => {
    const view = describeLoginError({ status: 401 })
    assert.equal(view.message, 'Email hoặc mật khẩu không đúng.')
    assert.deepEqual(view.fields, {})
  })

  it('400 puts the messages under the (lower-cased) fields', () => {
    const view = describeLoginError({ status: 400, fieldErrors: { Email: ['bad'], password: ['empty'] } })
    assert.deepEqual(view.fields, { email: ['bad'], password: ['empty'] })
    assert.equal(view.message, undefined)
  })

  it('400 without fields still shows a message', () => {
    assert.ok(describeLoginError({ status: 400 }).message)
  })

  it('403 says the account is disabled; 423 shows the wait in minutes; 0 is a network error', () => {
    assert.match(describeLoginError({ status: 403 }).message ?? '', /vô hiệu hóa/)
    assert.match(describeLoginError({ status: 423, retryAfterSeconds: 900 }).message ?? '', /15 phút/)
    assert.match(describeLoginError({ status: 423, retryAfterSeconds: 61 }).message ?? '', /2 phút/)
    assert.match(describeLoginError({ status: 423 }).message ?? '', /thử lại sau/i)
    assert.match(describeLoginError({ status: 0 }).message ?? '', /kết nối/)
  })

  it('never contains the password it was not given', () => {
    const view = describeLoginError({ status: 401 })
    assert.ok(!JSON.stringify(view).includes('Admin@'))
  })
})

describe('auth controller', () => {
  function setup(overrides: Partial<AuthApi> = {}, storage = memoryStorage()) {
    const calls: string[] = []
    const scheduled: { handler: () => void; ms: number; id: number; cleared: boolean }[] = []
    const timers: Timers = {
      setTimeout: (handler, ms) => {
        const t = { handler, ms, id: scheduled.length, cleared: false }
        scheduled.push(t)
        return t.id
      },
      clearTimeout: (id) => {
        scheduled[id as number].cleared = true
      },
    }
    let counter = 0
    const api: AuthApi = {
      login: async (email, _password, role) => {
        calls.push(`login ${email} ${role}`)
        return result('A1', 'R1', role)
      },
      refresh: async (token) => {
        calls.push(`refresh ${token}`)
        counter++
        return result(`A${counter + 1}`, `R${counter + 1}`)
      },
      logout: async (token) => {
        calls.push(`logout ${token}`)
      },
      ...overrides,
    }
    const store = createSessionStore(storage, () => 0)
    const controller = createAuthController({ api, store, timers })
    return { controller, calls, scheduled, storage, store }
  }

  it('login stores the session, publishes it and schedules a refresh at 80 %', async () => {
    const { controller, scheduled } = setup()
    let notified = 0
    controller.subscribe(() => notified++)
    await controller.login('a@b.c', 'pw', 'Admin')
    assert.equal(controller.getSnapshot().status, 'authenticated')
    assert.equal(controller.getAccessToken(), 'A1')
    assert.equal(scheduled.length, 1)
    assert.equal(scheduled[0].ms, 720_000)
    assert.ok(notified >= 1)
  })

  it('a failed login stores nothing and rethrows', async () => {
    const boom = new Error('401')
    const { controller, storage } = setup({ login: async () => { throw boom } })
    await assert.rejects(controller.login('a@b.c', 'bad', 'Admin'), (e) => e === boom)
    assert.equal(controller.getSnapshot().session, null)
    assert.equal(storage.data.size, 0)
  })

  it('bootstrap without a refresh token is anonymous and makes no request', async () => {
    const { controller, calls } = setup()
    assert.equal(await controller.bootstrap(), null)
    assert.equal(controller.getSnapshot().status, 'anonymous')
    assert.deepEqual(calls, [])
  })

  it('bootstrap with a stored refresh token gets a new session through refresh (reload case)', async () => {
    const storage = memoryStorage()
    storage.setItem(REFRESH_TOKEN_KEY, 'OLD')
    const { controller, calls } = setup({}, storage)
    const session = await controller.bootstrap()
    assert.equal(session?.accessToken, 'A2')
    assert.deepEqual(calls, ['refresh OLD'])
    assert.equal(storage.data.get(REFRESH_TOKEN_KEY), 'R2', 'the rotated refresh token replaces the old one')
  })

  it('parallel refreshes share one request (rotation would otherwise invalidate the second)', async () => {
    const storage = memoryStorage()
    storage.setItem(REFRESH_TOKEN_KEY, 'OLD')
    const { controller, calls } = setup({}, storage)
    const results = await Promise.all([controller.bootstrap(), controller.bootstrap(), controller.refresh()])
    assert.equal(calls.filter((c) => c.startsWith('refresh')).length, 1)
    assert.ok(results.every((r) => r?.accessToken === 'A2'))
  })

  it('a failed refresh ends the session and clears the stored token', async () => {
    const storage = memoryStorage()
    storage.setItem(REFRESH_TOKEN_KEY, 'OLD')
    const { controller } = setup({ refresh: async () => { throw new Error('401') } }, storage)
    assert.equal(await controller.bootstrap(), null)
    assert.equal(controller.getSnapshot().status, 'anonymous')
    assert.equal(storage.data.size, 0)
  })

  it('the scheduled refresh runs, replaces the timer and keeps the user logged in', async () => {
    const { controller, scheduled, calls } = setup()
    await controller.login('a@b.c', 'pw', 'Admin')
    scheduled[0].handler()
    await controller.refresh() // joins the refresh started by the timer
    assert.deepEqual(calls.filter((c) => c.startsWith('refresh')), ['refresh R1'])
    assert.equal(scheduled[0].cleared, true)
    assert.equal(scheduled.length, 2)
    assert.equal(controller.getAccessToken(), 'A2')
  })

  it('logout revokes the refresh token, clears everything and stops the timer', async () => {
    const { controller, calls, scheduled, storage } = setup()
    await controller.login('a@b.c', 'pw', 'Admin')
    await controller.logout()
    assert.deepEqual(calls.filter((c) => c.startsWith('logout')), ['logout R1'])
    assert.equal(controller.getSnapshot().status, 'anonymous')
    assert.equal(storage.data.size, 0)
    assert.equal(scheduled[0].cleared, true)
  })

  it('logout still ends the local session when the server call fails', async () => {
    const { controller, storage } = setup({ logout: async () => { throw new Error('network') } })
    await controller.login('a@b.c', 'pw', 'Admin')
    await controller.logout()
    assert.equal(controller.getSnapshot().status, 'anonymous')
    assert.equal(storage.data.size, 0)
  })

  it('a 401 on an authenticated request drops the session once', async () => {
    const { controller } = setup()
    await controller.login('a@b.c', 'pw', 'Admin')
    let notified = 0
    controller.subscribe(() => notified++)
    controller.handleUnauthorized()
    controller.handleUnauthorized()
    assert.equal(controller.getSnapshot().status, 'anonymous')
    assert.equal(notified, 1)
    assert.equal(controller.getAccessToken(), null)
  })

  it('areaOfRole of a refreshed session follows the role the server returned', async () => {
    const storage = memoryStorage()
    storage.setItem(REFRESH_TOKEN_KEY, 'OLD')
    const { controller } = setup({ refresh: async () => result('A', 'R', 'Partner') }, storage)
    const session = await controller.bootstrap()
    assert.equal(areaOfRole(session?.user.role), 'partner')
  })
})
