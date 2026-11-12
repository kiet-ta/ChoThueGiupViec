import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  BANK_CHANGE_TABS,
  accountText,
  approveConfirmText,
  bankChangeActionError,
  bankChangeQuery,
  bankChangeRow,
  bankChangeStatusInfo,
} from '../src/features/admin/bank-changes/view.ts'
import type { BankChangeRequest } from '../src/features/admin/bank-changes/types.ts'

const request = (over: Partial<BankChangeRequest> = {}): BankChangeRequest => ({
  requestId: 5,
  workerId: 3,
  workerName: 'Võ Thanh Hạnh',
  workerPhone: '0977100003',
  previousBankName: 'ACB',
  previousBankAccountNo: '19100111',
  bankName: 'Techcombank',
  bankAccountNo: '8800770066',
  status: 'PENDING',
  requestedAt: '2026-11-12T18:10:30Z',
  reviewedAt: null,
  rejectReason: null,
  ...over,
})

const failure = (status: number, code?: string, message = '', fieldErrors?: Record<string, string[]>) => ({
  status,
  message,
  data: code ? { code } : null,
  fieldErrors,
})

describe('the page that approves payout account changes (LEDGER-29)', () => {
  it('has the three tabs in the order the Admin works through them', () => {
    assert.deepEqual(BANK_CHANGE_TABS.map((t) => t.value), ['PENDING', 'APPROVED', 'REJECTED'])
    assert.deepEqual(BANK_CHANGE_TABS.map((t) => t.label), ['Chờ duyệt', 'Đã duyệt', 'Bị từ chối'])
  })

  it('names the status and gives it a tone', () => {
    assert.deepEqual(bankChangeStatusInfo('PENDING'), { label: 'Chờ duyệt', tone: 'warning' })
    assert.deepEqual(bankChangeStatusInfo('APPROVED'), { label: 'Đã duyệt', tone: 'success' })
    assert.deepEqual(bankChangeStatusInfo('REJECTED'), { label: 'Bị từ chối', tone: 'danger' })
    assert.deepEqual(bankChangeStatusInfo('SOMETHING'), { label: 'SOMETHING', tone: 'neutral' })
  })

  it('writes an account as bank and number', () => {
    assert.equal(accountText('ACB', '19100111'), 'ACB · 19100111')
    assert.equal(accountText(' ACB ', null), 'ACB')
    assert.equal(accountText(null, null), 'Chưa có tài khoản')
  })

  it('shows the old and the new account side by side and only a pending row has actions', () => {
    const row = bankChangeRow(request())
    assert.equal(row.worker, 'Võ Thanh Hạnh')
    assert.equal(row.phone, '0977100003')
    assert.equal(row.previous, 'ACB · 19100111')
    assert.equal(row.next, 'Techcombank · 8800770066')
    assert.equal(row.pending, true)
    assert.equal(row.detail, '')
    assert.equal(bankChangeRow(request({ status: 'APPROVED', reviewedAt: '2026-11-13T01:00:00Z' })).pending, false)
  })

  it('says why a request was rejected and when one was approved', () => {
    assert.equal(bankChangeRow(request({ status: 'REJECTED', rejectReason: 'Sai tên chủ tài khoản' })).detail, 'Sai tên chủ tài khoản')
    assert.match(bankChangeRow(request({ status: 'APPROVED', reviewedAt: '2026-11-12T18:10:31Z' })).detail, /^Duyệt lúc /)
  })

  it('falls back to the worker number and a dash when the name or phone are missing', () => {
    const row = bankChangeRow(request({ workerName: ' ', workerPhone: '' }))
    assert.equal(row.worker, 'Thợ #3')
    assert.equal(row.phone, '—')
  })

  it('the approval step names both accounts and says the lock is 15 days from the day the request was sent', () => {
    const text = approveConfirmText(bankChangeRow(request()))
    assert.match(text, /Võ Thanh Hạnh/)
    assert.match(text, /ACB · 19100111/)
    assert.match(text, /Techcombank · 8800770066/)
    assert.match(text, /15 ngày/)
  })

  it('translates the answers of the API', () => {
    assert.match(bankChangeActionError(failure(409, 'BANK_CHANGE_NOT_PENDING')), /đã được xử lý/)
    assert.match(bankChangeActionError(failure(409, 'LEDGER_BUSY')), /đang bận/)
    assert.equal(bankChangeActionError(failure(409, 'OTHER', 'Lý do khác')), 'Lý do khác')
    assert.equal(bankChangeActionError(failure(400, undefined, '', { reason: ['Hãy nhập lý do từ chối.'] })), 'Hãy nhập lý do từ chối.')
    assert.equal(bankChangeActionError(failure(404)), 'Không tìm thấy đơn này.')
    assert.equal(bankChangeActionError(failure(0)), 'Không kết nối được máy chủ.')
    assert.equal(bankChangeActionError(failure(500, undefined, 'Lỗi máy chủ')), 'Lỗi máy chủ')
  })

  it('asks the API for the tab and the page and sends no order parameters', () => {
    assert.deepEqual(bankChangeQuery('PENDING', 1, 20), { status: 'PENDING', page: 1, pageSize: 20 })
    assert.deepEqual(bankChangeQuery('REJECTED', 3, 20), { status: 'REJECTED', page: 3, pageSize: 20 })
  })
})
