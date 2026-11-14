import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import {
  acceptanceTime,
  canAskRedo,
  customerAcceptanceView,
  REDO_WINDOW_CLOSED_TEXT,
  UNKNOWN_ACCEPTANCE_TITLE,
  type AcceptanceBlock,
} from '../src/features/customer/after-shift/acceptance.ts'
import { afterShiftProblem } from '../src/features/customer/after-shift/view.ts'
import { timelineDetail, timelineRows } from '../src/features/disputes/view.ts'

// ACCEPT-02 (decision D32): the 24-hour acceptance window on the customer web and the Admin case timeline.
const T = '2026-11-14T03:00:00Z' // 10:00 Vietnam time
const at = (minutes: number) => new Date(new Date(T).getTime() + minutes * 60_000)
const block = (phase: string, extra: Partial<AcceptanceBlock> = {}): AcceptanceBlock => ({
  phase,
  submittedAt: T,
  redoUntil: '2026-11-14T03:30:00Z',
  autoAcceptAt: phase === 'ON_SITE' || phase === 'REVIEW' ? '2026-11-15T03:00:00Z' : null,
  disputeUntil: '2026-11-15T03:00:00Z',
  completionSource: null,
  redoCount: 0,
  ...extra,
})

describe('acceptance window: times', () => {
  it('reads HH:mm in Vietnam time and adds the day when it is not today', () => {
    assert.equal(acceptanceTime('2026-11-14T03:30:00Z', at(0)), '10:30')
    assert.equal(acceptanceTime('2026-11-15T03:00:00Z', at(0)), '10:00 15/11')
  })
})

describe('acceptance window: the customer line', () => {
  it('names each phase with its deadline', () => {
    assert.deepEqual(customerAcceptanceView(block('ON_SITE'), at(10)), {
      title: 'Mời bạn kiểm tra',
      note: 'Người làm còn ở đây đến 10:30. Chỗ nào chưa sạch, hãy nhờ dọn lại ngay.',
      tone: 'warn',
    })
    assert.equal(customerAcceptanceView(block('REVIEW'), at(40))?.note, 'Tự nghiệm thu lúc 10:00 15/11 nếu bạn không phản hồi.')
    assert.equal(customerAcceptanceView(block('DISPUTED'), at(40))?.title, 'Đang xử lý khiếu nại')
    assert.equal(customerAcceptanceView(block('COMPLETED', { completionSource: 'CUSTOMER' }), at(40))?.note, 'Bạn đã nghiệm thu ca này.')
    assert.equal(customerAcceptanceView(block('COMPLETED', { completionSource: 'AUTO' }), at(40))?.note, 'Đã tự nghiệm thu sau 24 giờ.')
    assert.equal(customerAcceptanceView(block('COMPLETED', { completionSource: 'VERDICT' }), at(40))?.tone, 'done')
  })

  it('shows nothing before the hand-over or without a block, and "updating" for an unknown phase', () => {
    assert.equal(customerAcceptanceView(block('IN_PROGRESS'), at(0)), null)
    assert.equal(customerAcceptanceView(null, at(0)), null)
    assert.equal(customerAcceptanceView(undefined, at(0)), null)
    assert.equal(customerAcceptanceView(block('SOMETHING_NEW'), at(0))?.title, UNKNOWN_ACCEPTANCE_TITLE)
  })

  it('offers a redo only before redoUntil', () => {
    assert.equal(canAskRedo(block('ON_SITE'), at(29)), true)
    assert.equal(canAskRedo(block('ON_SITE'), at(30)), false)
    assert.equal(canAskRedo(block('COMPLETED'), at(0)), false)
    assert.equal(canAskRedo(null, at(0)), true) // an older server: the server decides
  })

  it('explains a 409 REDO_WINDOW_CLOSED and keeps the other messages', () => {
    assert.equal(afterShiftProblem({ status: 409, message: 'x', data: { code: 'REDO_WINDOW_CLOSED' } }), REDO_WINDOW_CLOSED_TEXT)
    assert.match(afterShiftProblem({ status: 409, message: 'The dispute already exists' }), /đã có khiếu nại/)
  })
})

describe('acceptance window: the Admin case timeline', () => {
  it('labels the hand-over, the redos and who accepted the job', () => {
    const rows = timelineRows([
      { at: T, type: 'SUBMITTED', detail: 'After photos submitted for acceptance' },
      { at: T, type: 'REDO_REQUESTED', detail: 'Customer asked for a redo on site 2 time(s) before this submit' },
    ])
    assert.deepEqual(rows.map((r) => [r.label, r.detail]), [
      ['Bàn giao nghiệm thu', 'thợ gửi ảnh After, bắt đầu 24 giờ chờ nghiệm thu'],
      ['Khách nhờ dọn lại', '2 lần, trước lần bàn giao này'],
    ])
    assert.equal(timelineDetail({ at: T, type: 'CHECK_OUT', detail: 'Job completed: accepted automatically after 24 hours' }), 'tự nghiệm thu sau 24 giờ')
    assert.equal(timelineDetail({ at: T, type: 'CHECK_OUT', detail: 'Job completed: closed by the dispute verdict' }), 'đóng theo phán quyết khiếu nại')
    assert.equal(timelineDetail({ at: T, type: 'CHECK_OUT', detail: 'Job completed: accepted by the customer' }), 'khách nghiệm thu')
    assert.equal(timelineDetail({ at: T, type: 'CHECK_OUT', detail: 'Job completed' }), '')
  })
})
