import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  assessmentText,
  confidenceInfo,
  ekycQueueRow,
  isUnderThreshold,
  scoreBadgeText,
  NO_SCORE_LABEL,
  parseActionError,
  validateRejectionReason,
  TABS,
  ekycQueueRequest,
  ekycReviewRequest,
} from '../src/features/workers/view.ts'
import type { EkycQueueItemResponse } from '../src/features/workers/types.ts'

const item = (over: Partial<EkycQueueItemResponse> = {}): EkycQueueItemResponse => ({
  workerId: 102,
  fullName: 'Trần Văn Bình',
  phoneNumber: '0987654321',
  nationalId: '098765432109',
  frontCccdUrl: 'https://storage.giupviec.local/ekyc/102_front.jpg',
  backCccdUrl: 'https://storage.giupviec.local/ekyc/102_back.jpg',
  selfieUrl: 'https://storage.giupviec.local/ekyc/102_selfie.jpg',
  confidenceScore: 78.5,
  submittedAt: '2026-10-08T11:30:00Z',
  ...over,
})

describe('TABS', () => {
  it('has the three statuses for eKYC review filtering', () => {
    assert.deepEqual(
      TABS.map((t) => t.status),
      ['PENDING', 'APPROVED', 'REJECTED']
    )
    assert.deepEqual(
      TABS.map((t) => t.label),
      ['Chờ hậu kiểm', 'Đã duyệt', 'Từ chối']
    )
  })
})

describe('confidenceInfo', () => {
  it('gives success tone when score >= 85.0 (auto-approve threshold)', () => {
    const info = confidenceInfo(85.0)
    assert.equal(info.tone, 'success')
    assert.match(info.label, /85\.0%/)
    assert.match(info.label, /Tự động duyệt/)
  })

  it('gives warning tone when score is between 70.0 and 84.9 (manual review)', () => {
    const info = confidenceInfo(78.5)
    assert.equal(info.tone, 'warning')
    assert.match(info.label, /78\.5%/)
    assert.match(info.label, /Cần xem xét/)
  })

  it('gives danger tone when score is below 70.0', () => {
    const info = confidenceInfo(65.2)
    assert.equal(info.tone, 'danger')
    assert.match(info.label, /65\.2%/)
    assert.match(info.label, /Nghi vấn thấp/)
  })
})

describe('a worker without a score (ADMIN-UAT-09: ekyc_confidence is NULL until eKYC is submitted)', () => {
  it('confidenceInfo(null) is neutral, says there is no score and does not throw', () => {
    const info = confidenceInfo(null)
    assert.equal(info.tone, 'neutral')
    assert.equal(info.label, NO_SCORE_LABEL)
    assert.doesNotMatch(info.label, /\d/)
  })

  it('an undefined score (field missing in the answer) is handled like null', () => {
    assert.equal(confidenceInfo(undefined as unknown as null).label, NO_SCORE_LABEL)
  })

  it('a missing score is never shown as 0% or as a low score', () => {
    assert.notEqual(confidenceInfo(null).tone, 'danger')
    assert.doesNotMatch(scoreBadgeText(null), /%/)
    assert.doesNotMatch(assessmentText(null), /%/)
  })

  it('needs a manual look: under the threshold when the score is missing', () => {
    assert.equal(isUnderThreshold(null), true)
    assert.equal(isUnderThreshold(84.9), true)
    assert.equal(isUnderThreshold(85), false)
  })

  it('the badge and the assessment keep their wording for a real score', () => {
    assert.equal(scoreBadgeText(92), 'Điểm tin cậy: 92.0%')
    assert.match(assessmentText(78.5), /78\.5%.*thấp hơn ngưỡng tự động \(85%\)/)
    assert.match(assessmentText(92), /đạt 92\.0%/)
  })

  it('ekycQueueRow does not throw and keeps the null', () => {
    const row = ekycQueueRow(item({ confidenceScore: null }))
    assert.equal(row.confidenceScore, null)
    assert.equal(row.confidenceText, NO_SCORE_LABEL)
    assert.equal(row.confidenceTone, 'neutral')
  })

  it('a page with one scored and one unscored worker maps every row (one null row used to break the whole page)', () => {
    const rows = [item({ workerId: 1, confidenceScore: 91 }), item({ workerId: 2, confidenceScore: null })].map(ekycQueueRow)
    assert.deepEqual(rows.map((r) => r.workerId), [1, 2])
    assert.equal(rows[0].confidenceTone, 'success')
    assert.equal(rows[1].confidenceTone, 'neutral')
  })
})

describe('ekycQueueRow', () => {
  it('correctly maps raw item into row structure', () => {
    const row = ekycQueueRow(item())
    assert.equal(row.workerId, 102)
    assert.equal(row.fullName, 'Trần Văn Bình')
    assert.equal(row.phoneNumber, '0987654321')
    assert.equal(row.nationalId, '098765432109')
    assert.equal(row.confidenceScore, 78.5)
    assert.equal(row.confidenceTone, 'warning')
  })
})

describe('validateRejectionReason', () => {
  it('rejects empty or whitespace reason', () => {
    assert.match(validateRejectionReason('') ?? '', /nhập lý do/)
    assert.match(validateRejectionReason('   ') ?? '', /nhập lý do/)
  })

  it('accepts valid reason within 255 chars', () => {
    assert.equal(validateRejectionReason('Ảnh CCCD mờ không thấy rõ số'), null)
    assert.equal(validateRejectionReason('a'.repeat(255)), null)
  })

  it('rejects reason exceeding 255 chars', () => {
    assert.match(validateRejectionReason('a'.repeat(256)) ?? '', /255/)
  })
})

describe('parseActionError', () => {
  it('handles fieldErrors for rejectionReason', () => {
    const err = parseActionError({
      status: 400,
      message: 'Validation failed',
      data: null,
      fieldErrors: { rejectionReason: ['Lý do không hợp lệ.'] },
    } as any)
    assert.equal(err.message, 'Lý do không hợp lệ.')
  })

  it('handles 409 conflict error', () => {
    const err = parseActionError({
      status: 409,
      message: 'Conflict',
      data: null,
      fieldErrors: {},
    } as any)
    assert.match(err.message, /đã được xử lý/)
  })
})

describe('the two calls of the audit page', () => {
  it('sends the status of the tab, so each tab asks for its own list', () => {
    assert.deepEqual(ekycQueueRequest(2, 20, 'APPROVED'), { path: '/admin/workers/ekyc-queue', query: { page: 2, pageSize: 20, status: 'APPROVED' } })
    assert.deepEqual(TABS.map((t) => ekycQueueRequest(1, 20, t.status).query.status), ['PENDING', 'APPROVED', 'REJECTED'])
  })

  it('sends the review as the body itself: a wrapped body made the server read approved as false and reject the worker', () => {
    const request = ekycReviewRequest(9, { approved: true, rejectionReason: null })

    assert.equal(request.path, '/admin/workers/9/ekyc-review')
    assert.deepEqual(request.body, { approved: true, rejectionReason: null })
    assert.equal(JSON.stringify(request.body), '{"approved":true,"rejectionReason":null}')
  })
})
