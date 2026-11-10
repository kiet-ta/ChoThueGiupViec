import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  batchRow,
  batchStatusInfo,
  closedByText,
  exportFallbackName,
  fileErrorMessage,
  itemRow,
  itemStatusInfo,
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
  advanceAmount: 100000,
  carryInAmount: -20000,
  netAmount: 470000,
  bankName: 'ACB',
  bankAccountNo: '0012345678',
  itemStatus: 'PENDING',
  ...over,
})

describe('months', () => {
  it('labels a period and passes a malformed one through', () => {
    assert.equal(periodLabel('2026-01'), 'Tháng 1/2026')
    assert.equal(periodLabel('2026-12'), 'Tháng 12/2026')
    assert.equal(periodLabel('nope'), 'nope')
  })
})

describe('labels', () => {
  it('labels batch and item statuses and payee types', () => {
    assert.deepEqual(batchStatusInfo('DRAFT'), { label: 'Đang tất toán', tone: 'warning' })
    assert.deepEqual(batchStatusInfo('CLOSED'), { label: 'Đã tất toán', tone: 'success' })
    assert.deepEqual(batchStatusInfo('X'), { label: 'X', tone: 'neutral' })
    assert.equal(itemStatusInfo('PENDING').label, 'Chờ tất toán')
    assert.equal(itemStatusInfo('TRANSFERRED').label, 'Đã tất toán')
    assert.equal(payeeTypeLabel('FREELANCER'), 'Freelancer')
    assert.equal(payeeTypeLabel('AGENCY'), 'Agency')
  })

  it('offers the three files of the contract with a predictable fallback name', () => {
    assert.deepEqual(EXPORTS.map((e) => e.type), ['freelancer', 'agency-summary', 'agency-detail'])
    assert.equal(exportFallbackName('2026-10', 'agency-detail'), 'payout-2026-10-agency-detail.xlsx')
  })
})

describe('rows', () => {
  it('maps a batch with money and who closed it', () => {
    const row = batchRow(batch({ batchStatus: 'CLOSED', confirmedBy: null, confirmedAt: '2026-11-01T17:05:00Z' }))

    assert.equal(row.month, 'Tháng 10/2026')
    assert.equal(row.total, '1.110.001 đ')
    assert.equal(row.items, 3)
    assert.equal(row.closedBy, 'Hệ thống tự chốt lúc 02/11/2026 00:05')
    assert.equal(row.status.label, 'Đã tất toán')
    assert.equal(row.detailPath, '/admin/payout-batches/12')
  })

  it('says the system settled a month without a confirmer, names the Admin of an old confirmation and shows a dash while open', () => {
    assert.equal(closedByText({ confirmedBy: null, confirmedAt: '2026-11-01T17:05:00Z' }), 'Hệ thống tự chốt lúc 02/11/2026 00:05')
    assert.equal(closedByText({ confirmedBy: 3, confirmedAt: '2026-11-06T03:30:00Z' }), 'Admin #3 lúc 06/11/2026 10:30')
    assert.equal(closedByText({ confirmedBy: null, confirmedAt: null }), '—')
    assert.equal(batchRow(batch()).closedBy, '—')
  })

  it('maps an item with money, the penalty and the advance as deductions, the carry-in and the bank', () => {
    const row = itemRow(item())

    assert.equal(row.type, 'Freelancer')
    assert.equal(row.gross, '624.000 đ')
    assert.equal(row.commission, '104.000 đ')
    assert.equal(row.penalty, '-50.000 đ')
    assert.equal(row.advance, '-100.000 đ')
    assert.equal(row.carryIn, '-20.000 đ')
    assert.equal(row.net, '470.000 đ')
    assert.equal(row.bank, 'ACB')
    assert.equal(row.account, '0012345678')
    assert.equal(row.missingAccount, false)
    assert.equal(row.status.label, 'Chờ tất toán')
  })

  it('shows a positive carry-in as it is and dashes when there is no advance or carry-in', () => {
    assert.equal(itemRow(item({ carryInAmount: 100000 })).carryIn, '100.000 đ')
    const none = itemRow(item({ advanceAmount: 0, carryInAmount: 0 }))
    assert.deepEqual([none.advance, none.carryIn], ['—', '—'])
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
