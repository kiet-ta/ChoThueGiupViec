import 'dart:convert';
import 'dart:io';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/ratings/models/rating_models.dart';
import 'package:mobile/features/ratings/services/ratings_service.dart';

void main() {
  group('RatingsService over a local server', () {
    late HttpServer server;
    late RatingsService service;
    final seen = <({String method, String path, Object? body})>[];

    Future<void> answer(int status, Object? json, {Map<String, String>? headers}) async {
      server.listen((HttpRequest request) async {
        final raw = await utf8.decodeStream(request);
        seen.add((method: request.method, path: request.uri.path, body: raw.isEmpty ? null : jsonDecode(raw)));
        request.response.statusCode = status;
        request.response.headers.contentType = ContentType.json;
        headers?.forEach(request.response.headers.set);
        request.response.write(jsonEncode(json));
        await request.response.close();
      });
    }

    setUp(() async {
      seen.clear();
      TokenStorage().clear();
      TokenStorage().saveTokens(accessToken: 'tok', refreshToken: 'ref');
      server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      service = RatingsService(client: ApiClient(baseUrl: 'http://${server.address.host}:${server.port}'));
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    test('a customer reads the window of an assignment on the customers path', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {'assignmentId': 7, 'canRate': true, 'reason': 'OPEN', 'opensAt': null, 'closesAt': null, 'secondsRemaining': 3600},
      });

      final window = await service.getWindow(RatingRole.customer, 7);

      expect(seen.single.method, 'GET');
      expect(seen.single.path, '/api/customers/me/assignments/7/rating-window');
      expect(window.canRate, isTrue);
      expect(window.secondsRemaining, 3600);
    });

    test('a worker reads the window on the workers path', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {'assignmentId': 9, 'canRate': false, 'reason': 'ALREADY_RATED', 'secondsRemaining': 0},
      });

      final window = await service.getWindow(RatingRole.worker, 9);

      expect(seen.single.path, '/api/workers/me/assignments/9/rating-window');
      expect(window.reason, 'ALREADY_RATED');
    });

    test('posts the rating body to the rating endpoint of the role', () async {
      await answer(201, {'success': true, 'message': 'Rating submitted.', 'data': {'ratingId': 1}});

      await service.submit(
        RatingRole.worker,
        9,
        const RatingSubmission(stars: 4, criteria: {'cooperation': 5, 'workingConditions': 3}, comment: 'ok'),
      );

      expect(seen.single.method, 'POST');
      expect(seen.single.path, '/api/workers/me/assignments/9/rating');
      expect(seen.single.body, {
        'stars': 4,
        'criteria': {'cooperation': 5, 'workingConditions': 3},
        'comment': 'ok',
      });
    });

    test('a 409 is thrown as an ApiException the screen can recognise', () async {
      await answer(409, {'success': false, 'message': 'This assignment has already been rated.', 'data': null});

      await expectLater(
        service.submit(RatingRole.customer, 7, const RatingSubmission(stars: 5, criteria: {'punctuality': 5})),
        throwsA(isA<ApiException>().having((e) => e.isConflict, 'isConflict', isTrue)),
      );
    });

    test('a 400 with field errors keeps its details', () async {
      await answer(400, {
        'success': false,
        'message': 'Validation failed',
        'data': {
          'errors': {
            'stars': ['Stars must be an integer between 1 and 5.']
          }
        },
      });

      await expectLater(
        service.submit(RatingRole.customer, 7, const RatingSubmission(stars: 9, criteria: {})),
        throwsA(isA<ApiException>()
            .having((e) => e.statusCode, 'status', 400)
            .having((e) => e.fieldErrors?['stars']?.first, 'stars error', 'Stars must be an integer between 1 and 5.')),
      );
    });

    test('a window answer without data is an error, not a silent null', () async {
      await answer(200, {'success': true, 'message': '', 'data': null});

      await expectLater(service.getWindow(RatingRole.customer, 7), throwsA(isA<ApiException>()));
    });
  });
}
