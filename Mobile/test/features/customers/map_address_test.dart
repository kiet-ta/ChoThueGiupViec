import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/customers/logic/map_address.dart';
import 'package:mobile/features/customers/models/reverse_geocode_result.dart';

ReverseGeocodeResult read({
  String city = 'Thành phố Hồ Chí Minh',
  String district = '',
  String addressLine = '',
  String formattedAddress = '',
}) => ReverseGeocodeResult(
  city: city,
  district: district,
  addressLine: addressLine,
  formattedAddress: formattedAddress,
  latitude: 10.7724,
  longitude: 106.698,
);

/// ADDR-MAP-01: what the address form takes from the address read at the point tapped on the map.
void main() {
  group('fillFromGeocode', () {
    test('the ward the server reads without its prefix is matched to the catalogue ("Bến Thành" -> "Phường Bến Thành")', () {
      // The answer of the real server for 10.7724, 106.6980 (probed on the Docker kit on 2026-11-14).
      final fill = fillFromGeocode(
        read(
          district: 'Bến Thành',
          addressLine: 'Công trường Quách Thị Trang',
          formattedAddress: 'Công trường Quách Thị Trang, Bến Thành, Thành phố Hồ Chí Minh, Việt Nam',
        ),
        currentCity: 'Thành phố Hồ Chí Minh',
      );
      expect(fill.addressLine, 'Công trường Quách Thị Trang');
      expect(fill.city, 'Thành phố Hồ Chí Minh');
      expect(fill.ward, 'Phường Bến Thành');
    });

    test('another province moves the province and the ward with it', () {
      final fill = fillFromGeocode(
        read(
          city: 'Thành phố Hà Nội',
          district: 'Hoàn Kiếm',
          addressLine: '17 Phố Trần Nguyên Hãn',
        ),
        currentCity: 'Thành phố Hồ Chí Minh',
      );
      expect(fill.city, 'Thành phố Hà Nội');
      expect(fill.ward, 'Phường Hoàn Kiếm');
      expect(fill.addressLine, '17 Phố Trần Nguyên Hãn');
    });

    test(
      'a ward that is not in the catalogue is left empty, never guessed',
      () {
        final fill = fillFromGeocode(
          read(district: 'Khu phố Không Có'),
          currentCity: 'Thành phố Hồ Chí Minh',
        );
        expect(fill.ward, isNull);
      },
    );

    test('an unknown province keeps the current one', () {
      final fill = fillFromGeocode(
        read(city: 'Nowhere'),
        currentCity: 'Thành phố Hồ Chí Minh',
      );
      expect(fill.city, 'Thành phố Hồ Chí Minh');
    });

    test('without a street the full address fills the line; without either the line is left alone', () {
      expect(
        fillFromGeocode(read(formattedAddress: 'Sài Gòn, Việt Nam'))
            .addressLine,
        'Sài Gòn, Việt Nam',
      );
      expect(fillFromGeocode(read()).addressLine, isNull);
    });
  });

  test('placeText shows the full address, else its parts', () {
    expect(placeText(read(formattedAddress: 'A, B, C')), 'A, B, C');
    expect(
      placeText(read(addressLine: 'Điện Biên Phủ', district: 'Thạnh Mỹ Tây')),
      'Điện Biên Phủ, Thạnh Mỹ Tây, Thành phố Hồ Chí Minh',
    );
  });

  test('LatestRequest: only the last request is current', () {
    final latest = LatestRequest();
    final first = latest.next();
    final second = latest.next();
    expect(latest.isLatest(first), isFalse);
    expect(latest.isLatest(second), isTrue);
  });
}
