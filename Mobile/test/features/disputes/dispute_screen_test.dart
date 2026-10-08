import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/disputes/models/dispute_models.dart';
import 'package:mobile/features/disputes/screens/dispute_create_screen.dart';
import 'package:mobile/features/disputes/screens/dispute_screen.dart';
import 'package:mobile/features/disputes/services/disputes_service.dart';
import 'package:mobile/features/disputes/services/evidence_uploader.dart';

Dispute dispute(int id, String status, {String? fault, int? compensation, String category = 'QUALITY'}) => Dispute(
      disputeId: id,
      orderId: 40 + id,
      raisedBy: 'CUSTOMER',
      category: category,
      description: 'Mô tả $id',
      evidenceUrls: const ['a', 'b'],
      disputeStatus: status,
      faultParty: fault,
      compensationAmount: compensation,
      slaDueAt: DateTime.utc(2026, 10, 10, 3),
      createdAt: DateTime.utc(2026, 10, 8, 3),
    );

class FakeDisputesService implements IDisputesService {
  List<Dispute> mine = [];
  Object? listError;
  Object? createError;
  Completer<void>? createGate;
  final created = <({DisputeRole role, DisputeSubmission submission})>[];
  int listCalls = 0;

  @override
  Future<Dispute> create(DisputeRole role, DisputeSubmission submission) async {
    created.add((role: role, submission: submission));
    if (createGate != null) await createGate!.future;
    if (createError != null) throw createError!;
    final d = dispute(99, 'OPEN');
    mine = [d, ...mine];
    return d;
  }

  @override
  Future<List<Dispute>> listMine(DisputeRole role) async {
    listCalls++;
    if (listError != null) throw listError!;
    return mine;
  }
}

class FakeUploader implements IEvidenceUploader {
  @override
  bool isAvailable;
  final urls = <String?>[];
  Object? error;

  FakeUploader({this.isAvailable = true});

  @override
  Future<String?> pickAndUpload() async {
    if (error != null) throw error!;
    return urls.isEmpty ? 'https://files.example/photo.jpg' : urls.removeAt(0);
  }
}

void setPhone(WidgetTester tester) {
  tester.view.physicalSize = const Size(390, 2200);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
}

Future<void> pumpList(WidgetTester tester, FakeDisputesService service, {DisputeRole role = DisputeRole.customer, int? orderId, IEvidenceUploader? uploader}) async {
  setPhone(tester);
  await tester.pumpWidget(MaterialApp(
    theme: NordicTheme.lightTheme,
    home: DisputeScreen(role: role, orderId: orderId, service: service, uploader: uploader ?? FakeUploader()),
  ));
  await tester.pumpAndSettle();
}

Future<void> pumpForm(WidgetTester tester, FakeDisputesService service, FakeUploader uploader, {DisputeRole role = DisputeRole.customer}) async {
  setPhone(tester);
  await tester.pumpWidget(MaterialApp(
    theme: NordicTheme.lightTheme,
    home: DisputeCreateScreen(role: role, orderId: 42, service: service, uploader: uploader),
  ));
  await tester.pumpAndSettle();
}

Future<void> fillForm(WidgetTester tester, {String category = 'QUALITY', int photos = 1}) async {
  await tester.tap(find.byKey(Key('dispute-category-$category')));
  await tester.enterText(find.byKey(const Key('dispute-description')), '  Dọn chưa sạch  ');
  await tester.pump();
  for (var i = 0; i < photos; i++) {
    await tester.ensureVisible(find.byKey(const Key('dispute-add-photo')));
    await tester.tap(find.byKey(const Key('dispute-add-photo')));
    await tester.pumpAndSettle();
  }
}

Finder get submit => find.byKey(const Key('dispute-submit'));
bool submitEnabled(WidgetTester tester) => tester.widget<ElevatedButton>(find.descendant(of: submit, matching: find.byType(ElevatedButton))).onPressed != null;

void main() {
  group('DisputeScreen (own disputes)', () {
    testWidgets('shows an empty state', (tester) async {
      await pumpList(tester, FakeDisputesService());

      expect(find.byKey(const Key('dispute-empty')), findsOneWidget);
      expect(find.byKey(const Key('dispute-new')), findsNothing);
    });

    testWidgets('an open dispute shows its status and the SLA due time in Ho Chi Minh time', (tester) async {
      await pumpList(tester, FakeDisputesService()..mine = [dispute(1, 'OPEN')]);

      expect(find.text('Đơn #41 · Chất lượng kém'), findsOneWidget);
      expect(find.byKey(const Key('dispute-status-1')), findsOneWidget);
      expect(find.text('Chờ tiếp nhận'), findsOneWidget);
      expect(find.text('Hạn xử lý dự kiến: 10/10/2026 10:00'), findsOneWidget);
      expect(find.textContaining('2 ảnh'), findsOneWidget);
    });

    testWidgets('a resolved dispute shows who was at fault and the compensation', (tester) async {
      await pumpList(tester, FakeDisputesService()..mine = [dispute(2, 'RESOLVED', fault: 'FREELANCER', compensation: 150000)]);

      expect(find.text('Lỗi thuộc về thợ'), findsOneWidget);
      expect(find.text('Bồi thường: 150.000 đ'), findsOneWidget);
    });

    testWidgets('a dismissed dispute says so and shows no compensation', (tester) async {
      await pumpList(tester, FakeDisputesService()..mine = [dispute(3, 'DISMISSED')]);

      expect(find.textContaining('đã bị bác bỏ'), findsOneWidget);
      expect(find.textContaining('Bồi thường:'), findsNothing);
    });

    testWidgets('a load failure offers a retry that loads again', (tester) async {
      final service = FakeDisputesService()..listError = const ApiException(0, 'Không kết nối được máy chủ.');
      await pumpList(tester, service);
      expect(find.text('Không kết nối được máy chủ.'), findsOneWidget);

      service
        ..listError = null
        ..mine = [dispute(1, 'OPEN')];
      await tester.tap(find.text('Thử lại'));
      await tester.pumpAndSettle();

      expect(service.listCalls, 2);
      expect(find.text('Đơn #41 · Chất lượng kém'), findsOneWidget);
    });

    testWidgets('with an order the button opens the form, and after sending the list reloads', (tester) async {
      final service = FakeDisputesService();
      await pumpList(tester, service, orderId: 42);

      await tester.tap(find.byKey(const Key('dispute-new')));
      await tester.pumpAndSettle();
      expect(find.text('Đơn #42'), findsOneWidget);

      await fillForm(tester);
      await tester.ensureVisible(submit);
      await tester.tap(submit);
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('dispute-done')));
      await tester.pumpAndSettle();

      expect(service.listCalls, 2);
      expect(find.text('Đơn #139 · Chất lượng kém'), findsOneWidget);
    });
  });

  group('DisputeCreateScreen', () {
    testWidgets('a customer sees the five categories, a worker four', (tester) async {
      await pumpForm(tester, FakeDisputesService(), FakeUploader());
      expect(find.byKey(const Key('dispute-category-ABSENT_FEE')), findsOneWidget);
      expect(find.byType(ChoiceChip), findsNWidgets(5));

      await pumpForm(tester, FakeDisputesService(), FakeUploader(), role: DisputeRole.worker);
      expect(find.byKey(const Key('dispute-category-ABSENT_FEE')), findsNothing);
      expect(find.byType(ChoiceChip), findsNWidgets(4));
    });

    testWidgets('states the 24 h window and the one-dispute-per-order rule before sending', (tester) async {
      await pumpForm(tester, FakeDisputesService(), FakeUploader());

      expect(find.textContaining('trong vòng 24 giờ'), findsOneWidget);
      expect(find.textContaining('mỗi đơn chỉ có một khiếu nại'), findsOneWidget);
    });

    testWidgets('the send button needs a category, a description and a photo', (tester) async {
      await pumpForm(tester, FakeDisputesService(), FakeUploader());
      expect(submitEnabled(tester), isFalse);

      await tester.tap(find.byKey(const Key('dispute-category-QUALITY')));
      await tester.enterText(find.byKey(const Key('dispute-description')), 'Dọn chưa sạch');
      await tester.pump();
      expect(submitEnabled(tester), isFalse);

      await tester.ensureVisible(find.byKey(const Key('dispute-add-photo')));
      await tester.tap(find.byKey(const Key('dispute-add-photo')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('dispute-evidence-0')), findsOneWidget);
      expect(submitEnabled(tester), isTrue);
    });

    testWidgets('removing the last photo disables the send button again', (tester) async {
      await pumpForm(tester, FakeDisputesService(), FakeUploader());
      await fillForm(tester);
      expect(submitEnabled(tester), isTrue);

      await tester.ensureVisible(find.byKey(const Key('dispute-remove-0')));
      await tester.tap(find.byKey(const Key('dispute-remove-0')));
      await tester.pump();

      expect(find.byKey(const Key('dispute-evidence-0')), findsNothing);
      expect(submitEnabled(tester), isFalse);
    });

    testWidgets('at 10 photos the add button is disabled', (tester) async {
      await pumpForm(tester, FakeDisputesService(), FakeUploader());
      await fillForm(tester, photos: 10);

      expect(find.text('Ảnh bằng chứng (10/10)'), findsOneWidget);
      expect(tester.widget<ButtonStyleButton>(find.descendant(of: find.byKey(const Key('dispute-add-photo')), matching: find.byWidgetPredicate((w) => w is ButtonStyleButton))).onPressed, isNull);
    });

    testWidgets('a cancelled photo pick adds nothing', (tester) async {
      final uploader = FakeUploader()..urls.add(null);
      await pumpForm(tester, FakeDisputesService(), uploader);

      await tester.ensureVisible(find.byKey(const Key('dispute-add-photo')));
      await tester.tap(find.byKey(const Key('dispute-add-photo')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('dispute-evidence-0')), findsNothing);
    });

    testWidgets('a failed upload shows its message and keeps the form', (tester) async {
      final uploader = FakeUploader()..error = const ApiException(0, 'Không tải được ảnh.');
      await pumpForm(tester, FakeDisputesService(), uploader);

      await tester.ensureVisible(find.byKey(const Key('dispute-add-photo')));
      await tester.tap(find.byKey(const Key('dispute-add-photo')));
      await tester.pumpAndSettle();

      expect(find.text('Không tải được ảnh.'), findsOneWidget);
    });

    testWidgets('sends the trimmed description and the photo addresses, then shows the SLA from the server', (tester) async {
      final service = FakeDisputesService();
      await pumpForm(tester, service, FakeUploader());
      await fillForm(tester, category: 'ATTITUDE', photos: 2);

      await tester.ensureVisible(submit);
      await tester.tap(submit);
      await tester.pumpAndSettle();

      final sent = service.created.single;
      expect(sent.role, DisputeRole.customer);
      expect(sent.submission.orderId, 42);
      expect(sent.submission.category, 'ATTITUDE');
      expect(sent.submission.description, 'Dọn chưa sạch');
      expect(sent.submission.evidenceUrls, hasLength(2));
      expect(find.byKey(const Key('dispute-sent')), findsOneWidget);
      expect(find.textContaining('10/10/2026 10:00'), findsOneWidget);
    });

    testWidgets('a double tap while sending sends one request', (tester) async {
      final service = FakeDisputesService()..createGate = Completer<void>();
      await pumpForm(tester, service, FakeUploader());
      await fillForm(tester);

      await tester.ensureVisible(submit);
      await tester.tap(submit);
      await tester.pump();
      await tester.tap(submit, warnIfMissed: false);
      await tester.pump();
      service.createGate!.complete();
      await tester.pumpAndSettle();

      expect(service.created, hasLength(1));
      expect(find.byKey(const Key('dispute-sent')), findsOneWidget);
    });

    testWidgets('a description over 1000 characters is refused before anything is sent', (tester) async {
      final service = FakeDisputesService();
      await pumpForm(tester, service, FakeUploader());
      await fillForm(tester);
      await tester.enterText(find.byKey(const Key('dispute-description')), 'x' * 1001);
      await tester.pump();

      await tester.ensureVisible(submit);
      await tester.tap(submit);
      await tester.pump();

      expect(service.created, isEmpty);
      expect(find.textContaining('tối đa 1000 ký tự'), findsOneWidget);
    });

    testWidgets('a 409 explains the causes and keeps the form', (tester) async {
      final service = FakeDisputesService()..createError = const ApiException(409, 'x');
      await pumpForm(tester, service, FakeUploader());
      await fillForm(tester);

      await tester.ensureVisible(submit);
      await tester.tap(submit);
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('dispute-failure')), findsOneWidget);
      expect(find.textContaining('quá 24 giờ'), findsOneWidget);
      expect(find.byKey(const Key('dispute-sent')), findsNothing);
    });

    testWidgets('server field messages show under their fields', (tester) async {
      final service = FakeDisputesService()
        ..createError = const ApiException(400, 'Validation failed', fieldErrors: {
          'category': ['Category is not allowed.']
        });
      await pumpForm(tester, service, FakeUploader());
      await fillForm(tester);

      await tester.ensureVisible(submit);
      await tester.tap(submit);
      await tester.pumpAndSettle();

      expect(find.text('Category is not allowed.'), findsOneWidget);
    });

    testWidgets('404 and a network failure have their own text', (tester) async {
      final service = FakeDisputesService()..createError = const ApiException(404, 'x');
      await pumpForm(tester, service, FakeUploader());
      await fillForm(tester);
      await tester.ensureVisible(submit);
      await tester.tap(submit);
      await tester.pumpAndSettle();
      expect(find.textContaining('Không tìm thấy đơn'), findsOneWidget);

      service.createError = const ApiException(0, 'Không kết nối được máy chủ.');
      await tester.tap(submit);
      await tester.pumpAndSettle();
      expect(find.text('Không kết nối được máy chủ.'), findsOneWidget);
    });

    testWidgets('when photos cannot be uploaded the form says so and cannot be sent', (tester) async {
      final service = FakeDisputesService();
      await pumpForm(tester, service, FakeUploader(isAvailable: false));
      await tester.tap(find.byKey(const Key('dispute-category-QUALITY')));
      await tester.enterText(find.byKey(const Key('dispute-description')), 'Dọn chưa sạch');
      await tester.pump();

      expect(find.byKey(const Key('dispute-unavailable')), findsOneWidget);
      expect(find.byKey(const Key('dispute-add-photo')), findsNothing);
      expect(submitEnabled(tester), isFalse);
      expect(service.created, isEmpty);
    });
  });
}
