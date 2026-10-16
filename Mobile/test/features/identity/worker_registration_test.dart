import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/identity/models/auth_models.dart';
import 'package:mobile/features/identity/screens/worker_registration_screen.dart';
import 'package:mobile/features/identity/services/api_error.dart';
import 'package:mobile/features/identity/services/auth_service.dart';
import 'package:mobile/features/identity/services/token_storage.dart';

import '../workers/design_frame.dart';
import 'fake_backend.dart';

const _registration = RegistrationRequired(
  isNewUser: true,
  registrationToken: 'reg-token',
  registrationTokenExpiresInSeconds: 1800,
);

void main() {
  late FakeBackend backend;
  late TokenStorage storage;

  // The widget test below initialises the test binding, which answers every HttpClient call with 400: use the real one for the loopback server.
  setUpAll(() => HttpOverrides.global = null);

  setUp(() async {
    backend = FakeBackend();
    await backend.start();
    storage = TokenStorage()..clear();
    storage.saveRegistration(_registration);
  });

  tearDown(() async {
    storage.clear();
    await backend.stop();
  });

  AuthService service() =>
      AuthService(baseUrl: backend.baseUrl, tokenStorage: storage);

  group('workerProfileProblems', () {
    test('a name and 12 digits are enough', () {
      expect(
        AuthService.workerProfileProblems(
          fullName: 'Nguyen Van A',
          nationalId: '079300000001',
        ),
        isEmpty,
      );
    });

    test('an empty name and a short or lettered id are refused', () {
      final problems = AuthService.workerProfileProblems(
        fullName: ' ',
        nationalId: '12345A',
      );

      expect(problems.keys, containsAll(['fullName', 'nationalId']));
    });
  });

  group('registerWorker', () {
    test(
      'posts the profile with the registration token and signs out',
      () async {
        backend.on(
          'POST',
          '/api/workers/register',
          201,
          FakeBackend.ok({'workerId': 5, 'kycStatus': 'PENDING'}),
        );

        await service().registerWorker(
          fullName: ' Nguyen Van A ',
          nationalId: '079300000001',
          address: 'Quan 1',
          bio: '',
        );

        expect(backend.requestBodies['POST /api/workers/register'], {
          'registrationToken': 'reg-token',
          'fullName': 'Nguyen Van A',
          'nationalId': '079300000001',
          'address': 'Quan 1',
        });
        expect(storage.hasRegistrationToken, isFalse);
      },
    );

    test('a 409 is reported with its status', () async {
      backend.on(
        'POST',
        '/api/workers/register',
        409,
        FakeBackend.error('A worker with this phone number is registered.'),
      );

      expect(
        () => service().registerWorker(
          fullName: 'A',
          nationalId: '079300000001',
          address: 'x',
        ),
        throwsA(isA<ApiException>().having((e) => e.statusCode, 'status', 409)),
      );
    });

    test('without a registration token it refuses at once', () async {
      storage.clear();

      expect(
        () => service().registerWorker(
          fullName: 'A',
          nationalId: '079300000001',
          address: 'x',
        ),
        throwsA(isA<ApiException>()),
      );
    });
  });

  testWidgets('the screen refuses a bad id without sending anything', (
    tester,
  ) async {
    useDesignFrame(tester);
    await tester.pumpWidget(
      MaterialApp(home: WorkerRegistrationScreen(authService: service())),
    );

    await tester.enterText(
      find.byKey(const Key('worker-reg-fullName')),
      'Nguyen Van A',
    );
    await tester.enterText(
      find.byKey(const Key('worker-reg-nationalId')),
      '123',
    );
    await tapVisible(tester, find.text('Hoàn tất hồ sơ').last);

    expect(find.text('Số CCCD gồm đúng 12 chữ số.'), findsOneWidget);
    expect(backend.requestBodies, isEmpty);
  });
}
