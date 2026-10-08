import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/dispatch/models/incident_log.dart';

void main() {
  group('IncidentLog Model Tests', () {
    test('IncidentLog JSON roundtrip with force majeure valid reason', () {
      final incident = IncidentLog(
        incidentId: 10,
        assignmentId: 1001,
        workerId: 42,
        incidentType: 'ACCIDENT',
        description: 'Hỏng xe trên đường đến điểm hẹn',
        photoEvidenceUrl: 'https://cdn.toam.vn/evidence/incident_10.jpg',
        latitude: 10.776889,
        longitude: 106.700806,
        reportedAt: DateTime.parse('2026-10-08T10:00:00.000Z'),
        isPenaltyExempt: true,
        reDispatchStatus: 'SEARCHING',
        reDispatchDeadline: DateTime.parse('2026-10-08T10:05:00.000Z'),
        substituteWorkerId: 55,
      );

      final json = incident.toJson();
      expect(json['incidentId'], 10);
      expect(json['assignmentId'], 1001);
      expect(json['workerId'], 42);
      expect(json['incidentType'], 'ACCIDENT');
      expect(json['description'], 'Hỏng xe trên đường đến điểm hẹn');
      expect(json['photoEvidenceUrl'], 'https://cdn.toam.vn/evidence/incident_10.jpg');
      expect(json['latitude'], 10.776889);
      expect(json['longitude'], 106.700806);
      expect(json['isPenaltyExempt'], true);
      expect(json['reDispatchDeadline'], '2026-10-08T10:05:00.000Z');
      expect(json['substituteWorkerId'], 55);

      final restored = IncidentLog.fromJson(json);
      expect(restored.incidentId, incident.incidentId);
      expect(restored.assignmentId, incident.assignmentId);
      expect(restored.description, incident.description);
      expect(restored.incidentType, 'ACCIDENT');
      expect(restored.isPenaltyExempt, isTrue);
      expect(restored.substituteWorkerId, 55);
    });

    test('IncidentLog can parse minimal json without optional fields', () {
      final json = {
        'incidentId': 11,
        'assignmentId': 2002,
        'workerId': 99,
        'incidentType': 'WEATHER',
        'description': 'Thời tiết ngập lụt nghiêm trọng',
        'reportedAt': '2026-10-08T10:15:00.000Z',
      };

      final log = IncidentLog.fromJson(json);
      expect(log.incidentId, 11);
      expect(log.assignmentId, 2002);
      expect(log.photoEvidenceUrl, isNull);
      expect(log.latitude, 0.0);
      expect(log.longitude, 0.0);
      expect(log.isPenaltyExempt, isTrue);
      expect(log.substituteWorkerId, isNull);
    });
  });
}
