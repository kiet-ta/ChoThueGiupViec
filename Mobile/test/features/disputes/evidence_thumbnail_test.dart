import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/disputes/widgets/evidence_thumbnail.dart';

void main() {
  group('evidenceImageUrl', () {
    test('makes a server path absolute with the API base address', () {
      expect(
        evidenceImageUrl(
          '/files/dispute-evidence/a.jpg',
          baseUrl: 'http://10.0.2.2:5004',
        ),
        'http://10.0.2.2:5004/files/dispute-evidence/a.jpg',
      );
    });

    test('keeps a full http or https address as it is', () {
      expect(
        evidenceImageUrl(
          'https://files.example/photo.jpg',
          baseUrl: 'http://x',
        ),
        'https://files.example/photo.jpg',
      );
      expect(
        evidenceImageUrl(' http://files.example/p.png ', baseUrl: 'http://x'),
        'http://files.example/p.png',
      );
    });

    test('is null for anything that is not a photo address', () {
      expect(evidenceImageUrl('', baseUrl: 'http://x'), isNull);
      expect(evidenceImageUrl('photo.jpg', baseUrl: 'http://x'), isNull);
      expect(
        evidenceImageUrl('/api/v1/workers/me/bank', baseUrl: 'http://x'),
        isNull,
      );
      expect(
        evidenceImageUrl('file:///etc/passwd', baseUrl: 'http://x'),
        isNull,
      );
    });
  });

  group('EvidenceThumbnail', () {
    Future<void> pump(WidgetTester tester, String url) {
      return tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: SizedBox(
              width: 100,
              height: 100,
              child: EvidenceThumbnail(url: url, index: 1),
            ),
          ),
        ),
      );
    }

    testWidgets('loads the photo from the address', (tester) async {
      await pump(tester, 'https://files.example/photo.jpg');

      final image = tester.widget<Image>(
        find.byKey(const Key('dispute-evidence-image-1')),
      );
      expect(
        (image.image as NetworkImage).url,
        'https://files.example/photo.jpg',
      );
      expect(image.semanticLabel, 'Ảnh bằng chứng 2');
    });

    testWidgets('shows the grey tile when the address is not a photo address', (
      tester,
    ) async {
      await pump(tester, 'photo.jpg');

      expect(find.byKey(const Key('dispute-evidence-image-1')), findsNothing);
      expect(find.text('Ảnh 2'), findsOneWidget);
    });

    testWidgets('falls back to the grey tile when the photo cannot be read', (
      tester,
    ) async {
      // flutter_test answers every image request with an error, which is the "cannot be read" case.
      await pump(tester, 'https://files.example/missing.jpg');
      await tester.pumpAndSettle();

      expect(find.text('Ảnh 2'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });
  });
}
