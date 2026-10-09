import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  confidenceInfo,
  ekycQueueRow,
  parseActionError,
  validateRejectionReason,
  TABS,
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
