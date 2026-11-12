import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/customers/models/customer_profile.dart';
import 'package:mobile/features/customers/screens/customer_onboarding_screen.dart';
import 'package:mobile/features/home/customer_shell.dart';

import '../workers/design_frame.dart';

CustomerProfile _profile(String name) => CustomerProfile(
  customerId: 1,
  phoneNumber: '0988111222',
  fullName: name,
  trustScore: 5.0,
  accountStatus: 'ACTIVE',
);

Widget _shell({required Future<CustomerProfile> Function() loader}) =>
    MaterialApp(
      home: CustomerShell(
        onSignOut: () {},
        tabs: const [
          Text('tab home'),
          Text('tab orders'),
          Text('tab favourites'),
          Text('tab account'),
        ],
        profileLoader: loader,
      ),
    );

void main() {
  group('CustomerShell onboarding check (MOB-ONBOARD-02)', () {
    testWidgets('a customer without a name is taken to the onboarding', (
      tester,
    ) async {
      useDesignFrame(tester);
      var calls = 0;
      await tester.pumpWidget(
        _shell(
          loader: () async {
            calls++;
            return _profile('');
          },
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byType(CustomerOnboardingScreen), findsOneWidget);
      expect(find.text('Chúng tôi nên gọi bạn là gì?'), findsOneWidget);
      expect(calls, 1);
    });

    testWidgets('a name made only of spaces counts as no name', (tester) async {
      useDesignFrame(tester);
      await tester.pumpWidget(_shell(loader: () async => _profile('   ')));
      await tester.pumpAndSettle();

      expect(find.byType(CustomerOnboardingScreen), findsOneWidget);
    });

    testWidgets('a customer with a name stays on the home', (tester) async {
      useDesignFrame(tester);
      await tester.pumpWidget(_shell(loader: () async => _profile('Duy')));
      await tester.pumpAndSettle();

      expect(find.byType(CustomerOnboardingScreen), findsNothing);
      expect(find.text('tab home'), findsOneWidget);
    });

    testWidgets('a failed call never blocks the app', (tester) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        _shell(loader: () => Future.error(Exception('offline'))),
      );
      await tester.pumpAndSettle();

      expect(find.byType(CustomerOnboardingScreen), findsNothing);
      expect(find.text('tab home'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });

    testWidgets(
      'without a loader and with the tabs replaced there is no check',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          MaterialApp(
            home: CustomerShell(
              onSignOut: () {},
              tabs: const [Text('a'), Text('b'), Text('c'), Text('d')],
            ),
          ),
        );
        await tester.pumpAndSettle();

        expect(find.byType(CustomerOnboardingScreen), findsNothing);
      },
    );
  });
}
