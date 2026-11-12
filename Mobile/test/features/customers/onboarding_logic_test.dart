import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/customers/logic/address_rules.dart';
import 'package:mobile/features/customers/logic/onboarding_logic.dart';
import 'package:mobile/features/customers/models/customer_address.dart';
import 'package:mobile/features/customers/models/customer_profile.dart';

CustomerProfile _profile(String name) => CustomerProfile(
  customerId: 1,
  phoneNumber: '0988111222',
  fullName: name,
  trustScore: 5.0,
  accountStatus: 'ACTIVE',
);

void main() {
  group('steps', () {
    test('go name, address, house type and stop at both ends', () {
      expect(nextStep(OnboardingStep.name), OnboardingStep.address);
      expect(nextStep(OnboardingStep.address), OnboardingStep.houseType);
      expect(nextStep(OnboardingStep.houseType), isNull);
      expect(previousStep(OnboardingStep.houseType), OnboardingStep.address);
      expect(previousStep(OnboardingStep.address), OnboardingStep.name);
      expect(previousStep(OnboardingStep.name), isNull);
    });

    test('are numbered from 1 out of 3', () {
      expect(OnboardingStep.values.map(stepNumber), [1, 2, 3]);
      expect(onboardingStepCount, OnboardingStep.values.length);
    });
  });

  group('the name the customer wants to be called', () {
    test('is trimmed and runs of spaces become one', () {
      expect(normalizeCallName('  Anh   Duy \n'), 'Anh Duy');
    });

    test('is required and has a friendly message', () {
      expect(callNameError(''), isNotNull);
      expect(callNameError('   '), isNotNull);
      expect(callNameError('Duy'), isNull);
    });

    test('is at most 100 characters after the clean up', () {
      expect(callNameError('a' * 100), isNull);
      expect(callNameError('a' * 101), contains('101'));
      // 49 + 6 spaces + 50 is 100 characters once the spaces are one
      expect(callNameError('${'a' * 49}      ${'b' * 50}'), isNull);
    });
  });

  group('the address line', () {
    test('is required and at most 255 characters', () {
      expect(addressLineError('  '), isNotNull);
      expect(addressLineError('12 Nguyễn Huệ'), isNull);
      expect(addressLineError('x' * 255), isNull);
      expect(addressLineError('x' * 256), isNotNull);
    });
  });

  group('who needs the onboarding', () {
    test('a customer with an empty or blank name does', () {
      expect(needsOnboarding(_profile('')), isTrue);
      expect(needsOnboarding(_profile('   ')), isTrue);
    });

    test('a customer with a name does not', () {
      expect(needsOnboarding(_profile('Duy')), isFalse);
    });
  });

  group('house type rules (shared with the address form)', () {
    test('an apartment and a room are one floor, a house chooses', () {
      expect(effectiveFloors(HousingType.apartment, '5'), 1);
      expect(effectiveFloors(HousingType.room, '5'), 1);
      expect(effectiveFloors(HousingType.house, '3'), 3);
      expect(effectiveFloors(HousingType.house, 'x'), 1);
    });

    test('changing the type fixes the floors and cuts a big room', () {
      expect(floorsTextAfterTypeChange(HousingType.apartment), '1');
      expect(floorsTextAfterTypeChange(HousingType.house), isNull);
      expect(floorAreaTextAfterTypeChange(HousingType.room, 45), '30.0');
      expect(floorAreaTextAfterTypeChange(HousingType.room, 25), isNull);
      expect(floorAreaTextAfterTypeChange(HousingType.house, 45), isNull);
    });

    test(
      'the area must be above 0, at most 9999.99, and at most 30 for a room',
      () {
        expect(floorAreaError(HousingType.house, '0'), isNotNull);
        expect(floorAreaError(HousingType.house, 'abc'), isNotNull);
        expect(floorAreaError(HousingType.house, '10000'), isNotNull);
        expect(floorAreaError(HousingType.house, '9999.99'), isNull);
        expect(floorAreaError(HousingType.room, '31'), isNotNull);
        expect(floorAreaError(HousingType.room, '30'), isNull);
      },
    );

    test(
      'a house has 1 to 10 floors, the other types have nothing to check',
      () {
        expect(floorsError(HousingType.house, '0'), isNotNull);
        expect(floorsError(HousingType.house, '11'), isNotNull);
        expect(floorsError(HousingType.house, '10'), isNull);
        expect(floorsError(HousingType.apartment, 'abc'), isNull);
      },
    );
  });
}
