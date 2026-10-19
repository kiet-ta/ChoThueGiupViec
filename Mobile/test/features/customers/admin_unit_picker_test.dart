import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/customers/models/vn_admin_units.dart';
import 'package:mobile/features/customers/widgets/admin_unit_picker_field.dart';

void main() {
  group('vietnamAdminUnits catalogue', () {
    test('has the 34 provinces and 3,321 units in force since 2025-07-01', () {
      expect(vietnamAdminUnits.length, 34);
      expect(
        vietnamAdminUnits.values.fold<int>(0, (sum, w) => sum + w.length),
        3321,
      );
      expect(vietnamAdminUnits['Thành phố Hồ Chí Minh'], hasLength(168));
      expect(
        vietnamAdminUnits['Thành phố Hồ Chí Minh'],
        contains('Phường Sài Gòn'),
      );
    });

    test(
      'names fit the contract (1-100 chars) and are unique per province',
      () {
        for (final entry in vietnamAdminUnits.entries) {
          expect(entry.key.length, inInclusiveRange(1, 100));
          expect(
            entry.value.toSet().length,
            entry.value.length,
            reason: entry.key,
          );
          for (final ward in entry.value) {
            expect(ward.length, inInclusiveRange(1, 100));
          }
        }
      },
    );
  });

  group('search', () {
    test('ignores case and Vietnamese diacritics', () {
      expect(foldVietnamese('Phường Sài Gòn'), 'phuong sai gon');
      expect(foldVietnamese('ĐẮK LẮK'), 'dak lak');
      expect(matchesAdminUnitQuery('Phường Sài Gòn', 'sai gon'), isTrue);
      expect(matchesAdminUnitQuery('Phường Sài Gòn', 'SÀI'), isTrue);
      expect(matchesAdminUnitQuery('Tỉnh Đắk Lắk', 'dak'), isTrue);
      expect(matchesAdminUnitQuery('Phường Sài Gòn', 'ha noi'), isFalse);
    });

    test('an empty query matches everything', () {
      expect(matchesAdminUnitQuery('Xã Bình Chánh', ''), isTrue);
      expect(matchesAdminUnitQuery('Xã Bình Chánh', '   '), isTrue);
    });
  });
}
