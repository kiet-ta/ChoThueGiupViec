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

  group('matchAdminCity', () {
    test('matches exact, without prefix, and case/accent-insensitive', () {
      expect(matchAdminCity('Thành phố Hồ Chí Minh'), 'Thành phố Hồ Chí Minh');
      expect(matchAdminCity('TP. Hồ Chí Minh'), 'Thành phố Hồ Chí Minh');
      expect(matchAdminCity('ho chi minh'), 'Thành phố Hồ Chí Minh');
      expect(matchAdminCity('Hà Nội'), 'Thành phố Hà Nội');
      expect(matchAdminCity('ha noi'), 'Thành phố Hà Nội');
      expect(matchAdminCity('Đà Nẵng'), 'Thành phố Đà Nẵng');
      expect(matchAdminCity(''), isNull);
      expect(matchAdminCity(null), isNull);
      expect(matchAdminCity('Thành phố Không Tồn Tại'), isNull);
    });
  });

  group('matchAdminWard', () {
    test('matches exact ward and stripped prefix', () {
      const city = 'Thành phố Hồ Chí Minh';
      expect(matchAdminWard(city, 'Phường Bến Thành'), 'Phường Bến Thành');
      expect(matchAdminWard(city, 'Bến Thành'), 'Phường Bến Thành');
      expect(matchAdminWard(city, 'ben thanh'), 'Phường Bến Thành');
      expect(matchAdminWard(city, 'Phường Tân Hưng'), 'Phường Tân Hưng');
      expect(matchAdminWard(city, 'tan hung'), 'Phường Tân Hưng');
    });

    test('matches ward embedded in address line or formatted address', () {
      const city = 'Thành phố Hồ Chí Minh';
      expect(
        matchAdminWard(city, 'Quận 1', addressLine: '123 Lê Lợi, P. Bến Thành'),
        'Phường Bến Thành',
      );
      expect(
        matchAdminWard(
          city,
          'Quận 1',
          formattedAddress: 'Số 1 Đồng Khởi, Phường Sài Gòn, TP.HCM',
        ),
        'Phường Sài Gòn',
      );
      expect(
        matchAdminWard(
          city,
          'Quận 7',
          formattedAddress: '27 Nguyễn Hữu Thọ, Phường Tân Hưng, Quận 7',
        ),
        'Phường Tân Hưng',
      );
    });

    test('returns null when city is invalid or ward cannot be found', () {
      expect(matchAdminWard(null, 'Phường Bến Thành'), isNull);
      expect(
        matchAdminWard('Thành phố Không Tồn Tại', 'Phường Bến Thành'),
        isNull,
      );
      expect(matchAdminWard('Thành phố Hà Nội', 'Phường Không Hề Có'), isNull);
    });
  });
}
