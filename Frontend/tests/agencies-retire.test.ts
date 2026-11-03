import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  buildRetireDialogState,
  canRetire,
  firstRetireRequest,
  futureJobText,
  replacementCandidates,
  retireFailureText,
  retireRequestFor,
  workStatusLabel,
} from '../src/features/agencies/view.ts'
import type { AgencyWorkerItem, RetireWorkerResponse } from '../src/features/agencies/types.ts'

// AGENCY-FIX-02: contract agencies.md 9.2 (preview, retire, error answers) and 9.3 (status labels).

const job = { assignmentId: 5001, orderId: 7001, slotDate: '2026-10-25', shiftCode: 'SHIFT_MORNING' }

const answer = (over: Partial<RetireWorkerResponse>): RetireWorkerResponse => ({
  workerId: 101,
  requiresConfirmation: false,
  workStatus: 'RETIRED',
  replacementWorkerId: null,
  futureJobs: null,
  warning: null,
  ...over,
})

const goodForm = {
  fullName: '  Tran Van B ',
  phoneNumber: '0901234567',
  nationalId: '012345678901',
  bankName: '',
  bankAccountNo: '',
}

describe('workStatusLabel (contract 9.3)', () => {
  it('has a label for every wire value', () => {
    assert.equal(workStatusLabel('IDLE'), 'Đang làm việc')
    assert.equal(workStatusLabel('BUSY'), 'Đang làm việc')
    assert.equal(workStatusLabel('LOCKED'), 'Bị khóa')
    assert.equal(workStatusLabel('RETIRING'), 'Đang chờ nghỉ')
    assert.equal(workStatusLabel('RETIRED'), 'Đã nghỉ')
  })
  it('shows an unknown value as it is, never a made-up label', () => {
    assert.equal(workStatusLabel('SOMETHING_NEW'), 'SOMETHING_NEW')
  })
  it('only a worker that is not Retiring or Retired can be retired', () => {
    assert.equal(canRetire('IDLE'), true)
    assert.equal(canRetire('LOCKED'), true)
    assert.equal(canRetire('RETIRING'), false)
    assert.equal(canRetire('RETIRED'), false)
  })
})

describe('buildRetireDialogState (one test per server answer)', () => {
  it('preview: requiresConfirmation asks the agency and lists the shifts', () => {
    const s = buildRetireDialogState(answer({ requiresConfirmation: true, workStatus: 'IDLE', futureJobs: [job] }))
    assert.equal(s.kind, 'choose')
    if (s.kind !== 'choose') return
    assert.deepEqual(s.jobs, [job])
    assert.match(s.message, /1 ca sắp tới/)
    assert.match(s.message, /phạt SLA/)
  })
  it('retired with a replacement: reports the status, the replacement and the moved shifts', () => {
    const s = buildRetireDialogState(
      answer({ replacementWorkerId: 202, futureJobs: [job], warning: 'Worker has future jobs that have been transferred to the replacement worker.' }),
    )
    assert.equal(s.kind, 'done')
    if (s.kind !== 'done') return
    assert.equal(s.statusText, 'Đã nghỉ')
    assert.equal(s.replacementWorkerId, 202)
    assert.deepEqual(s.jobs, [job])
    assert.equal(s.message, 'Worker has future jobs that have been transferred to the replacement worker.')
  })
  it('Retiring (a job in progress, or the rescue was confirmed)', () => {
    const s = buildRetireDialogState(answer({ workStatus: 'RETIRING', futureJobs: [job] }))
    assert.equal(s.kind, 'done')
    if (s.kind !== 'done') return
    assert.equal(s.statusText, 'Đang chờ nghỉ')
  })
  it('no future jobs: futureJobs is null and the result is still shown', () => {
    const s = buildRetireDialogState(answer({ futureJobs: null }))
    assert.equal(s.kind, 'done')
    if (s.kind !== 'done') return
    assert.deepEqual(s.jobs, [])
    assert.equal(s.message, 'Trạng thái của thợ: Đã nghỉ.')
  })
  it('a repeated call on an already retired worker is shown as a result, not as an error', () => {
    const s = buildRetireDialogState(answer({ workStatus: 'RETIRED', futureJobs: null }))
    assert.equal(s.kind, 'done')
  })
})

describe('request bodies (contract 9.2)', () => {
  it('first call: no replacement and no confirmRescue', () => {
    assert.deepEqual(firstRetireRequest(), {})
  })
  it('existing worker: only existingWorkerId', () => {
    assert.deepEqual(retireRequestFor({ type: 'existing', workerId: 202 }), { replacement: { existingWorkerId: 202 } })
  })
  it('existing worker not chosen yet: no body', () => {
    assert.equal(retireRequestFor({ type: 'existing', workerId: null }), null)
  })
  it('new worker: trimmed, the empty bank fields left out', () => {
    assert.deepEqual(retireRequestFor({ type: 'new', form: goodForm }), {
      replacement: { newWorker: { fullName: 'Tran Van B', phoneNumber: '0901234567', nationalId: '012345678901' } },
    })
  })
  it('new worker with the bank pair: both sent', () => {
    const body = retireRequestFor({ type: 'new', form: { ...goodForm, bankName: 'Vietcombank', bankAccountNo: '0011001234567' } })
    assert.deepEqual(body, {
      replacement: {
        newWorker: {
          fullName: 'Tran Van B',
          phoneNumber: '0901234567',
          nationalId: '012345678901',
          bankName: 'Vietcombank',
          bankAccountNo: '0011001234567',
        },
      },
    })
  })
  it('new worker with a bad phone: no body', () => {
    assert.equal(retireRequestFor({ type: 'new', form: { ...goodForm, phoneNumber: '123' } }), null)
  })
  it('rescue: confirmRescue true and nothing else', () => {
    assert.deepEqual(retireRequestFor({ type: 'rescue' }), { confirmRescue: true })
  })
})

describe('replacementCandidates (contract 9.2: not Pending, Locked, Retiring or Retired)', () => {
  const w = (workerId: number, workStatus: string): AgencyWorkerItem => ({ workerId, fullName: `W${workerId}`, workStatus, futureAssignedJobs: 0 })
  it('keeps Idle and Busy workers, drops the retiring one and the blocked statuses', () => {
    const list = [w(1, 'IDLE'), w(2, 'BUSY'), w(3, 'LOCKED'), w(4, 'RETIRING'), w(5, 'RETIRED'), w(6, 'PENDING'), w(101, 'IDLE')]
    assert.deepEqual(replacementCandidates(list, 101).map((x) => x.workerId), [1, 2])
  })
})

describe('futureJobText', () => {
  it('shows date, shift label and order', () => {
    assert.equal(futureJobText(job), '2026-10-25 · Ca sáng · đơn #7001')
  })
  it('shows an unknown shift code as it is', () => {
    assert.equal(futureJobText({ ...job, shiftCode: 'SHIFT_X' }), '2026-10-25 · SHIFT_X · đơn #7001')
  })
})

describe('retireFailureText (the server message, never ours)', () => {
  const err = (status: number, message: string, fieldErrors: Record<string, string[]> = {}) => ({ status, message, fieldErrors })
  for (const [code, message] of [
    ['SLOT_HELD', 'A future slot is held by a customer in checkout.'],
    ['QUOTA_EXCEEDED', 'The replacement would exceed the worker quota.'],
    ['REPLACEMENT_CONFLICT', 'No AVAILABLE slot on 2026-10-25 SHIFT_MORNING.'],
    ['WORKER_NOT_RETIRABLE', 'The worker is pending.'],
    ['ALREADY_EXISTS', 'Phone number already exists.'],
  ] as const) {
    it(`409 ${code} shows the server message`, () => {
      assert.equal(retireFailureText(err(409, message)), message)
    })
  }
  it('404 shows the server message', () => {
    assert.equal(retireFailureText(err(404, 'Worker not found.')), 'Worker not found.')
  })
  it('400 shows the field messages', () => {
    assert.equal(
      retireFailureText(err(400, 'Validation failed', { 'newWorker.phoneNumber': ['INVALID_FORMAT: phone'], 'newWorker.nationalId': ['REQUIRED: id'] })),
      'INVALID_FORMAT: phone REQUIRED: id',
    )
  })
  it('no connection and a non-error value have fixed words', () => {
    assert.equal(retireFailureText(err(0, '')), 'Không kết nối được máy chủ.')
    assert.equal(retireFailureText(new Error('x')), 'Đã xảy ra lỗi.')
    assert.equal(retireFailureText(null), 'Đã xảy ra lỗi.')
  })
  it('an empty message falls back to the status', () => {
    assert.equal(retireFailureText(err(500, '')), 'Lỗi 500')
  })
})
