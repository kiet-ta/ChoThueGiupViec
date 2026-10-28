import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/core/widgets/nordic_text_input.dart';
import 'package:mobile/features/workers/models/customer_acceptance_models.dart';
import 'package:mobile/features/workers/models/job_photo_models.dart';
import 'package:mobile/features/workers/routes.dart';
import 'package:mobile/features/workers/screens/customer_acceptance_screen.dart';
import 'package:mobile/features/workers/services/customer_acceptance_service.dart';
import 'package:mobile/features/workers/services/workers_photos_service.dart';

import 'design_frame.dart';

JobPhotoDto photo(String phase, int angle, int id) => JobPhotoDto(
  photoId: id,
  assignmentId: 55,
  photoPhase: phase,
  angleNo: angle,
  photoUrl: '/api/workers/assignments/55/photos/$id/file',
  volScore: 142.5,
  isAccepted: true,
  uploadedAt: DateTime(2026, 10, 21, 9),
);

/// Gives the photos the test chose, or fails like the server / the network.
class FakePhotosService extends WorkersPhotosService {
  List<JobPhotoDto> photos = [];
  ApiException? failure;
  int reads = 0;

  FakePhotosService() : super(apiClient: ApiClient(baseUrl: 'http://api.test'));

  @override
  Future<ApiResponse<List<JobPhotoDto>>> getPhotos(int assignmentId) async {
    reads++;
    if (failure != null) throw failure!;
    return ApiResponse<List<JobPhotoDto>>(
      success: true,
      message: 'OK',
      data: photos,
    );
  }
}

/// Answers accept / redo like the server would, or fails.
class FakeAcceptanceService extends CustomerAcceptanceService {
  AssignmentCompletionDto? completion;
  AssignmentStatusDto? redone;
  ApiException? failure;
  bool emptyAnswer = false;
  final accepted = <({int id, String feedback})>[];
  final redos = <({int id, String reason})>[];

  FakeAcceptanceService()
    : super(apiClient: ApiClient(baseUrl: 'http://api.test'));

  @override
  Future<ApiResponse<AssignmentCompletionDto>> acceptCompletion({
    required int assignmentId,
    String? feedback,
  }) async {
    accepted.add((id: assignmentId, feedback: feedback ?? ''));
    if (failure != null) throw failure!;
    return ApiResponse<AssignmentCompletionDto>(
      success: true,
      message: 'OK',
      data: emptyAnswer ? null : completion,
    );
  }

  @override
  Future<ApiResponse<AssignmentStatusDto>> requestRedo({
    required int assignmentId,
    required RequestRedoRequest request,
  }) async {
    redos.add((id: assignmentId, reason: request.reason));
    if (failure != null) throw failure!;
    return ApiResponse<AssignmentStatusDto>(
      success: true,
      message: 'OK',
      data: emptyAnswer ? null : redone,
    );
  }
}

Future<void> pumpScreen(
  WidgetTester tester, {
  FakePhotosService? photos,
  FakeAcceptanceService? acceptance,
}) async {
  useDesignFrame(tester);
  await tester.pumpWidget(
    MaterialApp(
      home: CustomerAcceptanceScreen(
        assignmentId: 55,
        photosService: photos ?? FakePhotosService(),
        acceptanceService: acceptance ?? FakeAcceptanceService(),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

/// MOB-CUSTOMER-03 (#386): the acceptance shows the real photos and the real answers of the server, never a sample or a made-up success.
void main() {
  setUp(() => TokenStorage().clear());

  group('CustomerAcceptanceScreen', () {
    testWidgets(
      'renders the title with the assignment id, the status chip, the photo boxes and the two actions',
      (tester) async {
        await pumpScreen(tester);

        expect(find.text('Nghiệm thu ca'), findsOneWidget);
        expect(find.text('Chờ nghiệm thu'), findsOneWidget);
        expect(find.text('So sánh ảnh trước và sau'), findsOneWidget);
        expect(find.text('Góc 1: Toàn cảnh phòng'), findsOneWidget);
        expect(find.text('Trước'), findsOneWidget);
        expect(find.text('Sau'), findsOneWidget);
        expect(find.text('Nghiệm thu'), findsOneWidget);
        expect(find.text('Dọn lại'), findsOneWidget);
      },
    );

    testWidgets('a job without photos shows empty boxes, not sample photos', (
      tester,
    ) async {
      await pumpScreen(tester);

      expect(find.text('Chưa có ảnh'), findsNWidgets(2));
      expect(find.byType(Image), findsNothing);
      expect(find.textContaining('storage.giupviec.local'), findsNothing);
    });

    testWidgets(
      'the real photos of the chosen angle are loaded with the Bearer token',
      (tester) async {
        TokenStorage().saveTokens(accessToken: 'tok', refreshToken: 'ref');
        final photos = FakePhotosService()
          ..photos = [
            photo('BEFORE', 1, 11),
            photo('AFTER', 1, 12),
            photo('BEFORE', 2, 13),
          ];
        await pumpScreen(tester, photos: photos);

        final images = tester
            .widgetList<Image>(find.byType(Image))
            .map((i) => i.image as NetworkImage)
            .toList();
        expect(images.map((i) => i.url), [
          'http://api.test/api/workers/assignments/55/photos/11/file',
          'http://api.test/api/workers/assignments/55/photos/12/file',
        ]);
        expect(
          images.every((i) => i.headers?['Authorization'] == 'Bearer tok'),
          isTrue,
        );

        await tapVisible(tester, find.text('Góc 2'));
        expect(find.text('Góc 2: Bàn ghế & tủ kệ'), findsOneWidget);
        expect(
          tester
              .widgetList<Image>(find.byType(Image))
              .map((i) => (i.image as NetworkImage).url),
          ['http://api.test/api/workers/assignments/55/photos/13/file'],
        );
        expect(
          find.text('Chưa có ảnh'),
          findsOneWidget,
        ); // no AFTER photo for angle 2
      },
    );

    testWidgets(
      'a failed photo load shows the reason with a retry, and the retry loads again',
      (tester) async {
        final photos = FakePhotosService()
          ..failure = const ApiException(
            0,
            'Không kết nối được máy chủ. Vui lòng kiểm tra mạng và thử lại.',
          );
        await pumpScreen(tester, photos: photos);

        expect(find.byKey(const Key('photos-error')), findsOneWidget);
        expect(
          find.text(
            'Không kết nối được máy chủ. Vui lòng kiểm tra mạng và thử lại.',
          ),
          findsOneWidget,
        );
        expect(
          find.byType(Image),
          findsNothing,
          reason: 'no sample photos instead',
        );

        photos
          ..failure = null
          ..photos = [photo('BEFORE', 1, 11)];
        await tapVisible(tester, find.byKey(const Key('photos-retry')));

        expect(photos.reads, 2);
        expect(find.byKey(const Key('photos-error')), findsNothing);
        expect(find.byType(Image), findsOneWidget);
      },
    );

    testWidgets(
      'accepting shows the amounts the server answered, with its commission rate',
      (tester) async {
        final acceptance = FakeAcceptanceService()
          ..completion = AssignmentCompletionDto(
            assignmentId: 55,
            status: 'COMPLETED',
            grossAmount: 300000,
            commissionRate: 0.0,
            payoutAmount: 300000,
            completedAt: DateTime(2026, 10, 21, 16),
          );
        await pumpScreen(tester, acceptance: acceptance);

        await tapVisible(tester, find.text('Nghiệm thu'));

        expect(acceptance.accepted.single.id, 55);
        expect(find.text('Đã nghiệm thu'), findsOneWidget);
        expect(find.text('Chi tiết nghiệm thu'), findsOneWidget);
        expect(find.text('Phí nền tảng (0%)'), findsOneWidget);
        expect(find.text('Người làm nhận (100%)'), findsOneWidget);
        expect(find.text('300000 đ'), findsNWidgets(2));
      },
    );

    testWidgets('a 409 is explained and nothing is shown as accepted', (
      tester,
    ) async {
      final acceptance = FakeAcceptanceService()
        ..failure = const ApiException(409, 'Invalid state');
      await pumpScreen(tester, acceptance: acceptance);

      await tapVisible(tester, find.text('Nghiệm thu'));
      await scrollTo(tester, find.textContaining('không còn chờ nghiệm thu'));

      expect(
        find.textContaining('Ca này không còn chờ nghiệm thu'),
        findsOneWidget,
      );
      await scrollTo(tester, find.text('Chờ nghiệm thu'));
      expect(find.text('Chờ nghiệm thu'), findsOneWidget);
      expect(find.text('Đã nghiệm thu'), findsNothing);
      expect(find.text('Chi tiết nghiệm thu'), findsNothing);
      expect(find.textContaining('Ca làm đã xong'), findsNothing);
    });

    testWidgets(
      'a network failure while accepting is an error, not a made-up payout',
      (tester) async {
        final acceptance = FakeAcceptanceService()
          ..failure = const ApiException(
            0,
            'Không kết nối được máy chủ. Vui lòng kiểm tra mạng và thử lại.',
          );
        await pumpScreen(tester, acceptance: acceptance);

        await tapVisible(tester, find.text('Nghiệm thu'));
        await scrollTo(
          tester,
          find.textContaining('Không kết nối được máy chủ'),
        );

        expect(
          find.textContaining('Không kết nối được máy chủ'),
          findsOneWidget,
        );
        expect(find.text('Đã nghiệm thu'), findsNothing);
        expect(find.textContaining('208000'), findsNothing);
        expect(find.textContaining('260000'), findsNothing);
        expect(find.text('Chi tiết nghiệm thu'), findsNothing);
      },
    );

    testWidgets('an answer without data is an error, not a success', (
      tester,
    ) async {
      final acceptance = FakeAcceptanceService()..emptyAnswer = true;
      await pumpScreen(tester, acceptance: acceptance);

      await tapVisible(tester, find.text('Nghiệm thu'));
      await scrollTo(
        tester,
        find.textContaining('không trả kết quả nghiệm thu'),
      );

      expect(
        find.textContaining('Máy chủ không trả kết quả nghiệm thu'),
        findsOneWidget,
      );
      expect(find.text('Đã nghiệm thu'), findsNothing);
    });

    testWidgets(
      'requesting a redo validates the reason, then shows the status the server answered',
      (tester) async {
        final acceptance = FakeAcceptanceService()
          ..redone = const AssignmentStatusDto(
            assignmentId: 55,
            status: 'IN_PROGRESS',
          );
        await pumpScreen(tester, acceptance: acceptance);

        await tapVisible(tester, find.text('Dọn lại'));
        expect(find.text('Yêu cầu dọn lại (15–30 phút)'), findsOneWidget);

        await tapVisible(tester, find.text('Gửi yêu cầu'));
        await scrollTo(tester, find.text('Nhập chỗ cần làm lại.'));
        expect(find.text('Nhập chỗ cần làm lại.'), findsOneWidget);
        expect(acceptance.redos, isEmpty);

        // NordicTextInput draws its label above the TextField, so find the field inside the labelled input.
        final reasonField = find.descendant(
          of: find.widgetWithText(NordicTextInput, 'Chỗ cần làm lại'),
          matching: find.byType(TextField),
        );
        await tester.enterText(reasonField, 'Chưa lau kính');
        await tapVisible(tester, find.text('Gửi yêu cầu'));

        expect(acceptance.redos.single, (id: 55, reason: 'Chưa lau kính'));
        await scrollTo(tester, find.text('Đang dọn lại'));
        expect(find.text('Đang dọn lại'), findsOneWidget);
      },
    );

    testWidgets(
      'a failed redo request shows the error and leaves the status as it was',
      (tester) async {
        final acceptance = FakeAcceptanceService()
          ..failure = const ApiException(409, 'Invalid state');
        await pumpScreen(tester, acceptance: acceptance);

        await tapVisible(tester, find.text('Dọn lại'));
        final reasonField = find.descendant(
          of: find.widgetWithText(NordicTextInput, 'Chỗ cần làm lại'),
          matching: find.byType(TextField),
        );
        await tester.enterText(reasonField, 'Chưa lau kính');
        await tapVisible(tester, find.text('Gửi yêu cầu'));
        await scrollTo(tester, find.textContaining('không còn chờ nghiệm thu'));

        expect(
          find.textContaining('Ca này không còn chờ nghiệm thu'),
          findsOneWidget,
        );
        await scrollTo(tester, find.text('Chờ nghiệm thu'));
        expect(find.text('Chờ nghiệm thu'), findsOneWidget);
        expect(find.text('Đang dọn lại'), findsNothing);
        expect(
          find.textContaining('Đã ghi nhận yêu cầu dọn lại'),
          findsNothing,
        );
      },
    );
  });

  group('the acceptance route', () {
    testWidgets(
      'without an assignment it does not guess one (no default 2001)',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          MaterialApp(
            routes: WorkersFeatureModule().routes,
            initialRoute: WorkerRoutes.customerAcceptance,
          ),
        );
        await tester.pumpAndSettle();

        expect(
          find.textContaining('Hãy mở màn nghiệm thu từ chi tiết của một đơn'),
          findsOneWidget,
        );
        expect(find.textContaining('2001'), findsNothing);
        expect(find.text('Nghiệm thu'), findsNothing);
      },
    );

    testWidgets('with the arguments of an order it opens that assignment', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        MaterialApp(
          routes: WorkersFeatureModule().routes,
          home: Builder(
            builder: (context) => TextButton(
              onPressed: () => Navigator.of(context).pushNamed(
                WorkerRoutes.customerAcceptance,
                arguments: const CustomerAcceptanceArgs(assignmentId: 77),
              ),
              child: const Text('open'),
            ),
          ),
        ),
      );
      await tester.tap(find.text('open'));
      await tester.pumpAndSettle();

      expect(find.text('Nghiệm thu ca'), findsOneWidget);
    });
  });
}
