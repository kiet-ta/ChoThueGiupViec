import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/customers/logic/onboarding_logic.dart';
import 'package:mobile/features/customers/models/customer_address.dart';
import 'package:mobile/features/customers/models/customer_profile.dart';
import 'package:mobile/features/customers/screens/customer_onboarding_screen.dart';
import 'package:mobile/features/customers/services/customer_address_service.dart';
import 'package:mobile/features/customers/services/customer_profile_service.dart';
import 'package:mobile/features/identity/services/api_error.dart';

const _searchHint = 'Tìm (gõ không dấu cũng được)';

/// Counts the calls and can fail them; everything else is the in-memory mock of the real service.
class _Profiles extends CustomerProfileService {
  final names = <String>[];
  Object? failWith;

  _Profiles() : super(isMock: true);

  @override
  Future<CustomerProfile> updateProfile({
    required String fullName,
    String? email,
  }) async {
    if (failWith != null) throw failWith!;
    names.add(fullName);
    return mockProfile.copyWith(fullName: fullName);
  }
}

class _Addresses extends CustomerAddressService {
  final created = <CustomerAddress>[];
  Object? failWith;

  _Addresses() : super(isMock: true);

  @override
  Future<CustomerAddress> createAddress(CustomerAddress input) async {
    if (failWith != null) throw failWith!;
    created.add(input);
    return input;
  }
}

/// Opens the onboarding from a button and keeps the result it pops with.
class _Host extends StatefulWidget {
  final _Profiles profiles;
  final _Addresses addresses;
  final void Function(OnboardingResult?) onResult;

  const _Host({
    required this.profiles,
    required this.addresses,
    required this.onResult,
  });

  @override
  State<_Host> createState() => _HostState();
}

class _HostState extends State<_Host> {
  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Center(
        child: TextButton(
          key: const Key('open'),
          onPressed: () async {
            final result = await Navigator.of(context).push<OnboardingResult>(
              MaterialPageRoute(
                builder: (_) => CustomerOnboardingScreen(
                  profileService: widget.profiles,
                  addressService: widget.addresses,
                ),
              ),
            );
            widget.onResult(result);
          },
          child: const Text('open'),
        ),
      ),
    );
  }
}

Future<void> _open(
  WidgetTester tester, {
  required _Profiles profiles,
  required _Addresses addresses,
  required void Function(OnboardingResult?) onResult,
}) async {
  tester.view.physicalSize = const Size(390, 1600);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  await tester.pumpWidget(
    MaterialApp(
      theme: NordicTheme.lightTheme,
      home: _Host(profiles: profiles, addresses: addresses, onResult: onResult),
    ),
  );
  await tester.tap(find.byKey(const Key('open')));
  await tester.pumpAndSettle();
}

/// The text field inside a keyed `NordicTextInput` or picker (the key is on the wrapper, the label is above the field).
Finder _input(String key) => find.descendant(
  of: find.byKey(Key(key)),
  matching: find.byType(TextFormField),
);

Future<void> _tap(WidgetTester tester, String key) async {
  final finder = find.byKey(Key(key));
  await tester.ensureVisible(finder);
  await tester.pumpAndSettle();
  await tester.tap(finder);
  await tester.pumpAndSettle();
}

Future<void> _typeName(WidgetTester tester, String text) async {
  await tester.enterText(
    find.descendant(
      of: find.byKey(const Key('onboarding-name')),
      matching: find.byType(TextFormField),
    ),
    text,
  );
  await tester.pump();
}

Future<void> _pickWard(WidgetTester tester) async {
  final field = _input('onboarding-ward');
  await tester.ensureVisible(field);
  await tester.pumpAndSettle();
  await tester.tap(field);
  await tester.pumpAndSettle();
  await tester.enterText(
    find.widgetWithText(TextField, _searchHint),
    'an khanh',
  );
  await tester.pumpAndSettle();
  await tester.tap(find.widgetWithText(ListTile, 'Phường An Khánh'));
  await tester.pumpAndSettle();
}

/// Name saved, then the address typed and the ward chosen: on the house type step.
Future<void> _toHouseStep(WidgetTester tester) async {
  await _typeName(tester, 'Anh Duy');
  await _tap(tester, 'onboarding-continue');
  await tester.enterText(
    find.descendant(
      of: find.byKey(const Key('onboarding-address-line')),
      matching: find.byType(TextFormField),
    ),
    '12 Nguyễn Huệ',
  );
  await _pickWard(tester);
  await _tap(tester, 'onboarding-continue');
}

String _stepLabel(WidgetTester tester) =>
    tester.widget<Text>(find.byKey(const Key('onboarding-step-label'))).data!;

void main() {
  late _Profiles profiles;
  late _Addresses addresses;
  OnboardingResult? result;
  var closed = false;

  setUp(() {
    profiles = _Profiles();
    addresses = _Addresses();
    result = null;
    closed = false;
  });

  void onResult(OnboardingResult? r) {
    result = r;
    closed = true;
  }

  group('step 1: the name', () {
    testWidgets(
      'starts on step 1 of 3 with no skip, no back arrow and no sign-out',
      (tester) async {
        await _open(
          tester,
          profiles: profiles,
          addresses: addresses,
          onResult: onResult,
        );

        expect(_stepLabel(tester), 'Bước 1/3');
        expect(find.text('Chúng tôi nên gọi bạn là gì?'), findsOneWidget);
        expect(find.byKey(const Key('onboarding-skip')), findsNothing);
        expect(find.byKey(const Key('onboarding-back')), findsNothing);
        // MOB-ONBOARD-03: no way out of the name step but giving a name
        expect(find.text('Đăng xuất'), findsNothing);
      },
    );

    testWidgets('a blank name is refused on the device and nothing is sent', (
      tester,
    ) async {
      await _open(
        tester,
        profiles: profiles,
        addresses: addresses,
        onResult: onResult,
      );

      await _typeName(tester, '    ');
      await _tap(tester, 'onboarding-continue');

      expect(
        find.text('Cho chúng tôi biết nên gọi bạn là gì.'),
        findsOneWidget,
      );
      expect(_stepLabel(tester), 'Bước 1/3');
      expect(profiles.names, isEmpty);
    });

    testWidgets('saves the cleaned name at once and goes to step 2', (
      tester,
    ) async {
      await _open(
        tester,
        profiles: profiles,
        addresses: addresses,
        onResult: onResult,
      );

      await _typeName(tester, '  Anh   Duy ');
      await _tap(tester, 'onboarding-continue');

      expect(profiles.names, ['Anh Duy']);
      expect(_stepLabel(tester), 'Bước 2/3');
    });

    testWidgets('a server error keeps step 1 and shows the message', (
      tester,
    ) async {
      profiles.failWith = const ApiException(500, 'Máy chủ đang bận.');
      await _open(
        tester,
        profiles: profiles,
        addresses: addresses,
        onResult: onResult,
      );

      await _typeName(tester, 'Duy');
      await _tap(tester, 'onboarding-continue');

      expect(_stepLabel(tester), 'Bước 1/3');
      expect(find.text('Máy chủ đang bận.'), findsOneWidget);
      expect(profiles.names, isEmpty);
    });
  });

  group('step 2: the address', () {
    testWidgets('"Để sau" ends the run: the name is kept, no address', (
      tester,
    ) async {
      await _open(
        tester,
        profiles: profiles,
        addresses: addresses,
        onResult: onResult,
      );
      await _typeName(tester, 'Duy');
      await _tap(tester, 'onboarding-continue');

      await _tap(tester, 'onboarding-skip');

      expect(closed, isTrue);
      expect(result?.nameSaved, isTrue);
      expect(result?.addressSaved, isFalse);
      expect(profiles.names, ['Duy']);
      expect(addresses.created, isEmpty);
    });

    testWidgets('needs the street and the ward before step 3', (tester) async {
      await _open(
        tester,
        profiles: profiles,
        addresses: addresses,
        onResult: onResult,
      );
      await _typeName(tester, 'Duy');
      await _tap(tester, 'onboarding-continue');

      await _tap(tester, 'onboarding-continue');

      expect(_stepLabel(tester), 'Bước 2/3');
      expect(find.text('Nhập số nhà và tên đường.'), findsOneWidget);
      expect(find.text('Chọn phường / xã.'), findsOneWidget);
    });

    testWidgets('back goes to step 1 and the name stays typed', (tester) async {
      await _open(
        tester,
        profiles: profiles,
        addresses: addresses,
        onResult: onResult,
      );
      await _typeName(tester, 'Duy');
      await _tap(tester, 'onboarding-continue');

      await _tap(tester, 'onboarding-back');

      expect(_stepLabel(tester), 'Bước 1/3');
      expect(find.text('Duy'), findsOneWidget);
    });
  });

  group('step 3: the house type', () {
    testWidgets('a full run creates one default address labelled "Nhà"', (
      tester,
    ) async {
      await _open(
        tester,
        profiles: profiles,
        addresses: addresses,
        onResult: onResult,
      );
      await _toHouseStep(tester);
      expect(_stepLabel(tester), 'Bước 3/3');

      await _tap(tester, 'onboarding-type-APARTMENT');
      await _tap(tester, 'onboarding-save-address');

      expect(addresses.created, hasLength(1));
      final a = addresses.created.single;
      expect(a.label, 'Nhà');
      expect(a.addressLine, '12 Nguyễn Huệ');
      expect(a.district, 'Phường An Khánh');
      expect(a.housingType, HousingType.apartment);
      expect(a.floorAreaM2, 60.0);
      expect(a.numFloors, 1);
      expect(a.isDefault, isTrue);
      expect(result?.nameSaved, isTrue);
      expect(result?.addressSaved, isTrue);
    });

    testWidgets('a house with 3 floors sends the floors it was given', (
      tester,
    ) async {
      await _open(
        tester,
        profiles: profiles,
        addresses: addresses,
        onResult: onResult,
      );
      await _toHouseStep(tester);

      await _tap(tester, 'onboarding-type-HOUSE');
      await tester.enterText(_input('onboarding-floors'), '3');
      await tester.pump();
      await _tap(tester, 'onboarding-save-address');

      expect(addresses.created.single.numFloors, 3);
      expect(addresses.created.single.totalAreaM2, 180.0);
    });

    testWidgets('no type chosen: the message shows and nothing is sent', (
      tester,
    ) async {
      await _open(
        tester,
        profiles: profiles,
        addresses: addresses,
        onResult: onResult,
      );
      await _toHouseStep(tester);

      await _tap(tester, 'onboarding-save-address');

      expect(find.text('Chọn loại nhà.'), findsOneWidget);
      expect(addresses.created, isEmpty);
      expect(closed, isFalse);
    });

    testWidgets('choosing a room cuts a bigger area to 30 m2', (tester) async {
      await _open(
        tester,
        profiles: profiles,
        addresses: addresses,
        onResult: onResult,
      );
      await _toHouseStep(tester);

      await _tap(tester, 'onboarding-type-HOUSE');
      await tester.enterText(_input('onboarding-area'), '45');
      await tester.pump();
      await _tap(tester, 'onboarding-type-ROOM');
      await _tap(tester, 'onboarding-save-address');

      expect(addresses.created.single.floorAreaM2, 30.0);
    });

    testWidgets('a room typed bigger than 30 m2 is refused', (tester) async {
      await _open(
        tester,
        profiles: profiles,
        addresses: addresses,
        onResult: onResult,
      );
      await _toHouseStep(tester);

      await _tap(tester, 'onboarding-type-ROOM');
      await tester.enterText(_input('onboarding-area'), '45');
      await tester.pump();
      await _tap(tester, 'onboarding-save-address');

      expect(find.text('Phòng trọ tối đa 30 m².'), findsOneWidget);
      expect(addresses.created, isEmpty);
    });

    testWidgets('"Để sau" here ends the run WITHOUT saving the address', (
      tester,
    ) async {
      await _open(
        tester,
        profiles: profiles,
        addresses: addresses,
        onResult: onResult,
      );
      await _toHouseStep(tester);

      await _tap(tester, 'onboarding-skip');

      expect(closed, isTrue);
      expect(addresses.created, isEmpty);
      expect(result?.nameSaved, isTrue);
      expect(result?.addressSaved, isFalse);
    });

    testWidgets('back to step 2 keeps the street and the ward', (tester) async {
      await _open(
        tester,
        profiles: profiles,
        addresses: addresses,
        onResult: onResult,
      );
      await _toHouseStep(tester);

      await _tap(tester, 'onboarding-back');

      expect(_stepLabel(tester), 'Bước 2/3');
      expect(find.text('12 Nguyễn Huệ'), findsOneWidget);
      expect(find.text('Phường An Khánh'), findsOneWidget);
    });

    testWidgets(
      'a server error keeps step 3, shows it, and "Để sau" still works',
      (tester) async {
        addresses.failWith = const ApiException(500, 'Không lưu được địa chỉ.');
        await _open(
          tester,
          profiles: profiles,
          addresses: addresses,
          onResult: onResult,
        );
        await _toHouseStep(tester);
        await _tap(tester, 'onboarding-type-APARTMENT');

        await _tap(tester, 'onboarding-save-address');

        expect(_stepLabel(tester), 'Bước 3/3');
        expect(find.text('Không lưu được địa chỉ.'), findsOneWidget);
        expect(closed, isFalse);

        await _tap(tester, 'onboarding-skip');
        expect(closed, isTrue);
        expect(result?.addressSaved, isFalse);
      },
    );
  });

  group('layout of the house type step (MOB-ONBOARD-04)', () {
    Future<void> openOnNarrowPhone(WidgetTester tester) async {
      tester.view.physicalSize = const Size(360, 1600);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });
      await tester.pumpWidget(
        MediaQuery(
          data: MediaQueryData.fromView(tester.view)
              .copyWith(textScaler: const TextScaler.linear(1.3)),
          child: MaterialApp(
            theme: NordicTheme.lightTheme,
            home: _Host(
              profiles: profiles,
              addresses: addresses,
              onResult: onResult,
            ),
          ),
        ),
      );
      await tester.tap(find.byKey(const Key('open')));
      await tester.pumpAndSettle();
    }

    testWidgets(
      'the fields are full width with their label above, and the room hint is whole',
      (tester) async {
        await openOnNarrowPhone(tester);
        await _toHouseStep(tester);

        await _tap(tester, 'onboarding-type-ROOM');

        expect(find.text('Diện tích mỗi sàn (m²)'), findsOneWidget);
        expect(find.text('Phòng trọ tối đa 30 m²'), findsOneWidget);
        // the label is a text above the field, not a floating label inside it
        final label = tester.getRect(find.text('Diện tích mỗi sàn (m²)'));
        final field = tester.getRect(_input('onboarding-area'));
        expect(label.bottom, lessThanOrEqualTo(field.top + 1));
        // full width of the screen minus the gutters, not half of it
        expect(field.width, greaterThan(360 / 2 + 40));
        expect(tester.takeException(), isNull);
      },
    );

    testWidgets('the error for a room over 30 m2 is whole too', (tester) async {
      await openOnNarrowPhone(tester);
      await _toHouseStep(tester);

      await _tap(tester, 'onboarding-type-ROOM');
      await tester.enterText(_input('onboarding-area'), '45');
      await tester.pump();
      await _tap(tester, 'onboarding-save-address');

      expect(find.text('Phòng trọ tối đa 30 m².'), findsOneWidget);
      expect(
        tester.getSize(find.text('Phòng trọ tối đa 30 m².')).width,
        lessThan(360),
      );
      expect(tester.takeException(), isNull);
    });

    testWidgets('the floors field is greyed out and read only for a room', (
      tester,
    ) async {
      await openOnNarrowPhone(tester);
      await _toHouseStep(tester);

      await _tap(tester, 'onboarding-type-ROOM');

      expect(
        tester
            .widget<Opacity>(
              find.ancestor(
                of: find.byKey(const Key('onboarding-floors')),
                matching: find.byType(Opacity),
              ),
            )
            .opacity,
        0.5,
      );
      expect(find.text('Cố định 1 tầng'), findsOneWidget);
    });
  });
}
