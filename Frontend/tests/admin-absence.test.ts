import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  actionError,
  blockReasonText,
  distanceText,
  moneySplitText,
  reportRow,
  statusInfo,
  TABS,
  validateReason,
} from '../src/features/admin/absence/view.ts'
import type { AbsenceReport } from '../src/features/admin/absence/types.ts'

const report = (over: Partial<AbsenceReport> = {}): AbsenceReport => ({
  assignmentId: 70,
  orderId: 970,
  orderCode: 'ORD970',
  workerId: 40,
  workerName: 'Lý Văn Phúc',
  workerType: 'AGENCY_STAFF',
  agencyName: 'CleanPro Solutions',
  customerName: 'Chị Ngọc Anh',
  status: 'PENDING',
  checkedInAt: '2026-10-07T03:00:00Z',
  customerAbsentAt: '2026-10-07T03:20:00Z',
  gpsVerified: true,
  distanceM: 12.5,
  deviceLat: 10.76,
  deviceLng: 106.66,
  callAttempts: 2,
  waitedMinutes: 20,
  grossAmount: 260000,
  absenceFeeAmount: 104000,
  customerRefundAmount: 156000,
  photoUrl: null,
  canApprove: true,
  blockReasons: [],
  ...over,
})

describe('tabs and statuses', () => {
  it('has the three statuses of the contract in the order of the design', () => {
    assert.deepEqual(TABS.map((t) => t.status), ['PENDING', 'APPROVED', 'REJECTED'])
    assert.deepEqual(TABS.map((t) => t.label), ['Chờ duyệt', 'Đã duyệt', 'Từ chối'])
  })

  it('gives each status a label and a tone, and passes an unknown one through', () => {
    assert.deepEqual(statusInfo('PENDING'), { label: 'Chờ duyệt', tone: 'warning' })
    assert.deepEqual(statusInfo('APPROVED'), { label: 'Đã duyệt', tone: 'success' })
    assert.deepEqual(statusInfo('REJECTED'), { label: 'Đã từ chối', tone: 'danger' })
    assert.deepEqual(statusInfo('SOMETHING'), { label: 'SOMETHING', tone: 'neutral' })
  })
})

describe('blockReasonText', () => {
  it('explains the four reasons of the contract in words', () => {
    for (const code of ['GPS_NOT_VERIFIED', 'CALLS_BELOW_MINIMUM', 'WAIT_BELOW_MINIMUM', 'ABSENCE_NOT_REPORTED']) {
      assert.notEqual(blockReasonText(code), code)
    }
    assert.match(blockReasonText('CALLS_BELOW_MINIMUM'), /2 cuộc/)
    assert.match(blockReasonText('WAIT_BELOW_MINIMUM'), /15 phút/)
  })

  it('shows an unknown code as it is instead of hiding it', () => {
    assert.equal(blockReasonText('NEW_RULE'), 'NEW_RULE')
  })
})

describe('reportRow', () => {
  it('maps a report to the table row (local time, GPS, fee)', () => {
    const row = reportRow(report())

    assert.equal(row.orderCode, 'ORD970')
    assert.equal(row.workerKind, 'Agency · CleanPro Solutions')
    assert.equal(row.reportedAt, '07/10/2026 10:20') // 03:20Z is 10:20 in Ho Chi Minh
    assert.equal(row.gpsText, 'Đúng vị trí · 12.5 m')
    assert.equal(row.gpsOk, true)
    assert.equal(row.calls, 2)
    assert.equal(row.waitedText, '20 phút')
    assert.equal(row.feeText, '104.000 đ')
    assert.deepEqual(row.status, { label: 'Chờ duyệt', tone: 'warning' })
  })

  it('says GPS is not verified when it is not, and shows a freelancer without an agency', () => {
    const row = reportRow(report({ gpsVerified: false, distanceM: 350, workerType: 'FREELANCER', agencyName: null }))

    assert.equal(row.gpsText, 'Chưa xác minh · 350 m')
    assert.equal(row.gpsOk, false)
    assert.equal(row.workerKind, 'Freelancer')
  })
})

describe('distanceText', () => {
  it('uses metres below a kilometre and kilometres above', () => {
    assert.equal(distanceText(0), '0 m')
    assert.equal(distanceText(12.5), '12.5 m')
    assert.equal(distanceText(100), '100 m')
    assert.equal(distanceText(999), '999 m')
    assert.equal(distanceText(1000), '1.0 km')
    assert.equal(distanceText(1234), '1.2 km')
  })
})

describe('moneySplitText', () => {
  it('states what the worker gets and what goes back to the customer', () => {
    assert.equal(moneySplitText(report()), 'Thợ nhận 104.000 đ · hoàn 156.000 đ cho khách')
  })

  it('adds up to the job value for an amount that does not divide evenly', () => {
    const r = report({ grossAmount: 260001, absenceFeeAmount: 104000, customerRefundAmount: 156001 })
    assert.equal(r.absenceFeeAmount + r.customerRefundAmount, r.grossAmount)
    assert.equal(moneySplitText(r), 'Thợ nhận 104.000 đ · hoàn 156.001 đ cho khách')
  })
})

describe('validateReason', () => {
  it('asks for a reason when it is empty or only spaces', () => {
    assert.match(validateReason('') ?? '', /nhập lý do/)
    assert.match(validateReason('   \n ') ?? '', /nhập lý do/)
  })

  it('accepts 1 and 255 characters, counted after trimming', () => {
    assert.equal(validateReason('x'), null)
    assert.equal(validateReason('x'.repeat(255)), null)
    assert.equal(validateReason(`  ${'x'.repeat(255)}  `), null)
  })

  it('refuses 256 characters and says how many there are', () => {
    assert.match(validateReason('x'.repeat(256)) ?? '', /255/)
    assert.match(validateReason('x'.repeat(256)) ?? '', /256/)
  })
})

describe('actionError', () => {
  it('lists the block reasons of a 409 refusal in words', () => {
    const err = actionError({
      status: 409,
      message: 'The absence cannot be approved yet.',
      data: { blockReasons: ['GPS_NOT_VERIFIED', 'WAIT_BELOW_MINIMUM'] },
    })

    assert.equal(err.message, 'Chưa đủ điều kiện để duyệt:')
    assert.equal(err.reasons.length, 2)
    assert.equal(err.reasons[0], blockReasonText('GPS_NOT_VERIFIED'))
  })

  it('explains a 409 without reasons as already decided or changed', () => {
    const err = actionError({ status: 409, message: 'The report is already decided.', data: null })

    assert.match(err.message, /đã được xử lý/)
    assert.deepEqual(err.reasons, [])
  })

  it('uses the field message of a 400 and falls back to the server message', () => {
    assert.equal(actionError({ status: 400, message: 'Validation failed', data: null, fieldErrors: { reason: ['A reason is required.'] } }).message, 'A reason is required.')
    assert.equal(actionError({ status: 400, message: 'Validation failed', data: null, fieldErrors: {} }).message, 'Validation failed')
  })

  it('says that nothing changed after a refused refund (502)', () => {
    const err = actionError({ status: 502, message: 'The wallet is closed.', data: null })

    assert.match(err.message, /Chưa có gì thay đổi/)
  })

  it('handles 404, a network failure and any other status', () => {
    assert.match(actionError({ status: 404, message: 'x', data: null }).message, /Không tìm thấy/)
    assert.match(actionError({ status: 0, message: 'x', data: null }).message, /kết nối/)
    assert.equal(actionError({ status: 500, message: 'Boom', data: null }).message, 'Boom')
    assert.equal(actionError({ status: 500, message: '', data: null }).message, 'Lỗi 500')
  })

  it('ignores a malformed blockReasons value', () => {
    assert.deepEqual(actionError({ status: 409, message: 'x', data: { blockReasons: 'GPS_NOT_VERIFIED' } }).reasons, [])
    assert.deepEqual(actionError({ status: 409, message: 'x', data: { blockReasons: [1, null, 'GPS_NOT_VERIFIED'] } }).reasons, [blockReasonText('GPS_NOT_VERIFIED')])
  })
})
