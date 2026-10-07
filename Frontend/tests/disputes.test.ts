import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  categoryLabel,
  EMPTY_FORM,
  effectText,
  faultLabel,
  faultOptions,
  parseAmount,
  photoAssignmentIds,
  priorityInfo,
  queueRow,
  safeEvidenceUrl,
  resolveRequest,
  statusInfo,
  timelineRows,
  toComparePhotos,
  validateResolve,
  verdictError,
  type ResolveForm,
} from '../src/features/disputes/view.ts'
import type { CasePhoto, DisputeSummary, DisputeWorker, TimelineEntry } from '../src/features/disputes/types.ts'

const freelancer: DisputeWorker = { workerId: 1, fullName: 'Phạm Hoàng Nam', workerType: 'FREELANCER', agencyName: null }
const staff: DisputeWorker = { workerId: 2, fullName: 'Lý Văn Phúc', workerType: 'AGENCY_STAFF', agencyName: 'CleanPro Solutions' }

const summary = (over: Partial<DisputeSummary> = {}): DisputeSummary => ({
  disputeId: 1042,
  orderId: 42,
  orderCode: 'ORD1042',
  customerName: 'Chị Ngọc Anh',
  workers: [staff],
  raisedBy: 'CUSTOMER',
  category: 'QUALITY',
  priority: 'HIGH',
  slaDueAt: '2026-10-07T07:00:00Z',
  slaSecondsRemaining: 11520,
  disputeStatus: 'IN_REVIEW',
  autoCancelled: false,
  ...over,
})

const form = (over: Partial<ResolveForm>): ResolveForm => ({ ...EMPTY_FORM, note: 'Đã đối soát ảnh', ...over })

describe('labels', () => {
  it('labels priorities and statuses and passes unknown values through', () => {
    assert.deepEqual(priorityInfo('HIGH'), { label: 'Cao', tone: 'danger' })
    assert.deepEqual(priorityInfo('MEDIUM'), { label: 'Trung bình', tone: 'warning' })
    assert.deepEqual(priorityInfo('LOW'), { label: 'Thấp', tone: 'neutral' })
    assert.deepEqual(priorityInfo('X'), { label: 'X', tone: 'neutral' })
    assert.equal(statusInfo('OPEN').label, 'Chờ tiếp nhận')
    assert.equal(statusInfo('IN_REVIEW').label, 'Đang xử lý')
    assert.equal(statusInfo('RESOLVED').label, 'Đã phán quyết')
    assert.equal(statusInfo('DISMISSED').label, 'Đã bác bỏ')
    assert.equal(statusInfo('?').label, '?')
  })

  it('names the five categories of the contract and the verdicts', () => {
    assert.equal(categoryLabel('QUALITY'), 'Chất lượng kém')
    assert.equal(categoryLabel('ATTITUDE'), 'Thái độ')
    assert.equal(categoryLabel('PROPERTY_DAMAGE'), 'Hư hỏng tài sản')
    assert.equal(categoryLabel('ABSENT_FEE'), 'Phí vắng mặt')
    assert.equal(categoryLabel('OTHER'), 'Khác')
    assert.equal(categoryLabel('NEW'), 'NEW')
    assert.equal(faultLabel('FREELANCER'), 'Lỗi thuộc Freelancer')
    assert.equal(faultLabel('AGENCY'), 'Lỗi thuộc Agency')
    assert.equal(faultLabel('CUSTOMER'), 'Lỗi thuộc khách hàng')
    assert.equal(faultLabel(null), 'Không bên nào có lỗi')
  })
})

describe('queueRow', () => {
  it('maps an open ticket the way the design shows it', () => {
    const row = queueRow(summary())

    assert.equal(row.code, '#TC-1042')
    assert.deepEqual(row.workers, [{ name: 'Lý Văn Phúc', kind: 'Agency · CleanPro Solutions' }])
    assert.equal(row.category, 'Chất lượng kém')
    assert.equal(row.autoTag, false)
    assert.equal(row.slaText, '03h 12p')
    assert.equal(row.overdue, false)
    assert.deepEqual(row.status, { label: 'Đang xử lý', tone: 'warning' })
    assert.equal(row.detailPath, '/admin/disputes/1042')
  })

  it('marks an overdue open ticket and the absence-fee dispute tag', () => {
    const row = queueRow(summary({ slaSecondsRemaining: -900, category: 'ABSENT_FEE', autoCancelled: true, disputeStatus: 'OPEN' }))

    assert.equal(row.overdue, true)
    assert.equal(row.slaText, 'Quá hạn 00h 15p')
    assert.equal(row.autoTag, true)
  })

  it('shows a dash instead of a countdown for a decided ticket', () => {
    const row = queueRow(summary({ disputeStatus: 'RESOLVED', slaSecondsRemaining: 0 }))

    assert.equal(row.slaText, '—')
    assert.equal(row.overdue, false)
  })
})

describe('timelineRows', () => {
  const entries: TimelineEntry[] = [
    { at: '2026-10-07T01:58:00Z', type: 'CHECK_IN', detail: 'Check-in at 12.5 m from the address', gpsVerified: true, distanceM: 12.5 },
    { at: '2026-10-07T02:35:00Z', type: 'PHOTO_AFTER', detail: 'After photo, angle 1, accepted', volScore: 82.4 },
    { at: '2026-10-07T02:36:00Z', type: 'CUSTOMER_DISPUTED', detail: 'Dispute filed by CUSTOMER' },
    { at: '2026-10-07T02:41:00Z', type: 'CHECK_OUT', detail: 'Job completed' },
  ]

  it('labels each event, shows local time and the GPS or VoL badge', () => {
    const rows = timelineRows(entries)

    assert.deepEqual(rows.map((r) => r.label), ['Check-in', 'Ảnh After', 'Khiếu nại', 'Hoàn tất ca'])
    assert.equal(rows[0].time, '07/10/2026 08:58')
    assert.deepEqual(rows[0].badge, { text: 'GPS ✓', tone: 'success' })
    assert.deepEqual(rows[1].badge, { text: 'VoL 82', tone: 'info' })
    assert.equal(rows[2].badge, null)
    assert.equal(rows[3].badge, null)
  })

  it('shows a failed GPS check and keeps every key unique', () => {
    const rows = timelineRows([{ ...entries[0], gpsVerified: false }, { ...entries[0], gpsVerified: false }])

    assert.deepEqual(rows[0].badge, { text: 'GPS ✗', tone: 'danger' })
    assert.notEqual(rows[0].key, rows[1].key)
  })

  it('shows no GPS badge when the check-in has no result and passes an unknown type through', () => {
    const rows = timelineRows([{ at: '2026-10-07T01:58:00Z', type: 'CHECK_IN', detail: '', gpsVerified: null }, { at: '2026-10-07T01:58:00Z', type: 'X', detail: 'd' }])

    assert.equal(rows[0].badge, null)
    assert.equal(rows[1].label, 'X')
  })

  it('is empty for no events', () => {
    assert.deepEqual(timelineRows([]), [])
  })
})

describe('photos', () => {
  const photo = (assignmentId: number, phase: string, angleNo: number, accepted = true): CasePhoto => ({
    assignmentId,
    phase,
    angleNo,
    url: `/p/${assignmentId}-${phase}-${angleNo}.jpg`,
    volScore: 90,
    isAccepted: accepted,
  })

  it('lists the assignments that have photos once each, ascending', () => {
    assert.deepEqual(photoAssignmentIds([photo(9, 'AFTER', 1), photo(3, 'BEFORE', 1), photo(9, 'BEFORE', 1)]), [3, 9])
    assert.deepEqual(photoAssignmentIds([]), [])
  })

  it('splits one assignment into before and after in the viewer shape', () => {
    const photos = [photo(3, 'BEFORE', 1), photo(3, 'AFTER', 1, false), photo(9, 'AFTER', 2), photo(3, 'AFTER', 2)]

    const { before, after } = toComparePhotos(photos, 3)

    assert.deepEqual(before, [{ angleNo: 1, url: '/p/3-BEFORE-1.jpg', volScore: 90, isAccepted: true }])
    assert.deepEqual(after.map((p) => [p.angleNo, p.isAccepted]), [[1, false], [2, true]])
  })

  it('gives empty lists for an assignment without photos', () => {
    assert.deepEqual(toComparePhotos([photo(3, 'BEFORE', 1)], 7), { before: [], after: [] })
  })
})

describe('faultOptions', () => {
  it('offers FREELANCER only when the order has a freelancer and AGENCY only when it has agency staff', () => {
    assert.deepEqual(faultOptions([freelancer]).map((o) => o.value), ['FREELANCER', 'CUSTOMER', 'DISMISS'])
    assert.deepEqual(faultOptions([staff]).map((o) => o.value), ['AGENCY', 'CUSTOMER', 'DISMISS'])
    assert.deepEqual(faultOptions([freelancer, staff]).map((o) => o.value), ['FREELANCER', 'AGENCY', 'CUSTOMER', 'DISMISS'])
  })

  it('always offers the customer and a dismissal, even for an order with no workers', () => {
    assert.deepEqual(faultOptions([]).map((o) => o.value), ['CUSTOMER', 'DISMISS'])
  })
})

describe('parseAmount', () => {
  it('reads a whole number, tolerating dots and spaces as thousands separators; empty is 0', () => {
    assert.equal(parseAmount(''), 0)
    assert.equal(parseAmount('  '), 0)
    assert.equal(parseAmount('0'), 0)
    assert.equal(parseAmount('50000'), 50000)
    assert.equal(parseAmount('50.000'), 50000)
    assert.equal(parseAmount('1 250 000'), 1250000)
  })

  it('rejects anything that is not a whole non-negative number', () => {
    assert.equal(parseAmount('-1'), null)
    assert.equal(parseAmount('12,5'), null)
    assert.equal(parseAmount('abc'), null)
    assert.equal(parseAmount('1e3'), null)
    assert.equal(parseAmount('99999999999999999999'), null)
  })
})

describe('validateResolve', () => {
  const both = [freelancer, staff]

  it('needs a verdict, and one the order allows', () => {
    assert.ok(validateResolve(form({ fault: '' }), both).faultParty)
    assert.ok(validateResolve(form({ fault: 'AGENCY' }), [freelancer]).faultParty)
    assert.equal(validateResolve(form({ fault: 'AGENCY' }), both).faultParty, undefined)
  })

  it('accepts a freelancer verdict with money and a lock, and an agency verdict with money', () => {
    assert.deepEqual(validateResolve(form({ fault: 'FREELANCER', amount: '100000', lockWorker: true }), both), {})
    assert.deepEqual(validateResolve(form({ fault: 'AGENCY', amount: '100.000' }), both), {})
  })

  it('refuses money for a customer verdict and a dismissal (disputes.md 2.3)', () => {
    assert.ok(validateResolve(form({ fault: 'CUSTOMER', amount: '1' }), both).compensationAmount)
    assert.ok(validateResolve(form({ fault: 'DISMISS', amount: '1' }), both).compensationAmount)
    assert.deepEqual(validateResolve(form({ fault: 'CUSTOMER', amount: '0' }), both), {})
    assert.deepEqual(validateResolve(form({ fault: 'DISMISS', amount: '' }), both), {})
  })

  it('refuses a fractional or negative amount', () => {
    assert.ok(validateResolve(form({ fault: 'FREELANCER', amount: '10,5' }), both).compensationAmount)
    assert.ok(validateResolve(form({ fault: 'FREELANCER', amount: '-5' }), both).compensationAmount)
  })

  it('allows the lock only with a freelancer verdict', () => {
    assert.ok(validateResolve(form({ fault: 'AGENCY', lockWorker: true }), both).lockWorker)
    assert.ok(validateResolve(form({ fault: 'CUSTOMER', lockWorker: true }), both).lockWorker)
    assert.ok(validateResolve(form({ fault: 'DISMISS', lockWorker: true }), both).lockWorker)
  })

  it('needs a note of 1 to 255 characters once trimmed', () => {
    assert.ok(validateResolve(form({ fault: 'CUSTOMER', note: '' }), both).note)
    assert.ok(validateResolve(form({ fault: 'CUSTOMER', note: '   ' }), both).note)
    assert.equal(validateResolve(form({ fault: 'CUSTOMER', note: 'x' }), both).note, undefined)
    assert.equal(validateResolve(form({ fault: 'CUSTOMER', note: 'x'.repeat(255) }), both).note, undefined)
    assert.equal(validateResolve(form({ fault: 'CUSTOMER', note: `  ${'x'.repeat(255)}  ` }), both).note, undefined)
    assert.match(validateResolve(form({ fault: 'CUSTOMER', note: 'x'.repeat(256) }), both).note ?? '', /256/)
  })

  it('reports every problem at once', () => {
    const errors = validateResolve({ fault: '', amount: 'abc', lockWorker: true, note: '' }, both)

    assert.deepEqual(Object.keys(errors).sort(), ['compensationAmount', 'faultParty', 'lockWorker', 'note'])
  })
})

describe('resolveRequest', () => {
  it('sends the fault, the amount as a number, the lock and the trimmed note', () => {
    assert.deepEqual(resolveRequest(form({ fault: 'FREELANCER', amount: '50.000', lockWorker: true, note: '  ok  ' })), {
      faultParty: 'FREELANCER',
      compensationAmount: 50000,
      lockWorker: true,
      note: 'ok',
    })
  })

  it('sends null for a dismissal and 0 money for a customer verdict, never a lock outside FREELANCER', () => {
    assert.deepEqual(resolveRequest(form({ fault: 'DISMISS' })), { faultParty: null, compensationAmount: 0, lockWorker: false, note: 'Đã đối soát ảnh' })
    assert.equal(resolveRequest(form({ fault: 'CUSTOMER', amount: '999' })).compensationAmount, 0)
    assert.equal(resolveRequest(form({ fault: 'AGENCY', lockWorker: true })).lockWorker, false)
    assert.equal(resolveRequest(form({ fault: 'AGENCY', amount: '1.000' })).compensationAmount, 1000)
  })
})

describe('effectText', () => {
  it('states the money and the lock of a freelancer verdict', () => {
    assert.deepEqual(effectText(form({ fault: 'FREELANCER', amount: '100000', lockWorker: true })), [
      'Hoàn 100.000 đ cho khách ngay.',
      'Trừ 100.000 đ vào kỳ payout kế tiếp của thợ.',
      'Ghi nhận yêu cầu khoá tài khoản thợ (việc khoá do module Thợ thực hiện).',
    ])
  })

  it('says there is no money when the amount is 0', () => {
    assert.deepEqual(effectText(form({ fault: 'FREELANCER' })), ['Ghi nhận lỗi của Freelancer, không có khoản tiền nào.'])
  })

  it('states the SLA and escrow effect of an agency verdict and the no-effect of the others', () => {
    const agency = effectText(form({ fault: 'AGENCY', amount: '50000' }))
    assert.equal(agency[0], 'Hoàn 50.000 đ cho khách ngay.')
    assert.match(agency[1], /5 điểm SLA/)
    assert.match(effectText(form({ fault: 'CUSTOMER' }))[0], /Không có tiền nào/)
    assert.match(effectText(form({ fault: 'DISMISS' }))[0], /không có tác động/)
    assert.deepEqual(effectText(form({ fault: '' })), [])
  })
})

describe('verdictError', () => {
  it('maps the field errors of a 400 to the fields', () => {
    const err = verdictError({
      status: 400,
      message: 'Validation failed',
      data: null,
      fieldErrors: { compensationAmount: ['The compensation must not exceed the value of the order\'s assignments.'], note: ['A note is required.'] },
    })

    assert.equal(err.fields.compensationAmount, "The compensation must not exceed the value of the order's assignments.")
    assert.equal(err.fields.note, 'A note is required.')
    assert.match(err.message, /báo lỗi/)
  })

  it('falls back to the server message when a 400 names no known field', () => {
    assert.equal(verdictError({ status: 400, message: 'Bad', data: null, fieldErrors: {} }).message, 'Bad')
  })

  it('explains 404, 409 and 502 and a network failure in words', () => {
    assert.match(verdictError({ status: 404, message: 'x', data: null }).message, /Không tìm thấy/)
    assert.match(verdictError({ status: 409, message: 'x', data: null }).message, /đã được xử lý/)
    assert.match(verdictError({ status: 502, message: 'x', data: null }).message, /Chưa có gì thay đổi/)
    assert.match(verdictError({ status: 0, message: 'x', data: null }).message, /kết nối/)
    assert.equal(verdictError({ status: 500, message: '', data: null }).message, 'Lỗi 500')
  })
})

describe('safeEvidenceUrl', () => {
  it('accepts an http(s) address and a path on this site', () => {
    assert.equal(safeEvidenceUrl('https://cdn.example.com/a.jpg'), 'https://cdn.example.com/a.jpg')
    assert.equal(safeEvidenceUrl('http://example.com/v.mp4'), 'http://example.com/v.mp4')
    assert.equal(safeEvidenceUrl('/uploads/a.jpg'), '/uploads/a.jpg')
    assert.equal(safeEvidenceUrl('  /files/disputes/x.png  '), '/files/disputes/x.png')
  })

  it('refuses script, data and file addresses and anything that is not an address', () => {
    for (const bad of ['javascript:alert(1)', 'JaVaScRiPt:alert(1)', ' javascript:alert(1)', 'data:text/html,<script>1</script>', 'file:///etc/passwd', 'vbscript:x', 'ftp://x/y', 'a.jpg', '', '   ']) {
      assert.equal(safeEvidenceUrl(bad), null, bad)
    }
  })

  it('refuses a protocol-relative or backslash address that would leave the site', () => {
    assert.equal(safeEvidenceUrl('//evil.example/x'), null)
    assert.equal(safeEvidenceUrl('/\\evil.example'), null)
    assert.equal(safeEvidenceUrl('https://exa mple.com'), null)
  })
})
