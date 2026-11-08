import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  acceptedLine, afterShiftProblem, anglePairs, anglesSummary, categoryLabel, disputeHoursLeft, disputeStatusLabel, evidenceFileError, formatWindow,
  overallStars, ratingWindowText, redoReasonError, secondsUntil, reviewAssignments, validateDispute, validateRating,
} from '../src/features/customer/after-shift/view.ts'
import type { JobPhoto } from '../src/features/customer/after-shift/types.ts'

const photo = (angleNo: number, photoPhase: string, over: Partial<JobPhoto> = {}): JobPhoto => ({
  photoId: angleNo * 10 + (photoPhase === 'BEFORE' ? 1 : 2), assignmentId: 1, photoPhase, angleNo, photoUrl: 'x', isAccepted: true, uploadedAt: '2026-11-01T01:00:00Z', ...over,
})

describe('anglePairs', () => {
  it('always lists the 5 angles and pairs before with after', () => {
    const pairs = anglePairs([photo(1, 'BEFORE'), photo(1, 'AFTER'), photo(3, 'BEFORE')])
    assert.equal(pairs.length, 5)
    assert.ok(pairs[0].before && pairs[0].after)
    assert.ok(pairs[2].before && !pairs[2].after)
    assert.deepEqual([pairs[1].before, pairs[1].after], [null, null])
    assert.equal(anglesSummary(pairs), '1 / 5 góc có ảnh trước và sau.')
  })
  it('ignores rejected photos and takes the newest accepted one', () => {
    const pairs = anglePairs([
      photo(2, 'AFTER', { photoId: 1, isAccepted: false }),
      photo(2, 'AFTER', { photoId: 2, uploadedAt: '2026-11-01T02:00:00Z' }),
      photo(2, 'AFTER', { photoId: 3, uploadedAt: '2026-11-01T03:00:00Z' }),
    ])
    assert.equal(pairs[1].after?.photoId, 3)
    assert.equal(anglePairs([photo(2, 'AFTER', { isAccepted: false })])[1].after, null)
  })
})

describe('rating', () => {
  const full = { punctuality: 5, cleaningQuality: 4, attitude: 5 }
  it('needs all three criteria from 1 to 5', () => {
    assert.deepEqual(validateRating(full, ''), {})
    assert.ok(validateRating({ punctuality: 5 }, '').criteria)
    assert.ok(validateRating({ ...full, attitude: 0 }, '').criteria)
    assert.ok(validateRating({ ...full, attitude: 6 }, '').criteria)
    assert.ok(validateRating({ ...full, attitude: 2.5 }, '').criteria)
  })
  it('limits the comment to 500 characters', () => {
    assert.equal(validateRating(full, 'x'.repeat(500)).comment, undefined)
    assert.ok(validateRating(full, 'x'.repeat(501)).comment)
  })
  it('derives the stars from the average, between 1 and 5', () => {
    assert.equal(overallStars(full), 5)
    assert.equal(overallStars({ punctuality: 1, cleaningQuality: 1, attitude: 1 }), 1)
    assert.equal(overallStars({ punctuality: 3, cleaningQuality: 4, attitude: 4 }), 4)
    assert.equal(overallStars({ punctuality: 5 }), null)
  })
  it('counts the 48 h window down in hh:mm:ss', () => {
    assert.equal(formatWindow(46 * 3600 + 12 * 60 + 5), '46:12:05')
    assert.equal(formatWindow(-5), '00:00:00')
    const now = new Date('2026-11-01T10:00:00Z')
    assert.equal(secondsUntil('2026-11-01T11:00:00Z', now), 3600)
    assert.equal(secondsUntil('2026-11-01T09:00:00Z', now), 0)
    assert.equal(secondsUntil(null, now), null)
    assert.equal(secondsUntil('bad', now), null)
  })
  it('explains every closed window', () => {
    for (const reason of ['ALREADY_RATED', 'WINDOW_CLOSED', 'NOT_COMPLETED', 'SOMETHING']) assert.ok(ratingWindowText({ reason }).length > 10, reason)
    assert.match(ratingWindowText({ reason: 'WINDOW_CLOSED' }), /48 giờ/)
  })
})

describe('dispute form', () => {
  const ok = { category: 'QUALITY', description: 'Kính còn vệt', evidenceCount: 2 }
  it('accepts a complete form', () => assert.deepEqual(validateDispute(ok), {}))
  it('requires a category, a description of 1 to 1000 characters and 1 to 10 photos', () => {
    assert.ok(validateDispute({ ...ok, category: '' }).category)
    assert.ok(validateDispute({ ...ok, category: 'NOPE' }).category)
    assert.ok(validateDispute({ ...ok, description: '   ' }).description)
    assert.equal(validateDispute({ ...ok, description: 'x'.repeat(1000) }).description, undefined)
    assert.ok(validateDispute({ ...ok, description: 'x'.repeat(1001) }).description)
    assert.ok(validateDispute({ ...ok, evidenceCount: 0 }).evidence)
    assert.equal(validateDispute({ ...ok, evidenceCount: 10 }).evidence, undefined)
    assert.ok(validateDispute({ ...ok, evidenceCount: 11 }).evidence)
  })
  it('checks an evidence file: jpeg/png/webp, 1 byte to 5 MB', () => {
    assert.equal(evidenceFileError({ name: 'a.jpg', type: 'image/jpeg', size: 1000 }), null)
    assert.equal(evidenceFileError({ name: 'a.webp', type: 'image/webp', size: 5 * 1024 * 1024 }), null)
    assert.match(evidenceFileError({ name: 'a.gif', type: 'image/gif', size: 10 })!, /JPG, PNG hoặc WebP/)
    assert.match(evidenceFileError({ name: 'a.png', type: 'image/png', size: 5 * 1024 * 1024 + 1 })!, /5 MB/)
    assert.match(evidenceFileError({ name: 'a.png', type: 'image/png', size: 0 })!, /rỗng/)
  })
  it('computes the hours left of the 24 h filing window', () => {
    const now = new Date('2026-11-02T00:00:00Z')
    assert.equal(disputeHoursLeft('2026-11-01T12:00:00Z', now), 12)
    assert.equal(disputeHoursLeft('2026-10-30T00:00:00Z', now), 0)
    assert.equal(disputeHoursLeft(null, now), null)
  })
  it('shows the server status, and the raw value when unknown', () => {
    assert.equal(disputeStatusLabel('IN_REVIEW'), 'Đang xử lý')
    assert.equal(disputeStatusLabel('RESOLVED'), 'Đã giải quyết')
    assert.equal(disputeStatusLabel('SOMETHING_NEW'), 'SOMETHING_NEW')
    assert.equal(categoryLabel('PROPERTY_DAMAGE'), 'Hư hỏng tài sản')
    assert.equal(categoryLabel('X'), 'X')
  })
})

describe('redo and errors', () => {
  it('needs a redo reason of 1 to 255 characters', () => {
    assert.ok(redoReasonError(' '))
    assert.equal(redoReasonError('Kính còn vệt'), null)
    assert.ok(redoReasonError('x'.repeat(256)))
  })
  it('maps failures', () => {
    assert.equal(afterShiftProblem({ status: 400, fieldErrors: { description: ['Quá dài.'] } }), 'Quá dài.')
    assert.equal(afterShiftProblem({ status: 409, message: 'The TIME TO FILE a dispute for this order has passed.' }), 'Đã quá 24 giờ sau ca nên không thể khiếu nại đơn này nữa.')
    assert.equal(afterShiftProblem({ status: 409, message: 'There is NOTHING TO DISPUTE on this order yet.' }), 'Đơn này chưa có ca nào để khiếu nại. Bạn chỉ khiếu nại được khi thợ đã làm hoặc đang chờ nghiệm thu.')
    assert.equal(afterShiftProblem({ status: 409, message: 'A dispute already exists for this order.' }), 'Đơn này đã có khiếu nại. Mỗi đơn chỉ có một khiếu nại.')
    assert.match(afterShiftProblem({ status: 409, message: 'The shift ended more than 24 h ago.' }), /không còn hợp lệ/)
    assert.match(afterShiftProblem({ status: 404 }), /Không tìm thấy/)
    assert.match(afterShiftProblem({ status: 500 }), /Đã có lỗi/)
  })
})

describe('acceptance review', () => {
  const a = (assignmentSeq: number, assignmentStatus: string) => ({ assignmentId: assignmentSeq, assignmentSeq, assignmentStatus })
  it('reviews the completed assignments by seq, none while one awaits acceptance', () => {
    assert.deepEqual(reviewAssignments([a(2, 'COMPLETED'), a(1, 'COMPLETED'), a(3, 'CANCELLED')]).map((x) => x.assignmentSeq), [1, 2])
    assert.deepEqual(reviewAssignments([a(1, 'COMPLETED'), a(2, 'AWAITING_ACCEPTANCE')]), [])
  })
  it('states the acceptance time in Vietnam time', () => {
    assert.equal(acceptedLine('2026-11-02T07:05:00Z'), 'Bạn đã nghiệm thu ca này lúc 14:05, 02/11/2026.')
    assert.equal(acceptedLine('2026-11-02T18:30:00Z'), 'Bạn đã nghiệm thu ca này lúc 01:30, 03/11/2026.')
    assert.equal(acceptedLine(null), 'Ca đã được nghiệm thu.')
  })
})
