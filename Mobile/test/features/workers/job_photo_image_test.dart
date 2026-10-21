import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/workers/models/job_photo_models.dart';
import 'package:mobile/features/workers/services/workers_photos_service.dart';
import 'package:mobile/features/workers/widgets/job_photo_image.dart';

JobPhotoDto photo(String url) => JobPhotoDto(
  photoId: 801,
  assignmentId: 2001,
  photoPhase: 'BEFORE',
  angleNo: 1,
  photoUrl: url,
  volScore: 142.5,
  isAccepted: true,
  uploadedAt: DateTime(2026, 10, 21),
);

/// MOB-PHOTO-01: a stored photo is loaded from the authenticated file endpoint with the Bearer token, never from a bare URL.
void main() {
  late WorkersPhotosService service;

  setUp(() {
    TokenStorage().clear();
    TokenStorage().saveTokens(accessToken: 'tok', refreshToken: 'ref');
    service = WorkersPhotosService(
      apiClient: ApiClient(baseUrl: 'http://api.example:5004'),
    );
  });

  tearDown(() => TokenStorage().clear());

  Widget host(JobPhotoDto? p) => MaterialApp(
    home: Scaffold(
      body: JobPhotoImage(service: service, photo: p, height: 110, width: 160),
    ),
  );

  testWidgets(
    'a stored photo is an Image.network with the file address and the Bearer header',
    (tester) async {
      await tester.pumpWidget(
        host(photo('/api/workers/assignments/2001/photos/801/file')),
      );

      final image = tester.widget<Image>(find.byType(Image));
      final provider = image.image as NetworkImage;
      expect(
        provider.url,
        'http://api.example:5004/api/workers/assignments/2001/photos/801/file',
      );
      expect(provider.headers, {'Authorization': 'Bearer tok'});
    },
  );

  testWidgets(
    'a photo that cannot be loaded shows the placeholder instead of breaking the screen',
    (tester) async {
      await tester.pumpWidget(
        host(photo('/api/workers/assignments/2001/photos/801/file')),
      );
      await tester.pumpAndSettle(); // the test HTTP client answers 400

      expect(tester.takeException(), isNull);
      expect(find.text('Không tải được ảnh'), findsOneWidget);
    },
  );

  testWidgets(
    'an old client URL is not requested at all, so the token cannot reach another host',
    (tester) async {
      await tester.pumpWidget(
        host(photo('https://storage.giupviec.local/jobs/a.jpg')),
      );

      expect(find.byType(Image), findsNothing);
      expect(find.text('Không tải được ảnh'), findsOneWidget);
    },
  );

  testWidgets('no photo says so', (tester) async {
    await tester.pumpWidget(host(null));

    expect(find.byType(Image), findsNothing);
    expect(find.text('Chưa có ảnh'), findsOneWidget);
  });
}
