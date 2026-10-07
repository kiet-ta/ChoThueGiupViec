import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  batchRow,
  batchStatusInfo,
  buildError,
  confirmError,
  confirmLines,
  exportFallbackName,
  fileErrorMessage,
  finishedMonths,
  itemRow,
  itemStatusInfo,
  localYearMonth,
  parseFileName,
  payeeTypeLabel,
  warningText,
  periodLabel,
  EXPORTS,
} from '../src/features/payouts/view.ts'
import type { PayoutBatch, PayoutItem } from '../src/features/payouts/types.ts'

const batch = (over: Partial<PayoutBatch> = {}): PayoutBatch => ({
  batchId: 12,
  periodMonth: '2026-10',
  batchStatus: 'DRAFT',
  totalAmount: 1110001,
  itemCount: 3,
  confirmedBy: null,
  confirmedAt: null,
  createdAt: '2026-11-05T04:00:00Z',
  ...over,
})

const item = (over: Partial<PayoutItem> = {}): PayoutItem => ({
  itemId: 1,
  payeeType: 'FREELANCER',
  workerId: 5,
  agencyId: null,
  payeeName: 'Nguyễn Văn A',
  jobCount: 3,
  grossAmount: 624000,
  commissionAmount: 104000,
  penaltyAmount: 50000,
  netAmount: 470000,
  bankName: 'ACB',
  bankAccountNo: '0012345678',
  itemStatus: 'PENDING',
  ...over,
})

describe('months', () => {
  it('reads the month on the Asia/Ho_Chi_Minh calendar, not the UTC one', () => {
    assert.deepEqual(localYearMonth(new Date('2026-10-31T16:59:59Z')), { year: 2026, month: 10 })
    assert.deepEqual(localYearMonth(new Date('2026-10-31T17:00:00Z')), { year: 2026, month: 11 })
    assert.deepEqual(localYearMonth(new Date('2026-12-31T17:00:00Z')), { year: 2027, month: 1 })
  })

  it('offers only finished months, newest first', () => {
    const months = finishedMonths(new Date('2026-11-10T04:00:00Z'), 3)

    assert.deepEqual(months.map((m) => m.value), ['2026-10', '2026-09', '2026-08'])
    assert.equal(months[0].label, 'Tháng 10/2026')
  })

  it('never offers the running month, also just after local midnight when UTC is still the last day', () => {
    assert.equal(finishedMonths(new Date('2026-10-15T04:00:00Z'), 1)[0].value, '2026-09')
    // 2026-10-31 17:30 UTC is already 1 November in Ho Chi Minh: October is over there, so it is offered.
    assert.equal(finishedMonths(new Date('2026-10-31T17:30:00Z'), 1)[0].value, '2026-10')
    // one minute earlier it is still 31 October locally: October is still running.
    assert.equal(finishedMonths(new Date('2026-10-31T16:59:00Z'), 1)[0].value, '2026-09')
  })

  it('crosses the year end and gives twelve months by default', () => {
    const months = finishedMonths(new Date('2027-01-05T04:00:00Z'))

    assert.equal(months.length, 12)
    assert.deepEqual(months.slice(0, 3).map((m) => m.value), ['2026-12', '2026-11', '2026-10'])
    assert.equal(months[11].value, '2026-01')
  })

  it('labels a period and passes a malformed one through', () => {
    assert.equal(periodLabel('2026-01'), 'Tháng 1/2026')
    assert.equal(periodLabel('2026-12'), 'Tháng 12/2026')
    assert.equal(periodLabel('nope'), 'nope')
  })
})

describe('labels', () => {
  it('labels batch and item statuses and payee types', () => {
    assert.deepEqual(batchStatusInfo('DRAFT'), { label: 'Nháp (chưa giải ngân)', tone: 'warning' })
    assert.deepEqual(batchStatusInfo('CLOSED'), { label: 'Đã giải ngân', tone: 'success' })
    assert.deepEqual(batchStatusInfo('X'), { label: 'X', tone: 'neutral' })
    assert.equal(itemStatusInfo('PENDING').label, 'Chờ chuyển')
    assert.equal(itemStatusInfo('TRANSFERRED').label, 'Đã chuyển')
    assert.equal(payeeTypeLabel('FREELANCER'), 'Freelancer')
    assert.equal(payeeTypeLabel('AGENCY'), 'Agency')
  })

  it('offers the three files of the contract with a predictable fallback name', () => {
    assert.deepEqual(EXPORTS.map((e) => e.type), ['freelancer', 'agency-summary', 'agency-detail'])
    assert.equal(exportFallbackName('2026-10', 'agency-detail'), 'payout-2026-10-agency-detail.xlsx')
  })
})

describe('rows', () => {
  it('maps a batch with money and the local confirmation time', () => {
    const row = batchRow(batch({ batchStatus: 'CLOSED', confirmedBy: 3, confirmedAt: '2026-11-06T03:30:00Z' }))

    assert.equal(row.month, 'Tháng 10/2026')
    assert.equal(row.total, '1.110.001 đ')
    assert.equal(row.items, 3)
    assert.equal(row.confirmedAt, '06/11/2026 10:30')
    assert.equal(row.status.label, 'Đã giải ngân')
    assert.equal(row.detailPath, '/admin/payout-batches/12')
  })

  it('shows a dash for a batch that is not confirmed yet', () => {
    assert.equal(batchRow(batch()).confirmedAt, '—')
  })

  it('maps an item with money, the penalty as a deduction and the bank', () => {
    const row = itemRow(item())

    assert.equal(row.type, 'Freelancer')
    assert.equal(row.gross, '624.000 đ')
    assert.equal(row.commission, '104.000 đ')
    assert.equal(row.penalty, '-50.000 đ')
    assert.equal(row.net, '470.000 đ')
    assert.equal(row.bank, 'ACB')
    assert.equal(row.account, '0012345678')
    assert.equal(row.missingAccount, false)
    assert.equal(row.status.label, 'Chờ chuyển')
  })

  it('keeps the leading zeros of an account number as text', () => {
    assert.equal(itemRow(item({ bankAccountNo: '0001234' })).account, '0001234')
  })

  it('flags a payee without an account and shows dashes for a zero penalty and a missing bank', () => {
    const row = itemRow(item({ bankAccountNo: '  ', bankName: null, penaltyAmount: 0 }))

    assert.equal(row.missingAccount, true)
    assert.equal(row.account, 'Chưa có')
    assert.equal(row.bank, '—')
    assert.equal(row.penalty, '—')
  })
})

describe('confirmLines', () => {
  it('states the month, the number of payees, the total and that no bank is called', () => {
    const lines = confirmLines(batch())

    assert.equal(lines[0], 'Tháng 10/2026: 3 người nhận, tổng 1.110.001 đ.')
    assert.match(lines[1], /không chuyển tiền/)
    assert.match(lines[2], /không thể dựng lại/)
  })
})

describe('errors', () => {
  it('explains a build refused for an unfinished month and for a closed batch', () => {
    assert.match(buildError({ status: 400, message: 'Validation failed', data: null, fieldErrors: { periodMonth: ['Only a finished month can be built.'] } }), /đã kết thúc/)
    assert.match(buildError({ status: 400, message: 'x', data: null, fieldErrors: { periodMonth: ['periodMonth must be a month in the form YYYY-MM.'] } }), /không hợp lệ/)
    assert.match(buildError({ status: 409, message: 'x', data: null }), /đã đóng/)
    assert.match(buildError({ status: 0, message: 'x', data: null }), /kết nối/)
    assert.equal(buildError({ status: 500, message: '', data: null }), 'Lỗi 500')
  })

  it('explains the refusals of a confirmation', () => {
    assert.match(confirmError({ status: 404, message: 'x', data: null }), /Không tìm thấy/)
    assert.match(confirmError({ status: 409, message: 'x', data: null }), /đã đóng/)
    assert.match(confirmError({ status: 409, message: 'x', data: null }), /tháng chưa kết thúc/)
    assert.equal(confirmError({ status: 500, message: 'Boom', data: null }), 'Boom')
  })
})

describe('parseFileName', () => {
  it('reads a quoted, an unquoted and an RFC 5987 file name', () => {
    assert.equal(parseFileName('attachment; filename="payout-2026-10-freelancer.xlsx"', 'x.xlsx'), 'payout-2026-10-freelancer.xlsx')
    assert.equal(parseFileName('attachment; filename=payout.xlsx', 'x.xlsx'), 'payout.xlsx')
    assert.equal(parseFileName("attachment; filename*=UTF-8''k%E1%BB%B3-2026.xlsx", 'x.xlsx'), 'kỳ-2026.xlsx')
    assert.equal(parseFileName('attachment; filename="a.xlsx"; filename*=UTF-8\'\'b.xlsx', 'x.xlsx'), 'b.xlsx')
  })

  it('falls back when there is no header, no name, or a name with a path or control characters', () => {
    assert.equal(parseFileName(null, 'fallback.xlsx'), 'fallback.xlsx')
    assert.equal(parseFileName('', 'fallback.xlsx'), 'fallback.xlsx')
    assert.equal(parseFileName('inline', 'fallback.xlsx'), 'fallback.xlsx')
    assert.equal(parseFileName('attachment; filename=""', 'fallback.xlsx'), 'fallback.xlsx')
    assert.equal(parseFileName('attachment; filename="../../etc/passwd"', 'fallback.xlsx'), 'fallback.xlsx')
    assert.equal(parseFileName('attachment; filename="a\\b.xlsx"', 'fallback.xlsx'), 'fallback.xlsx')
    assert.equal(parseFileName('attachment; filename=".."', 'fallback.xlsx'), 'fallback.xlsx')
  })

  it('keeps a malformed percent sequence as it is instead of throwing', () => {
    assert.equal(parseFileName("attachment; filename*=UTF-8''100%.xlsx", 'x.xlsx'), '100%.xlsx')
  })
})

describe('fileErrorMessage', () => {
  it('reads the message of the JSON envelope', () => {
    assert.equal(fileErrorMessage(400, JSON.stringify({ success: false, message: 'Validation failed', data: null })), 'Validation failed')
  })

  it('falls back to a line per status when the body is not JSON or has no message', () => {
    assert.match(fileErrorMessage(400, ''), /Loại file/)
    assert.match(fileErrorMessage(401, '<html>'), /hết hạn/)
    assert.match(fileErrorMessage(403, '{}'), /quyền/)
    assert.match(fileErrorMessage(404, JSON.stringify({ message: '  ' })), /Không tìm thấy/)
    assert.equal(fileErrorMessage(500, 'oops'), 'Không tải được file (lỗi 500).')
  })
})

describe('warningText', () => {
  it('turns the server line about a missing account into Vietnamese and passes other lines through', () => {
    assert.equal(warningText('Trần Thị B has no bank account number.'), 'Trần Thị B: chưa có số tài khoản ngân hàng.')
    assert.equal(warningText('Payee 7 has no bank account number.'), 'Payee 7: chưa có số tài khoản ngân hàng.')
    assert.equal(warningText('Something else'), 'Something else')
  })
})
