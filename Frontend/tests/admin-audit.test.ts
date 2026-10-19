import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  actorText,
  auditQuery,
  auditRow,
  changeText,
  EMPTY_FILTER,
  ENTITY_TYPES,
  entityLabel,
  rangeError,
  vietnamDayStartUtc,
} from '../src/features/admin/audit/view.ts'
import type { AuditLogEntry } from '../src/features/admin/audit/types.ts'

const entry = (over: Partial<AuditLogEntry> = {}): AuditLogEntry => ({
  logId: 5,
  actorType: 'ADMIN',
  adminId: 1,
  entityType: 'DISPUTE_TICKET',
  entityId: '12',
  fieldName: 'dispute_status',
  oldValue: 'IN_REVIEW',
  newValue: 'RESOLVED',
  reason: 'Anh bang chung ro',
  changedAt: '2026-10-19T04:42:00Z',
  ...over,
})

describe('labels', () => {
  it('names the seven entity types the backend writes and shows an unknown one as it is', () => {
    assert.equal(ENTITY_TYPES.length, 7)
    assert.equal(entityLabel('WORKER'), 'Thợ')
    assert.equal(entityLabel('PAYOUT_BATCH'), 'Kỳ payout')
    assert.equal(entityLabel('SOMETHING_NEW'), 'SOMETHING_NEW')
  })

  it('shows the actor as an Admin number or the system', () => {
    assert.equal(actorText({ actorType: 'ADMIN', adminId: 3 }), 'Admin #3')
    assert.equal(actorText({ actorType: 'ADMIN', adminId: null }), 'Admin')
    assert.equal(actorText({ actorType: 'SYSTEM', adminId: null }), 'Hệ thống')
  })

  it('writes a change with a dash for a missing value', () => {
    assert.equal(changeText('IN_REVIEW', 'RESOLVED'), 'IN_REVIEW → RESOLVED')
    assert.equal(changeText(null, 'LOCKED'), '— → LOCKED')
    assert.equal(changeText('true', null), 'true → —')
  })
})

describe('auditRow', () => {
  it('formats the time in Vietnam, links a dispute to its case and keeps the reason', () => {
    const row = auditRow(entry())

    assert.equal(row.time, '19/10/2026 11:42')
    assert.equal(row.actor, 'Admin #1')
    assert.equal(row.entity, 'Khiếu nại tranh chấp')
    assert.equal(row.link, '/admin/disputes/12')
    assert.equal(row.change, 'IN_REVIEW → RESOLVED')
    assert.equal(row.reason, 'Anh bang chung ro')
  })

  it('has no link for other entities or a non numeric id, and a dash for an empty reason', () => {
    assert.equal(auditRow(entry({ entityType: 'WORKER', entityId: '8' })).link, null)
    assert.equal(auditRow(entry({ entityId: 'abc' })).link, null)
    assert.equal(auditRow(entry({ reason: null })).reason, '—')
    assert.equal(auditRow(entry({ reason: '   ' })).reason, '—')
  })
})

describe('the filter query', () => {
  it('drops empty fields and always sends the page', () => {
    assert.deepEqual(auditQuery(EMPTY_FILTER, 1), { page: 1, pageSize: 20 })
  })

  it('sends the entity, the trimmed id and the actor', () => {
    assert.deepEqual(auditQuery({ ...EMPTY_FILTER, entityType: 'WORKER', entityId: ' 8 ', actorType: 'ADMIN' }, 3), {
      page: 3,
      pageSize: 20,
      entityType: 'WORKER',
      entityId: '8',
      actorType: 'ADMIN',
    })
  })

  it('turns a Vietnam day into UTC instants: the day starts at 17:00 UTC of the day before, and the whole last day is included', () => {
    assert.equal(vietnamDayStartUtc('2026-10-19'), '2026-10-18T17:00:00.000Z')
    const q = auditQuery({ ...EMPTY_FILTER, from: '2026-10-19', to: '2026-10-19' }, 1)
    assert.equal(q.from, '2026-10-18T17:00:00.000Z')
    assert.equal(q.to, '2026-10-19T17:00:00.000Z')
  })

  it('crosses a month and a year end and drops a malformed date', () => {
    assert.equal(auditQuery({ ...EMPTY_FILTER, to: '2026-12-31' }, 1).to, '2026-12-31T17:00:00.000Z')
    assert.equal(auditQuery({ ...EMPTY_FILTER, from: '2026-01-01' }, 1).from, '2025-12-31T17:00:00.000Z')
    const q = auditQuery({ ...EMPTY_FILTER, from: '19/10/2026', to: 'x' }, 1)
    assert.equal('from' in q, false)
    assert.equal('to' in q, false)
  })

  it('refuses a range that ends before it starts and accepts the same day', () => {
    assert.match(rangeError({ ...EMPTY_FILTER, from: '2026-10-20', to: '2026-10-19' }) ?? '', /trước hoặc bằng/)
    assert.equal(rangeError({ ...EMPTY_FILTER, from: '2026-10-19', to: '2026-10-19' }), null)
    assert.equal(rangeError({ ...EMPTY_FILTER, from: '2026-10-19' }), null)
  })
})
