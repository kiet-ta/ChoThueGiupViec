import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  ACTIONS,
  REASON_MAX_LENGTH,
  SEARCH_MAX_LENGTH,
  SEARCH_PAGE_SIZE,
  actionError,
  actionInfo,
  actionRequest,
  kycStatusText,
  parseWorkerId,
  ratingText,
  reasonText,
  resultsSummary,
  searchError,
  searchQuery,
  searchTextProblem,
  selectionMatches,
  shouldSearch,
  successText,
  validateReason,
  workStatusText,
  workerDetails,
  workerIdFromSearch,
  workerTitle,
  workerTypeText,
} from '../src/features/admin/worker-controls/view.ts'
import type { AdminWorker } from '../src/features/admin/worker-controls/types.ts'

describe('the three actions', () => {
  it('are approve, revoke and unlock, each with a hint and an effect an Admin reads before confirming', () => {
    assert.deepEqual(ACTIONS.map((a) => a.value), ['APPROVE_SUPER', 'REVOKE_SUPER', 'UNLOCK'])
    for (const a of ACTIONS) {
      assert.ok(a.label.length > 0 && a.hint.length > 0 && a.effect.length > 0, a.value)
    }
    assert.equal(actionInfo('UNLOCK').label, 'Mở khoá thợ')
  })
})

describe('parseWorkerId', () => {
  it('accepts a whole number of at least 1, trimmed', () => {
    assert.deepEqual(parseWorkerId(' 12 '), { id: 12 })
    assert.deepEqual(parseWorkerId('1'), { id: 1 })
    assert.deepEqual(parseWorkerId('2147483647'), { id: 2147483647 })
  })

  it('refuses empty, zero, negative, decimal, text and numbers beyond an int', () => {
    for (const bad of ['', '   ', '0', '-3', '1.5', '12a', 'abc', '2147483648', '99999999999999999999']) {
      assert.ok('error' in parseWorkerId(bad), `"${bad}" must be refused`)
    }
  })
})

describe('validateReason', () => {
  it('needs 1 to 255 characters after trimming, with the boundaries', () => {
    assert.match(validateReason('') ?? '', /nhập lý do/)
    assert.match(validateReason('   ') ?? '', /nhập lý do/)
    assert.equal(validateReason('a'), null)
    assert.equal(validateReason('a'.repeat(REASON_MAX_LENGTH)), null)
    assert.match(validateReason('a'.repeat(REASON_MAX_LENGTH + 1)) ?? '', /255/)
    assert.equal(validateReason('  ' + 'a'.repeat(REASON_MAX_LENGTH) + '  '), null) // trimmed first
  })
})

describe('actionRequest (contract admin.md 2.5 and 2.6)', () => {
  it('approve is a POST and revoke is a DELETE on the super-freelancer path, unlock a POST on the unlock path', () => {
    assert.deepEqual(actionRequest('APPROVE_SUPER', 5, ' top rated '), {
      method: 'POST',
      path: '/admin/workers/5/super-freelancer',
      body: { reason: 'top rated' },
    })
    assert.deepEqual(actionRequest('REVOKE_SUPER', 5, 'r'), { method: 'DELETE', path: '/admin/workers/5/super-freelancer', body: { reason: 'r' } })
    assert.deepEqual(actionRequest('UNLOCK', 12, 'warning served'), {
      method: 'POST',
      path: '/admin/workers/12/unlock',
      body: { reason: 'warning served' },
    })
  })

  it('always sends the reason as the body, also on a DELETE (the server refuses a revoke without it)', () => {
    assert.equal(JSON.stringify(actionRequest('REVOKE_SUPER', 1, ' x ').body), '{"reason":"x"}')
  })
})

describe('actionError', () => {
  const call = (status: number, data: unknown = null, fieldErrors: Record<string, string[]> = {}) => ({ status, message: 'm', data, fieldErrors })

  it('turns every failed criterion of an approval into words', () => {
    const e = actionError(
      'APPROVE_SUPER',
      call(409, { failedCriteria: ['RATING_BELOW_MINIMUM', 'COMPLETED_JOBS_BELOW_MINIMUM', 'KYC_NOT_APPROVED', 'UPHELD_DISPUTE_RECENT'] }),
    )
    assert.match(e.message, /Chưa đủ điều kiện/)
    assert.equal(e.reasons.length, 4)
    assert.match(e.reasons[0], /4,80/)
    assert.match(e.reasons[1], /50 ca/)
    assert.match(e.reasons[3], /180 ngày/)
  })

  it('turns the block reasons of an unlock into words and says the unlock is what was refused', () => {
    const e = actionError('UNLOCK', call(409, { blockReasons: ['NOT_LOCKED', 'KYC_NOT_APPROVED', 'NOT_FREELANCER'] }))
    assert.match(e.message, /Chưa thể mở khoá/)
    assert.equal(e.reasons.length, 3)
    assert.match(e.reasons[0], /không bị khoá/)
    assert.match(e.reasons[1], /eKYC/)
  })

  it('shows an unknown code as it is, and a 409 without codes in one plain line', () => {
    assert.equal(reasonText('SOMETHING_NEW'), 'SOMETHING_NEW')
    assert.match(actionError('UNLOCK', call(409)).message, /trạng thái của thợ/)
    assert.deepEqual(actionError('UNLOCK', call(409)).reasons, [])
  })

  it('maps 400 to the reason field message, 404, a network failure and anything else', () => {
    assert.equal(actionError('UNLOCK', call(400, null, { reason: ['A reason is required.'] })).message, 'A reason is required.')
    assert.match(actionError('UNLOCK', call(404)).message, /Không tìm thấy thợ/)
    assert.match(actionError('UNLOCK', call(0)).message, /Không kết nối/)
    assert.equal(actionError('UNLOCK', { status: 500, message: '', data: null }).message, 'Lỗi 500')
  })
})

describe('successText', () => {
  it('names the worker and the result', () => {
    assert.match(successText('APPROVE_SUPER', 7), /thợ #7.*Super-Freelancer/)
    assert.match(successText('REVOKE_SUPER', 7), /thu hồi.*#7/)
    assert.match(successText('UNLOCK', 7), /mở khoá thợ #7/)
  })
})

const worker = (over: Partial<AdminWorker> = {}): AdminWorker => ({
  workerId: 12,
  fullName: 'Tran Van A',
  phoneNumber: '0901234567',
  nationalId: '079000000001',
  workerType: 'FREELANCER',
  agencyId: null,
  agencyName: null,
  workStatus: 'LOCKED',
  kycStatus: 'APPROVED',
  isSuperFreelancer: false,
  ratingAvg: 4.85,
  completedJobs: 61,
  ...over,
})

describe('the worker lookup (ADMIN-UAT-14, contract admin.md 2.8)', () => {
  it('searchQuery trims the text, drops an empty one and asks for the first short page', () => {
    assert.deepEqual(searchQuery('  Tran Van A '), { q: 'Tran Van A', page: 1, pageSize: SEARCH_PAGE_SIZE })
    assert.deepEqual(searchQuery('12'), { q: '12', page: 1, pageSize: SEARCH_PAGE_SIZE })
    assert.equal(searchQuery('   ').q, undefined)
    assert.ok(SEARCH_PAGE_SIZE >= 1 && SEARCH_PAGE_SIZE <= 50) // the server accepts 1 to 50
  })

  it('shouldSearch needs some text and at most the length the server accepts', () => {
    assert.equal(shouldSearch(''), false)
    assert.equal(shouldSearch('   '), false)
    assert.equal(shouldSearch('a'), true)
    assert.equal(shouldSearch('a'.repeat(SEARCH_MAX_LENGTH)), true)
    assert.equal(shouldSearch('a'.repeat(SEARCH_MAX_LENGTH + 1)), false)
    assert.equal(searchTextProblem('a'.repeat(SEARCH_MAX_LENGTH + 1)), `Tối đa ${SEARCH_MAX_LENGTH} ký tự (đang ${SEARCH_MAX_LENGTH + 1}).`)
    assert.equal(searchTextProblem('ok'), null)
  })

  it('names the work status, the eKYC status and the worker type in Vietnamese and shows an unknown value as it is', () => {
    assert.equal(workStatusText('LOCKED'), 'Đang bị khoá')
    assert.equal(workStatusText('IDLE'), 'Sẵn sàng')
    assert.equal(workStatusText('SOMETHING_NEW'), 'SOMETHING_NEW')
    assert.equal(kycStatusText('MANUAL_REVIEW'), 'eKYC chờ hậu kiểm')
    assert.equal(kycStatusText('PENDING'), 'Chưa nộp eKYC')
    assert.equal(kycStatusText('X'), 'X')
    assert.equal(workerTypeText(worker()), 'Freelancer')
    assert.equal(workerTypeText(worker({ workerType: 'AGENCY_STAFF', agencyName: 'Cong ty A' })), 'Thợ Agency (Cong ty A)')
    assert.equal(workerTypeText(worker({ workerType: 'AGENCY_STAFF', agencyName: null })), 'Thợ Agency')
  })

  it('writes the rating with a decimal comma and says so when there is none yet', () => {
    assert.equal(ratingText(4.85), '4,85 sao')
    assert.equal(ratingText(5), '5,00 sao')
    assert.equal(ratingText(0), 'Chưa có đánh giá')
  })

  it('titles a worker with the number the form needs and lists what an Admin checks before acting', () => {
    assert.equal(workerTitle(worker()), '#12 Tran Van A')
    assert.deepEqual(workerDetails(worker()), [
      { label: 'Số điện thoại', value: '0901234567' },
      { label: 'CCCD', value: '079000000001' },
      { label: 'Loại thợ', value: 'Freelancer' },
      { label: 'Trạng thái', value: 'Đang bị khoá' },
      { label: 'eKYC', value: 'eKYC đã duyệt' },
      { label: 'Super-Freelancer', value: 'Không' },
      { label: 'Đánh giá', value: '4,85 sao' },
      { label: 'Ca hoàn thành', value: '61' },
    ])
    assert.equal(workerDetails(worker({ isSuperFreelancer: true })).find((l) => l.label === 'Super-Freelancer')?.value, 'Có')
  })

  it('resultsSummary says none matched, all matched, or that typing more narrows a long list', () => {
    assert.equal(resultsSummary(0, 0), 'Không tìm thấy thợ nào khớp.')
    assert.equal(resultsSummary(3, 3), '3 thợ khớp.')
    assert.equal(resultsSummary(8, 40), 'Hiện 8 trên 40 thợ khớp. Gõ thêm để thu hẹp.')
  })

  it('searchError maps the failures of the lookup: network, 400 on q, 404, anything else', () => {
    assert.equal(searchError({ status: 0, message: '', data: null }), 'Không kết nối được máy chủ.')
    assert.equal(searchError({ status: 400, message: 'Validation failed', data: null, fieldErrors: { q: ['Too long.'] } }), 'Too long.')
    assert.equal(searchError({ status: 400, message: 'Validation failed', data: null }), 'Validation failed')
    assert.equal(searchError({ status: 404, message: '', data: null }), 'Chưa có chức năng tìm thợ trên máy chủ.')
    assert.equal(searchError({ status: 403, message: 'Forbidden', data: null }), 'Forbidden')
    assert.equal(searchError({ status: 500, message: '', data: null }), 'Lỗi 500')
  })

  it('workerIdFromSearch takes ?workerId= only when it is a worker number', () => {
    assert.equal(workerIdFromSearch('?workerId=12'), '12')
    assert.equal(workerIdFromSearch('workerId=7&x=1'), '7')
    assert.equal(workerIdFromSearch('?workerId= 5 '), '5')
    assert.equal(workerIdFromSearch(''), '')
    assert.equal(workerIdFromSearch('?workerId=abc'), '')
    assert.equal(workerIdFromSearch('?workerId=0'), '')
    assert.equal(workerIdFromSearch('?other=12'), '')
  })

  it('selectionMatches keeps the selected worker only while its number is in the field', () => {
    assert.equal(selectionMatches(worker(), '12'), true)
    assert.equal(selectionMatches(worker(), ' 12 '), true)
    assert.equal(selectionMatches(worker(), '13'), false)
    assert.equal(selectionMatches(worker(), ''), false)
    assert.equal(selectionMatches(null, '12'), false)
  })
})
