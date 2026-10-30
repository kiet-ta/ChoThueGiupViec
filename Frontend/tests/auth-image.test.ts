import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { apiClientPath, needsAuthenticatedFetch } from '../src/components/photo-compare/auth-image.ts'

describe('needsAuthenticatedFetch (ADMIN-UAT-11: a browser sends no Authorization header for an <img>)', () => {
  it('is true for the protected addresses of this API, such as the file of a shift photo', () => {
    assert.equal(needsAuthenticatedFetch('/api/workers/assignments/1/photos/2/file'), true)
    assert.equal(needsAuthenticatedFetch('/api/anything'), true)
  })

  it('is false for an external photo, the anonymous evidence folder and everything that is not an /api/ path', () => {
    for (const url of [
      'https://storage.example/jobs/before_1.jpg',
      'http://example.test/a.png',
      '/files/dispute-evidence/abc_c1-anh1.png',
      '/api', // not a path below /api/
      'api/workers/1',
      '//evil.test/api/x',
      '',
    ]) {
      assert.equal(needsAuthenticatedFetch(url), false, `"${url}"`)
    }
  })
})

describe('apiClientPath', () => {
  it('drops the /api base because the API client adds it (and VITE_API_BASE_URL when the API is on another origin)', () => {
    assert.equal(apiClientPath('/api/workers/assignments/1/photos/2/file'), '/workers/assignments/1/photos/2/file')
  })

  it('keeps a query string', () => {
    assert.equal(apiClientPath('/api/x?y=1'), '/x?y=1')
  })
})
