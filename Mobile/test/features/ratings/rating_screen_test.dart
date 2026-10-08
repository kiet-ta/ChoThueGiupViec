import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/ratings/models/rating_models.dart';
import 'package:mobile/features/ratings/screens/rating_screen.dart';
import 'package:mobile/features/ratings/services/ratings_service.dart';

class FakeRatingsService implements IRatingsService {
  RatingWindow window;
  Object? windowError;
  Object? submitError;
  Completer<void>? submitGate;
  final submissions = <RatingSubmission>[];
  int windowCalls = 0;

  FakeRatingsService({RatingWindow? window})
      : window = window ?? const RatingWindow(assignmentId: 7, canRate: true, reason: RatingWindow.open, secondsRemaining: 47 * 3600 + 12 * 60);

  @override
  Future<RatingWindow> getWindow(RatingRole role, int assignmentId) async {
    windowCalls++;
    if (windowError != null) throw windowError!;
    return window;
  }

  @override
  Future<void> submit(RatingRole role, int assignmentId, RatingSubmission submission) async {
    submissions.add(submission);
    if (submitGate != null) await submitGate!.future;
    if (submitError != null) throw submitError!;
  }
}

Future<void> pumpScreen(WidgetTester tester, FakeRatingsService service, {RatingRole role = RatingRole.customer, String? name}) async {
  tester.view.physicalSize = const Size(390, 1400);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  await tester.pumpWidget(MaterialApp(
    theme: NordicTheme.lightTheme,
    home: RatingScreen(role: role, assignmentId: 7, ratedName: name, service: service),
  ));
  await tester.pumpAndSettle();
}

Future<void> tapStar(WidgetTester tester, String prefix, int n) async {
  await tester.tap(find.byKey(Key('$prefix-$n')));
  await tester.pump();
}

Future<void> fillCustomer(WidgetTester tester, {int stars = 5}) async {
  await tapStar(tester, 'stars', stars);
  await tapStar(tester, 'criterion-punctuality', 5);
  await tapStar(tester, 'criterion-cleaningQuality', 4);
  await tapStar(tester, 'criterion-attitude', 5);
}

Finder get submit => find.byKey(const Key('rating-submit'));

void main() {
  group('RatingScreen as the customer (MOB-M6-01)', () {
    testWidgets('shows the countdown, the overall stars and the three fixed criteria', (tester) async {
      await pumpScreen(tester, FakeRatingsService(), name: 'Nguyễn Thị Mai');

      expect(find.text('Đánh giá thợ Nguyễn Thị Mai'), findsOneWidget);
      expect(find.text('Còn 47 giờ 12 phút'), findsOneWidget);
      expect(find.text('Đúng giờ'), findsOneWidget);
      expect(find.text('Chất lượng dọn dẹp'), findsOneWidget);
      expect(find.text('Thái độ'), findsOneWidget);
      expect(find.text('Phối hợp của gia chủ'), findsNothing);
      expect(find.byKey(const Key('rating-internal')), findsNothing);
    });

    testWidgets('the send button stays disabled until every score is chosen', (tester) async {
      await pumpScreen(tester, FakeRatingsService());
      expect(tester.widget<ElevatedButton>(find.descendant(of: submit, matching: find.byType(ElevatedButton))).onPressed, isNull);

      await tapStar(tester, 'stars', 5);
      await tapStar(tester, 'criterion-punctuality', 5);
      await tapStar(tester, 'criterion-cleaningQuality', 4);
      expect(tester.widget<ElevatedButton>(find.descendant(of: submit, matching: find.byType(ElevatedButton))).onPressed, isNull);

      await tapStar(tester, 'criterion-attitude', 5);
      expect(tester.widget<ElevatedButton>(find.descendant(of: submit, matching: find.byType(ElevatedButton))).onPressed, isNotNull);
    });

    testWidgets('sends the fixed keys and the trimmed comment, then thanks the customer', (tester) async {
      final service = FakeRatingsService();
      await pumpScreen(tester, service);
      await fillCustomer(tester, stars: 4);
      await tester.enterText(find.byKey(const Key('rating-comment')), '  Dọn rất kỹ  ');
      await tester.pump();

      await tester.tap(submit);
      await tester.pumpAndSettle();

      final sent = service.submissions.single;
      expect(sent.stars, 4);
      expect(sent.criteria, {'punctuality': 5, 'cleaningQuality': 4, 'attitude': 5});
      expect(sent.comment, 'Dọn rất kỹ');
      expect(find.byKey(const Key('rating-thanks')), findsOneWidget);
      expect(find.textContaining('góp vào điểm uy tín của thợ'), findsOneWidget);
    });

    testWidgets('a comment over 500 characters is refused before anything is sent', (tester) async {
      final service = FakeRatingsService();
      await pumpScreen(tester, service);
      await fillCustomer(tester);
      await tester.enterText(find.byKey(const Key('rating-comment')), 'x' * 501);
      await tester.pump();

      await tester.tap(submit);
      await tester.pump();

      expect(service.submissions, isEmpty);
      expect(find.textContaining('tối đa 500 ký tự'), findsOneWidget);
    });

    testWidgets('a double tap while sending sends one request', (tester) async {
      final service = FakeRatingsService()..submitGate = Completer<void>();
      await pumpScreen(tester, service);
      await fillCustomer(tester);

      await tester.tap(submit);
      await tester.pump();
      await tester.tap(submit, warnIfMissed: false);
      await tester.pump();
      service.submitGate!.complete();
      await tester.pumpAndSettle();

      expect(service.submissions, hasLength(1));
      expect(find.byKey(const Key('rating-thanks')), findsOneWidget);
    });

    testWidgets('a 409 explains and loads the window again, which shows the real reason', (tester) async {
      final service = FakeRatingsService()..submitError = const ApiException(409, 'x');
      await pumpScreen(tester, service);
      await fillCustomer(tester);
      service.window = const RatingWindow(assignmentId: 7, canRate: false, reason: RatingWindow.alreadyRated);

      await tester.tap(submit);
      await tester.pumpAndSettle();

      expect(service.windowCalls, 2);
      expect(find.byKey(const Key('rating-closed-message')), findsOneWidget);
      expect(find.textContaining('đã đánh giá ca này rồi'), findsOneWidget);
    });

    testWidgets('server field messages show under their field and the form stays', (tester) async {
      final service = FakeRatingsService()
        ..submitError = const ApiException(400, 'Validation failed', fieldErrors: {
          'criteria.attitude': ['Must be an integer between 1 and 5.']
        });
      await pumpScreen(tester, service);
      await fillCustomer(tester);

      await tester.tap(submit);
      await tester.pumpAndSettle();

      expect(find.text('Must be an integer between 1 and 5.'), findsOneWidget);
      expect(find.byKey(const Key('rating-failure')), findsOneWidget);
      expect(find.byKey(const Key('rating-submit')), findsOneWidget);
    });

    testWidgets('a network failure while sending shows the message and keeps what was typed', (tester) async {
      final service = FakeRatingsService()..submitError = const ApiException(0, 'Không kết nối được máy chủ.');
      await pumpScreen(tester, service);
      await fillCustomer(tester);

      await tester.tap(submit);
      await tester.pumpAndSettle();

      expect(find.text('Không kết nối được máy chủ.'), findsOneWidget);
      expect(tester.widget<ElevatedButton>(find.descendant(of: submit, matching: find.byType(ElevatedButton))).onPressed, isNotNull);
    });

    testWidgets('says plainly that a rating cannot be edited after sending', (tester) async {
      await pumpScreen(tester, FakeRatingsService());

      expect(find.textContaining('không sửa hay gửi lại được'), findsOneWidget);
    });
  });

  group('RatingScreen as the worker (MOB-M6-02)', () {
    testWidgets('shows the two worker criteria and says the rating is internal only', (tester) async {
      await pumpScreen(tester, FakeRatingsService(), role: RatingRole.worker);

      expect(find.text('Đánh giá gia chủ'), findsOneWidget);
      expect(find.text('Phối hợp của gia chủ'), findsOneWidget);
      expect(find.text('Điều kiện làm việc'), findsOneWidget);
      expect(find.text('Đúng giờ'), findsNothing);
      expect(find.byKey(const Key('rating-internal')), findsOneWidget);
    });

    testWidgets('sends only the worker keys and ends with the internal-only thanks', (tester) async {
      final service = FakeRatingsService();
      await pumpScreen(tester, service, role: RatingRole.worker);
      await tapStar(tester, 'stars', 3);
      await tapStar(tester, 'criterion-cooperation', 4);
      await tapStar(tester, 'criterion-workingConditions', 2);

      await tester.tap(submit);
      await tester.pumpAndSettle();

      expect(service.submissions.single.criteria, {'cooperation': 4, 'workingConditions': 2});
      expect(service.submissions.single.comment, isNull);
      expect(find.textContaining('chỉ dùng nội bộ'), findsOneWidget);
    });
  });

  group('RatingScreen states', () {
    testWidgets('shows a loader while the window loads', (tester) async {
      final service = FakeRatingsService();
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });
      await tester.pumpWidget(MaterialApp(theme: NordicTheme.lightTheme, home: RatingScreen(role: RatingRole.customer, assignmentId: 7, service: service)));

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      await tester.pumpAndSettle();
    });

    for (final (reason, text) in [
      (RatingWindow.notCompleted, 'chưa hoàn thành'),
      (RatingWindow.windowClosed, 'quá 48 giờ'),
      (RatingWindow.alreadyRated, 'đã đánh giá ca này rồi'),
    ]) {
      testWidgets('says why it cannot be rated: $reason', (tester) async {
        await pumpScreen(tester, FakeRatingsService(window: RatingWindow(assignmentId: 7, canRate: false, reason: reason)));

        expect(find.textContaining(text), findsOneWidget);
        expect(submit, findsNothing);
      });
    }

    testWidgets('an assignment that is not the caller\'s (404) is a plain message with a retry', (tester) async {
      final service = FakeRatingsService()..windowError = const ApiException(404, 'x');
      await pumpScreen(tester, service);

      expect(find.text('Không tìm thấy ca làm này.'), findsOneWidget);
      expect(find.text('Thử lại'), findsOneWidget);
    });

    testWidgets('a network failure while loading offers a retry that loads again', (tester) async {
      final service = FakeRatingsService()..windowError = const ApiException(0, 'Không kết nối được máy chủ.');
      await pumpScreen(tester, service);
      expect(find.text('Không kết nối được máy chủ.'), findsOneWidget);

      service.windowError = null;
      await tester.tap(find.text('Thử lại'));
      await tester.pumpAndSettle();

      expect(service.windowCalls, 2);
      expect(find.text('Còn 47 giờ 12 phút'), findsOneWidget);
    });
  });

  group('StarRow', () {
    testWidgets('has a spoken label per star and fills the stars up to the value', (tester) async {
      var picked = 0;
      await tester.pumpWidget(MaterialApp(
        home: Scaffold(body: StarRow(keyPrefix: 's', label: 'Tổng thể', value: 3, onChanged: (v) => picked = v)),
      ));

      expect(find.bySemanticsLabel('Tổng thể: 3 sao'), findsOneWidget);
      expect(find.byIcon(Icons.star_rounded), findsNWidgets(3));
      expect(find.byIcon(Icons.star_border_rounded), findsNWidgets(2));

      await tester.tap(find.byKey(const Key('s-5')));
      expect(picked, 5);
    });
  });
}
