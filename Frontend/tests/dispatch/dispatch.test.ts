import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  formatIncidentType,
  formatReDispatchStatus,
  type IncidentLogDto,
} from '../../src/features/dispatch/types.ts'

describe('dispatch monitoring types and formatting', () => {
  it('formatIncidentType formats known incident types in Vietnamese', () => {
    assert.equal(formatIncidentType('ACCIDENT'), 'Tai nạn / Hỏng xe')
    assert.equal(formatIncidentType('HEALTH'), 'Sức khỏe đột xuất')
    assert.equal(formatIncidentType('SEVERE_WEATHER'), 'Thời tiết ngập lụt')
    assert.equal(formatIncidentType('OTHER'), 'Sự cố khác')
    assert.equal(formatIncidentType('UNKNOWN_CUSTOM'), 'UNKNOWN_CUSTOM')
  })

  it('formatReDispatchStatus returns label and variant corresponding to status', () => {
    const searching = formatReDispatchStatus('SEARCHING')
    assert.equal(searching.label, 'Đang tìm thợ (5m)')
    assert.equal(searching.variant, 'warning')

    const reassigned = formatReDispatchStatus('REASSIGNED')
    assert.equal(reassigned.label, 'Đã đổi thợ cứu hộ')
    assert.equal(reassigned.variant, 'success')

    const failed = formatReDispatchStatus('FAILED')
    assert.equal(failed.label, 'Huỷ ca (Hoàn 100%)')
    assert.equal(failed.variant, 'destructive')

    const other = formatReDispatchStatus('CUSTOM')
    assert.equal(other.label, 'CUSTOM')
    assert.equal(other.variant, 'secondary')
  })

  it('incident log shape satisfies BR-10 fields', () => {
    const incident: IncidentLogDto = {
      incidentId: 99,
      assignmentId: 200,
      workerId: 10,
      incidentType: 'ACCIDENT',
      description: 'Hỏng xích xe đạp điện',
      photoEvidenceUrl: 'https://example.com/bike.jpg',
      latitude: 21.0333,
      longitude: 105.8500,
      reportedAt: '2026-10-08T10:00:00Z',
      isPenaltyExempt: true,
      reDispatchStatus: 'SEARCHING',
      reDispatchDeadline: '2026-10-08T10:05:00Z',
    }

    assert.equal(incident.incidentId, 99)
    assert.equal(incident.isPenaltyExempt, true)
    assert.equal(incident.latitude, 21.0333)
  })
})
