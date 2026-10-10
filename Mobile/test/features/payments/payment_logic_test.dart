import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/payments/logic/payment_logic.dart';
import 'package:mobile/features/payments/models/payment_models.dart';

final _created = DateTime.utc(2026, 10, 14, 3);

Payment payment({String status = 'PENDING', DateTime? expiresAt, String purpose = 'ORDER'}) => Payment(
      paymentId: 1,
      purpose: purpose,
      orderId: 42,
      extensionId: null,
      gateway: 'FAKE',
      amount: 260000,
      txnStatus: status,
      qrPayload: 'fake-qr://FAKE-1',
      payUrl: null,
      expiresAt: expiresAt ?? _created.add(const Duration(minutes: 15)),
      paidAt: null,
      createdAt: _created,
      sandbox: true,
    );

void main() {
  test('the banner text is the one decision Q04 asks for', () {
    expect(sandboxBannerText, 'SANDBOX – no real money');
  });

  group('phase', () {
    test('follows the server status', () {
      expect(phaseOf(payment(status: 'SUCCESS'), _created), PaymentPhase.paid);
      expect(phaseOf(payment(status: 'EXPIRED'), _created), PaymentPhase.expired);
      expect(phaseOf(payment(status: 'REFUNDED'), _created), PaymentPhase.refunded);
      expect(phaseOf(payment(), _created), PaymentPhase.waiting);
    });

    test('a pending payment is expired at its deadline, and still waiting one second before', () {
      final deadline = _created.add(const Duration(minutes: 15));

      expect(phaseOf(payment(), deadline.subtract(const Duration(seconds: 1))), PaymentPhase.waiting);
      expect(phaseOf(payment(), deadline), PaymentPhase.expired);
      expect(phaseOf(payment(), deadline.add(const Duration(minutes: 1))), PaymentPhase.expired);
    });

    test('a paid payment stays paid after the deadline', () {
      expect(phaseOf(payment(status: 'SUCCESS'), _created.add(const Duration(hours: 1))), PaymentPhase.paid);
    });

    test('polling goes on only while waiting', () {
      expect(shouldKeepPolling(payment(), _created), isTrue);
      expect(shouldKeepPolling(payment(status: 'SUCCESS'), _created), isFalse);
      expect(shouldKeepPolling(payment(status: 'EXPIRED'), _created), isFalse);
      expect(shouldKeepPolling(payment(), _created.add(const Duration(minutes: 15))), isFalse);
    });
  });

  group('countdown', () {
    test('counts down to the deadline and never goes below zero', () {
      expect(countdownText(timeLeft(payment(), _created)), '15:00');
      expect(countdownText(timeLeft(payment(), _created.add(const Duration(seconds: 1)))), '14:59');
      expect(countdownText(timeLeft(payment(), _created.add(const Duration(minutes: 14, seconds: 55)))), '00:05');
      expect(countdownText(timeLeft(payment(), _created.add(const Duration(minutes: 20)))), '00:00');
    });

    test('reads a local-time clock as the same instant', () {
      final local = _created.add(const Duration(minutes: 5)).toLocal();

      expect(countdownText(timeLeft(payment(), local)), '10:00');
    });
  });

  test('formats the amount and names the purpose', () {
    expect(formatVnd(260000), '260.000 đ');
    expect(formatVnd(97500), '97.500 đ');
    expect(purposeLabel(payment()), 'Thanh toán đơn');
    expect(purposeLabel(payment(purpose: 'EXTENSION')), 'Thanh toán làm thêm giờ');
  });

  group('a failed QR request', () {
    test('explains each business code, and never depends on the message', () {
      expect(qrFailureMessage(const ApiException(409, 'server text', code: 'PAYMENT_EXPIRED')), contains('quá hạn thanh toán'));
      expect(qrFailureMessage(const ApiException(409, 'server text', code: 'INVALID_STATE')), contains('không còn chờ thanh toán'));
      expect(qrFailureMessage(const ApiException(409, 'server text')), 'Không tạo được mã thanh toán lúc này.');
    });

    test('explains 401, 403, 404, 502, 500 and no network', () {
      expect(qrFailureMessage(const ApiException(401, '')), contains('đăng nhập lại'));
      expect(qrFailureMessage(const ApiException(403, '')), contains('khách hàng'));
      expect(qrFailureMessage(const ApiException(404, '')), contains('Không tìm thấy đơn'));
      expect(qrFailureMessage(const ApiException(502, 'gateway')), contains('Chưa có gì bị trừ'));
      expect(qrFailureMessage(const ApiException(500, 'stack')), 'Máy chủ gặp lỗi. Vui lòng thử lại sau.');
      expect(qrFailureMessage(const ApiException(0, 'offline')), 'offline');
    });
  });
}
