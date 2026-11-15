import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { disputeDetailProblem, disputeOutcomeLines, disputeSteps } from '../src/features/customer/after-shift/view.ts'

const states = (status: string) => disputeSteps(status).map((s) => s.state)

describe('dispute detail (DISPUTE-DETAIL-01)', () => {
  it('shows where a dispute is', () => {
    assert.deepEqual(disputeSteps('OPEN').map((s) => s.label), ['Đã gửi', 'Đang xem xét', 'Đã giải quyết'])
    assert.deepEqual(states('OPEN'), ['current', 'todo', 'todo'])
    assert.deepEqual(states('IN_REVIEW'), ['done', 'current', 'todo'])
    assert.deepEqual(states('RESOLVED'), ['done', 'done', 'done'])
  })
  it('says when the dispute is not the customer\'s', () => {
    assert.equal(disputeDetailProblem({ status: 404 }), 'Không tìm thấy khiếu nại này.')
    assert.match(disputeDetailProblem({ status: 500 }), /Không tải được/)
  })
  it('keeps the verdict wording of the list', () => {
    const lines = disputeOutcomeLines({ disputeStatus: 'RESOLVED', faultParty: 'FREELANCER', resolvedAt: '2026-11-08T03:00:00Z', slaDueAt: '2026-11-09T03:00:00Z' })
    assert.equal(lines[0], 'Bên chịu trách nhiệm: Thợ')
  })
})
